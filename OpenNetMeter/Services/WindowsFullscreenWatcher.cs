using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenNetMeter.Services;

/// <summary>
/// 判断“前台窗口是否是一个全屏窗口”。
/// 判据：前台窗口可见、未最小化、不是桌面/任务栏/本进程，且窗口矩形覆盖了它所在显示器的完整矩形。
/// 无边框全屏（游戏常用的 Borderless）同样会被识别；最大化窗口因为让出了任务栏区域，不会被误判。
/// </summary>
internal sealed class WindowsFullscreenWatcher : IDisposable
{
    private const uint MonitorDefaultToNearest = 2;
    private const int GwlStyle = -16;
    private const long WsChild = 0x40000000L;

    private readonly Avalonia.Threading.DispatcherTimer timer;
    private bool? lastState;

    public event Action<bool>? FullscreenChanged;

    public WindowsFullscreenWatcher()
    {
        timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += Timer_Tick;
    }

    public void Start()
    {
        if (!timer.IsEnabled)
            timer.Start();

        Poll();
    }

    public void Stop()
    {
        timer.Stop();
        lastState = null;
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Tick -= Timer_Tick;
    }

    private void Timer_Tick(object? sender, EventArgs e) => Poll();

    private void Poll()
    {
        var state = IsForegroundWindowFullscreen();
        if (lastState == state)
            return;

        lastState = state;
        FullscreenChanged?.Invoke(state);
    }

    internal static bool IsForegroundWindowFullscreen()
    {
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || !IsWindowVisible(foreground) || IsIconic(foreground))
                return false;

            // 桌面、任务栏、以及本程序自己的窗口都不算“全屏应用”。
            var className = GetWindowClassName(foreground);
            if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW")
                return false;

            GetWindowThreadProcessId(foreground, out var processId);
            if (processId == Environment.ProcessId)
                return false;

            // 子窗口（例如任务栏里的按钮）不参与判断。
            var style = GetWindowLongPtr(foreground, GwlStyle).ToInt64();
            if ((style & WsChild) != 0)
                return false;

            if (!GetWindowRect(foreground, out var windowRect))
                return false;

            var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
                return false;

            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
                return false;

            const int tolerance = 2;
            return windowRect.Left <= info.rcMonitor.Left + tolerance
                && windowRect.Top <= info.rcMonitor.Top + tolerance
                && windowRect.Right >= info.rcMonitor.Right - tolerance
                && windowRect.Bottom >= info.rcMonitor.Bottom - tolerance;
        }
        catch
        {
            // 查询失败时按“非全屏”处理，避免挂件被误隐藏。
            return false;
        }
    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        return GetClassName(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));
}
