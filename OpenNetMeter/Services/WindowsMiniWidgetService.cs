using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using OpenNetMeter.ViewModels;
using OpenNetMeter.Views;
using OpenNetMeter.Properties;
using OpenNetMeter.Utilities;

namespace OpenNetMeter.Services;

public sealed class WindowsMiniWidgetService : IMiniWidgetService
{
    private readonly Window mainWindow;
    private readonly MiniWidgetViewModel viewModel;
    private readonly MiniWidgetWindow window;
    private readonly WindowsWidgetZOrderHelper zOrderHelper;
    private readonly WindowsFullscreenWatcher fullscreenWatcher;
    private readonly DispatcherTimer blinkTimer;

    /// <summary>闪烁总次数（400 ms 一次，共约 3.2 秒）。</summary>
    private const int BlinkTickCount = 8;
    private int blinkTicks;
    private bool positionTrackingEnabled;
    private bool restoringPosition;

    /// <summary>因前台全屏而被临时隐藏。与用户手动关闭（MiniWidgetVisibility=false）严格区分。</summary>
    private bool autoHidden;
    private bool disposed;

    public event Action<bool>? VisibilityChanged;

    public WindowsMiniWidgetService(MiniWidgetViewModel viewModel, Window mainWindow)
    {
        this.mainWindow = mainWindow;
        this.viewModel = viewModel;
        window = new MiniWidgetWindow
        {
            DataContext = viewModel
        };
        zOrderHelper = new WindowsWidgetZOrderHelper(window);

        fullscreenWatcher = new WindowsFullscreenWatcher();
        fullscreenWatcher.FullscreenChanged += OnFullscreenChanged;

        blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        blinkTimer.Tick += BlinkTimer_Tick;

        viewModel.SetActions(OpenMainWindow, Hide);
        window.Opened += Window_Opened;
        window.PositionChanged += Window_PositionChanged;
    }

    public void Show()
    {
        try
        {
            autoHidden = false;
            ShowWindowInternal();
            VisibilityChanged?.Invoke(true);
        }
        catch (Exception ex)
        {
            EventLogger.Error("Failed to show mini widget window", ex);
        }
    }

    public void Hide()
    {
        try
        {
            autoHidden = false;
            HideWindowInternal();
            VisibilityChanged?.Invoke(false);
        }
        catch (Exception ex)
        {
            EventLogger.Error("Failed to hide mini widget window", ex);
        }
    }

    public void RefreshAppearance(bool darkMode, int transparency)
    {
        viewModel.RefreshBackground(darkMode, transparency);
        viewModel.RefreshTextColor(darkMode, SettingsManager.Current.MiniWidgetTextColor);
    }

    /// <summary>
    /// 把挂件移回当前屏幕中央并闪烁约 3 秒。
    /// 用于分辨率/缩放/透明度调整后挂件跑到屏幕外或看不见的情况。
    /// </summary>
    public void RevealCentered()
    {
        try
        {
            if (!window.IsVisible)
                ShowWindowInternal();
            else
                window.Activate();

            var scaling = window.RenderScaling > 0 ? window.RenderScaling : 1.0;
            var width = Math.Max(1, (int)Math.Round((window.Bounds.Width > 0 ? window.Bounds.Width : window.Width) * scaling));
            var height = Math.Max(1, (int)Math.Round((window.Bounds.Height > 0 ? window.Bounds.Height : window.Height) * scaling));

            var bounds = (window.Screens?.ScreenFromWindow(window) ?? window.Screens?.Primary)?.Bounds;
            if (bounds is { } area)
            {
                restoringPosition = true;
                window.Position = new PixelPoint(
                    area.X + Math.Max(0, (area.Width - width) / 2),
                    area.Y + Math.Max(0, (area.Height - height) / 2));
                restoringPosition = false;
            }

            StartBlink();
        }
        catch (Exception ex)
        {
            EventLogger.Error("Failed to centre the mini widget", ex);
        }
    }

    private void StartBlink()
    {
        blinkTicks = 0;
        window.Opacity = 1;
        blinkTimer.Stop();
        blinkTimer.Start();
    }

    private void BlinkTimer_Tick(object? sender, EventArgs e)
    {
        blinkTicks++;
        window.Opacity = blinkTicks % 2 == 1 ? 0.25 : 1.0;

        if (blinkTicks < BlinkTickCount)
            return;

        blinkTimer.Stop();
        window.Opacity = 1;
    }

    /// <summary>
    /// 设置页改变“全屏自动隐藏”后调用：立即生效，无需重启。
    /// </summary>
    public void RefreshBehavior()
    {
        try
        {
            if (SettingsManager.Current.MiniWidgetAutoHideFullscreen)
            {
                fullscreenWatcher.Start();
            }
            else
            {
                fullscreenWatcher.Stop();

                // 关闭该功能时，如果当前正处于“因全屏而隐藏”的状态，要把挂件放回来。
                if (autoHidden)
                {
                    autoHidden = false;
                    if (SettingsManager.Current.MiniWidgetVisibility)
                        ShowWindowInternal();
                }
            }
        }
        catch (Exception ex)
        {
            EventLogger.Error("Failed to apply mini widget behaviour settings", ex);
        }
    }

