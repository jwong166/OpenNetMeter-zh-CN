// 简体中文汉化修改：本文件的用户界面文案已译为简体中文，其余逻辑与官方版本一致。
using System;
using System.Linq;
using Avalonia;
using OpenNetMeter.Services;

namespace OpenNetMeter;

internal sealed class Program
{
    /// <summary>Held for the process lifetime; null only before Main runs.</summary>
    internal static SingleInstanceGuard? InstanceGuard { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        // 界面语言与区域设置在 App.OnFrameworkInitializationCompleted 里由 LocalizationService 统一应用。

        // A launch at sign-in (/StartMinimized) shouldn't pop up an already running copy;
        // any other launch (shortcut, Start menu) brings it to the front.
        bool startMinimized = args.Any(a => string.Equals(a, "/StartMinimized", StringComparison.OrdinalIgnoreCase));
        InstanceGuard = SingleInstanceGuard.TryAcquire(activateExisting: !startMinimized);
        if (InstanceGuard == null)
            return;

        using (InstanceGuard)
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
