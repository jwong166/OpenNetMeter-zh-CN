using System;
using System.Diagnostics;
using System.IO;
using OpenNetMeter.Utilities;

namespace OpenNetMeter.Services;

/// <summary>
/// 由进程名反查可执行文件路径，供“打开文件所在位置”使用。
/// ETW 给出的名字通常带 .exe，而 <see cref="Process.GetProcessesByName(string)"/> 只接受不带扩展名的名字，
/// 所以这里先归一化，再逐个尝试同名的多个实例（同一程序可能开了多个进程）。
/// </summary>
internal static class ProcessLocator
{
    public static string? TryGetExecutablePath(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return null;

        var bareName = Path.GetFileNameWithoutExtension(processName.Trim());
        if (string.IsNullOrEmpty(bareName))
            return null;

        try
        {
            foreach (var process in Process.GetProcessesByName(bareName))
            {
                using (process)
                {
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            return path;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                    {
                        // 受保护进程（系统服务、反作弊等）会拒绝读取 MainModule，换下一个同名进程继续尝试。
                    }
                }
            }
        }
        catch (Exception ex)
        {
            EventLogger.Error($"Failed to resolve executable path for process '{processName}'", ex);
        }

        return null;
    }
}
