using System.IO;
using Microsoft.Win32;

namespace FloatWeather.Services;

/// <summary>
/// 开机自启（注册表 HKCU\Run）。写入当前 exe 路径，卸载时移除。
/// </summary>
public sealed class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "FloatWeather";

    // 用 Environment.ProcessPath：即使单文件发布也是实际 apphost 路径；Assembly.Location 在单文件下为空
    private static string ExePath => Environment.ProcessPath ?? "";

    /// <summary>读取注册表里当前记录的启动命令（无则 null）</summary>
    private static string? GetStoredValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(AppName) as string;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[AutoStart] 读取失败: " + ex.Message);
            return null;
        }
    }

    /// <summary>去引号规范化路径（注册表里存的是带引号的命令行）</summary>
    private static string Normalize(string? value) => value?.Trim().Trim('"') ?? "";

    /// <summary>当前是否已开机自启（记录的路径仍指向当前 exe）</summary>
    public bool IsEnabled => Normalize(GetStoredValue()) == ExePath && ExePath.Length > 0;

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

    /// <summary>
    /// 修复路径漂移：若注册表记录存在但指向的 exe 已不存在（程序被移动/删除/绿色解压换目录），
    /// 则自动把自启项重写到当前 exe 路径——保留用户「之前开启自启」的意图，同时避免自启失效。
    /// 已正确指向当前 exe、或用户本来就未开启（无记录）时不做任何事。
    /// </summary>
    public void RepairIfDrift()
    {
        var stored = GetStoredValue();
        if (stored is null) return;                              // 用户未开启自启，尊重其选择
        if (Normalize(stored) == ExePath) return;                // 路径已正确
        var oldPath = Normalize(stored);
        if (oldPath.Length > 0 && File.Exists(oldPath)) return;  // 旧路径仍有效（另一份安装），不覆盖
        Console.WriteLine("[AutoStart] 检测到自启路径失效，重新注册：" + stored);
        Set(true);
    }
}