    public void ResetPosition(Window mainWindow)
    {
        try
        {
            restoringPosition = true;

            var mainWidth = Math.Max(1, (int)Math.Round(mainWindow.Bounds.Width));
            var mainHeight = Math.Max(1, (int)Math.Round(mainWindow.Bounds.Height));
            var widgetWidth = Math.Max(1, (int)Math.Round(window.Bounds.Width > 0 ? window.Bounds.Width : window.Width));
            var widgetHeight = Math.Max(1, (int)Math.Round(window.Bounds.Height > 0 ? window.Bounds.Height : window.Height));

            var x = mainWindow.Position.X + (mainWidth / 2) - (widgetWidth / 2);
            var y = mainWindow.Position.Y + (mainHeight / 2) - (widgetHeight / 2);

            window.Position = new PixelPoint(x, y);
            SaveWindowPosition();
        }
        catch (Exception ex)
        {
            EventLogger.Error("Failed to reset mini widget window position", ex);
        }
        finally
        {
            restoringPosition = false;
        }
    }

    public void EnsurePositionOnScreen(Window mainWindow)
    {
        try
        {
            if (!SettingsManager.Current.MiniWidgetPositionInitialized || !IsWindowInBounds(window))
                ResetPosition(mainWindow);
        }
        catch (Exception ex)
        {
            EventLogger.Error("Failed to validate mini widget window position", ex);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        try
        {
            fullscreenWatcher.FullscreenChanged -= OnFullscreenChanged;
            fullscreenWatcher.Dispose();
            blinkTimer.Stop();
            zOrderHelper.Dispose();
            window.Close();
        }
        catch (Exception ex)
        {
            EventLogger.Error("Failed to dispose mini widget window", ex);
        }
    }

    private void ShowWindowInternal()
    {
        if (!window.IsVisible)
            window.Show();
        else
            window.Activate();

        zOrderHelper.Start();

        // 窗口重新显示后按当前设置重刷一次底色，避免出现“设置已改、挂件没跟着变”。
        viewModel.RefreshBackground(SettingsManager.Current.DarkMode, SettingsManager.Current.MiniWidgetTransparentSlider);

    }

    private void HideWindowInternal()
    {
        if (window.IsVisible)
            window.Hide();

        zOrderHelper.Stop();
    }

    private void OnFullscreenChanged(bool isFullscreen)
    {
        try
        {
            if (!SettingsManager.Current.MiniWidgetAutoHideFullscreen)
                return;

            if (isFullscreen)
            {
                if (!window.IsVisible)
                    return;

                autoHidden = true;
                HideWindowInternal();
            }
            else
            {
                if (!autoHidden)
                    return;

                autoHidden = false;

                // 期间用户可能已经手动关掉了挂件，此时不要擅自恢复。
                if (SettingsManager.Current.MiniWidgetVisibility)
                    ShowWindowInternal();
            }
        }
        catch (Exception ex)
        {
            EventLogger.Error("Failed to react to fullscreen state change", ex);
        }
    }

    private void OpenMainWindow()
    {
        try
        {
            if (!mainWindow.IsVisible)
                mainWindow.Show();

            if (mainWindow.WindowState == WindowState.Minimized)
                mainWindow.WindowState = WindowState.Normal;

            mainWindow.Activate();
        }
        catch (Exception ex)
        {
            EventLogger.Error("Failed to open main window from mini widget", ex);
        }
    }

    private void Window_Opened(object? sender, EventArgs e)
    {
        try
        {
            if (SettingsManager.Current.MiniWidgetPositionInitialized)
            {
                restoringPosition = true;
                window.Position = new PixelPoint(SettingsManager.Current.MiniWidgetPosX, SettingsManager.Current.MiniWidgetPosY);
                restoringPosition = false;
            }
            else
            {
                SaveWindowPosition();
            }

            positionTrackingEnabled = true;
        }
        catch (Exception ex)
        {
            restoringPosition = false;
            EventLogger.Error("Failed to restore mini widget window position", ex);
        }
    }

    private void Window_PositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (restoringPosition || !positionTrackingEnabled)
            return;

        try
        {
            SaveWindowPosition();
        }
        catch (Exception ex)
        {
            EventLogger.Error("Failed to save mini widget window position", ex);
        }
    }

    private void SaveWindowPosition()
    {
        SettingsManager.Current.MiniWidgetPosX = window.Position.X;
        SettingsManager.Current.MiniWidgetPosY = window.Position.Y;
        SettingsManager.Current.MiniWidgetPositionInitialized = true;
        SettingsManager.Save();
    }

    private static bool IsWindowInBounds(Window target)
    {
        var screens = target.Screens?.All;
        if (screens is null || screens.Count == 0)
            return true;

        var width = Math.Max(1, (int)Math.Round(target.Bounds.Width > 0 ? target.Bounds.Width : target.Width));
        var height = Math.Max(1, (int)Math.Round(target.Bounds.Height > 0 ? target.Bounds.Height : target.Height));
        const int margin = 32;

        foreach (var screen in screens)
        {
            // Use full screen bounds here, not WorkingArea, so a widget intentionally
            // positioned over the taskbar is still considered a valid persisted position.
            var area = screen.Bounds;
            var areaRight = area.X + area.Width;
            var areaBottom = area.Y + area.Height;
            var targetRight = target.Position.X + width;
            var targetBottom = target.Position.Y + height;

            if (area.X < targetRight - margin &&
                areaRight > target.Position.X + margin &&
                area.Y < targetBottom - margin &&
                areaBottom > target.Position.Y + margin)
            {
                return true;
            }
        }

        return false;
    }
}
