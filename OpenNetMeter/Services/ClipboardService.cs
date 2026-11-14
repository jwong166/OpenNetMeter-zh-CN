using System;
using System.Threading.Tasks;
using OpenNetMeter.Utilities;

namespace OpenNetMeter.Services;

/// <summary>
/// 剪贴板写入的薄封装。Avalonia 的剪贴板挂在 TopLevel 上，视图模型拿不到，
/// 因此在 App 启动时把主窗口的写入委托注册进来，视图模型只依赖这个静态入口。
/// </summary>
internal static class ClipboardService
{
    private static Func<string, Task>? writer;

    public static void Register(Func<string, Task>? clipboardWriter) => writer = clipboardWriter;

    /// <summary>写入文本；成功返回 true。无剪贴板或平台异常时返回 false，不抛出。</summary>
    public static async Task<bool> SetTextAsync(string text)
    {
        if (writer is null || string.IsNullOrEmpty(text))
            return false;

        try
        {
            await writer(text);
            return true;
        }
        catch (Exception ex)
        {
            EventLogger.Error("Failed to copy text to the clipboard", ex);
            return false;
        }
    }
}
