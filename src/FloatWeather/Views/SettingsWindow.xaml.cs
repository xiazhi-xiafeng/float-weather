using System.Windows;
using System.Windows.Input;
using FloatWeather.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FloatWeather.Views;

/// <summary>设置窗口</summary>
public partial class SettingsWindow : Window
{
    private readonly FloaterWindow _floater;

    public SettingsWindow(SettingsViewModel vm, FloaterWindow floater)
    {
        InitializeComponent();
        DataContext = vm;
        _floater = floater;
        IsVisibleChanged += (_, e) =>
        {
            // 设置窗显示时监听数据源健康状态，隐藏时停止
            if (vm is null) return;
            if (e.NewValue is true) { vm.RefreshHealth(); vm.StartHealthMonitoring(); }
            else vm.StopHealthMonitoring();
        };
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { /* 忽略拖拽异常 */ }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.Save();
            _floater.ApplyConfig(); // 刷新间隔/城市即时生效
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ConfigureQw_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var fields = new List<ProviderFieldRow>
        {
            new() { Label = "项目 ID (Project ID)", Value = vm.QwProjectId },
            new() { Label = "凭据 ID (Credential ID / JWT ID)", Value = vm.QwCredentialId },
            new() { Label = "私钥 (Ed25519 Private Key)", Value = vm.QwPrivateKey, Multiline = true },
            new() { Label = "API Host", Value = vm.QwApiHost },
        };
        var dlg = new ProviderDialogWindow("和风天气", fields) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            vm.QwProjectId = dlg.Values[0];
            vm.QwCredentialId = dlg.Values[1];
            vm.QwPrivateKey = dlg.Values[2];
            vm.QwApiHost = dlg.Values[3];
        }
    }

    private void ConfigureAmap_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var fields = new List<ProviderFieldRow>
        {
            new() { Label = "Web 服务 Key", Value = vm.AmapKey },
        };
        var dlg = new ProviderDialogWindow("高德天气", fields) { Owner = this };
        if (dlg.ShowDialog() == true) vm.AmapKey = dlg.Values[0];
    }

    private void ConfigureOw_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var fields = new List<ProviderFieldRow>
        {
            new() { Label = "API Key", Value = vm.OpenWeatherKey },
        };
        var dlg = new ProviderDialogWindow("OpenWeather", fields) { Owner = this };
        if (dlg.ShowDialog() == true) vm.OpenWeatherKey = dlg.Values[0];
    }

    private void ConfigureSn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var fields = new List<ProviderFieldRow>
        {
            new() { Label = "API Key（心知天气官网申请）", Value = vm.SeniverseKey },
        };
        var dlg = new ProviderDialogWindow("心知天气", fields) { Owner = this };
        if (dlg.ShowDialog() == true) vm.SeniverseKey = dlg.Values[0];
    }
}
