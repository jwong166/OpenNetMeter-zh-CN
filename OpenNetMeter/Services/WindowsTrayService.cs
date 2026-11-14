// 简体中文汉化修改：本文件的用户界面文案已译为简体中文，其余逻辑与官方版本一致。
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using OpenNetMeter.Localization;
using OpenNetMeter.Views;
using OpenNetMeter.Utilities;

namespace OpenNetMeter.Services;

public sealed class WindowsTrayService : ITrayService
{
    private readonly TrayIcon trayIcon;
    private readonly IClassicDesktopStyleApplicationLifetime desktop;
    private readonly MainWindow mainWindow;
    private readonly IMiniWidgetService miniWidgetService;

    public WindowsTrayService(
        Application application,
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow mainWindow,
        IMiniWidgetService miniWidgetService)
    {
        this.desktop = desktop;
        this.mainWindow = mainWindow;
        this.miniWidgetService = miniWidgetService;

        using var iconStream = AssetLoader.Open(new Uri("avares://OpenNetMeter/Assets/x48.png"));
        trayIcon = new TrayIcon
        {
            ToolTipText = "OpenNetMeter",
            Icon = new WindowIcon(iconStream),
            IsVisible = true
        };

        trayIcon.Clicked += (_, _) => mainWindow.OpenFromTray();

        BuildMenu();

        TrayIcon.SetIcons(application, new TrayIcons { trayIcon });

        LocalizationService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// 托盘菜单是原生菜单，不受 DynamicResource 影响，因此语言切换后整份重建。
    /// </summary>
    private void BuildMenu()
    {
        var menu = new NativeMenu();

        var resetPositionsItem = new NativeMenuItem(Loc.S("Tray.Reset"));
        resetPositionsItem.Click += (_, _) => mainWindow.ResetWindowPositions();
        menu.Add(resetPositionsItem);

        var showMiniWidgetItem = new NativeMenuItem(Loc.S("Tray.ShowWidget"));
        showMiniWidgetItem.Click += (_, _) => miniWidgetService.Show();
        menu.Add(showMiniWidgetItem);

        menu.Add(new NativeMenuItemSeparator());

        var openItem = new NativeMenuItem(Loc.S("Tray.Open"));
        openItem.Click += (_, _) => mainWindow.OpenFromTray();
        menu.Add(openItem);

        var exitItem = new NativeMenuItem(Loc.S("Tray.Exit"));
        exitItem.Click += (_, _) =>
        {
            mainWindow.PrepareForExit();
            desktop.Shutdown();
        };
        menu.Add(exitItem);

        trayIcon.Menu = menu;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        try
        {
            BuildMenu();
        }
        catch (Exception ex)
        {
            EventLogger.Error("Failed to rebuild tray menu after a language change", ex);
        }
    }

    public void Dispose()
    {
        try
        {
            LocalizationService.LanguageChanged -= OnLanguageChanged;
            trayIcon.IsVisible = false;
            trayIcon.Dispose();
        }
        catch (Exception ex)
        {
            EventLogger.Error("Failed to dispose tray icon", ex);
        }
    }
}
