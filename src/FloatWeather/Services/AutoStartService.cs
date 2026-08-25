using Microsoft.Win32;

namespace FloatWeather.Services;

/// <summary>
/// 开机自启（注册表 HKCU\Run）。写入当前 exe 路径，卸载时移除。
/// </summary>
public sealed class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "FloatWeather";

    private static string ExePath =>
        Environment.ProcessPath ?? System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "";

    /// <summary>当前是否已开机自启</summary>
    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
                return key?.GetValue(AppName) is string v && v == ExePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[AutoStart] 读取失败: " + ex.Message);
                return false;
            }
        }
    }

    public void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                if (key is not null) key.SetValue(AppName, "\"" + ExePath + "\"");
            }
            else
            {
                key?.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[AutoStart] 写入失败: " + ex.Message);
        }
    }
}