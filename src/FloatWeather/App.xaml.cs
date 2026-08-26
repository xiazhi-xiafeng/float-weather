using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using FloatWeather.Providers;
using FloatWeather.Services;
using FloatWeather.ViewModels;
using FloatWeather.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FloatWeather;

/// <summary>
/// 应用入口：构建 DI 容器、日志、并显示悬浮小组件。
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>全局服务容器</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    // 命名互斥体：保证同一会话内仅允许一个实例（Local 前缀按会话隔离）
    private const string SingleInstanceName = @"Local\FloatWeather_SingleInstance";
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 单实例：若已有一个实例在运行，则直接退出；现有实例负责展现自身
        _singleInstanceMutex = new Mutex(true, SingleInstanceName, out bool createdNew);
        if (!createdNew)
        {
            Log.Information("检测到已存在的实例，本实例退出");
            Shutdown();
            return;
        }

        // 全局异常兜底：记录堆栈，避免静默崩溃退出
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "未处理 UI 异常");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error(args.ExceptionObject as Exception, "未处理 AppDomain 异常");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "未观察任务异常");
            args.SetObserved();
        };

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile(FloatWeather.Services.ConfigService.UserConfigFile, optional: true, reloadOnChange: true)
            .Build();

        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "logs", "floatweather-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging(builder => builder.AddSerilog(Log.Logger));

        // 和风接口使用 Gzip 压缩，需开启自动解压
        services.AddHttpClient("qweather")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });
        services.AddHttpClient();
        services.AddHttpClient("icons").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });

        // 数据源注册（按注入顺序即优先级，默认主源）：需 Key 的稳定源在前（配置后优先），免 Key 源随后，
        // 中国天气网接口脆弱、明文 http，压至末位兜底
        services.AddSingleton<IWeatherProvider, QWeatherProvider>();
        services.AddSingleton<IWeatherProvider, AmapProvider>();
        services.AddSingleton<IWeatherProvider, SeniverseProvider>();
        services.AddSingleton<IWeatherProvider, OpenWeatherProvider>();
        services.AddSingleton<IWeatherProvider, OpenMeteoProvider>();
        services.AddSingleton<IWeatherProvider, WttrInProvider>();
        services.AddSingleton<IWeatherProvider, ChinaWeatherProvider>();

        // 服务
        services.AddSingleton<ConfigService>();
        services.AddSingleton<CityResolver>();
        services.AddSingleton<IpLocationService>();
        services.AddSingleton<IconService>();
        services.AddSingleton<UiStateService>();
        services.AddSingleton<AutoStartService>();
        services.AddSingleton<SourceManager>();
        services.AddSingleton<WeatherService>();

        // 视图模型 / 窗口
        services.AddSingleton<FloaterViewModel>();
        services.AddSingleton<DetailViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<FloaterWindow>();
        services.AddSingleton<SettingsWindow>();

        Services = services.BuildServiceProvider();

        // 修复开机自启路径漂移（程序被移动/换目录后自动重注册到当前 exe）
        Services.GetRequiredService<AutoStartService>().RepairIfDrift();

        var floater = Services.GetRequiredService<FloaterWindow>();
        floater.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}