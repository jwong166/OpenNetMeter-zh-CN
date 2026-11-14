namespace OpenNetMeter.Properties;

public class AppSettings
{
    public bool DarkMode { get; set; }
    public bool StartWithWin { get; set; }
    public bool MinimizeOnStart { get; set; } = true;
    public int WinPosX { get; set; }
    public int WinPosY { get; set; }
    public int WinWidth { get; set; } = 900;
    public int WinHeight { get; set; } = 600;
    public bool MainWindowPositionInitialized { get; set; }
    public bool MiniWidgetVisibility { get; set; } = true;
    public bool MiniWidgetPinned { get; set; }
    public int MiniWidgetPosX { get; set; }
    public int MiniWidgetPosY { get; set; }
    public bool MiniWidgetPositionInitialized { get; set; }
    public int MiniWidgetTransparentSlider { get; set; } = 0;

    public int NetworkType { get; set; } = 2;
    public int NetworkSpeedFormat { get; set; } = 0;
    public int NetworkSpeedMagnitude { get; set; } = 0;

    // Pinned network adapter Id; empty = automatic (the adapter Windows routes internet traffic through).
    public string NetworkAdapterId { get; set; } = string.Empty;
    // Last known display name of the pinned adapter, used to label it while it is disconnected.
    public string NetworkAdapterName { get; set; } = string.Empty;

    public int HistoryRetentionDays { get; set; } = Models.HistoryRetention.DefaultDays;

    /// <summary>UI language code: en-US, zh-CN or zh-TW. Empty = follow the OS UI culture.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Hide the mini widget while a fullscreen window is in the foreground.</summary>
    public bool MiniWidgetAutoHideFullscreen { get; set; }

    /// <summary>User-picked text colour for the mini widget digits. Empty = follow the theme.</summary>
    public string MiniWidgetTextColor { get; set; } = string.Empty;


    /// <summary>0 = show every process, 1 = show only the top N by session total.</summary>
    public int ProcessDisplayMode { get; set; }

    /// <summary>N for <see cref="ProcessDisplayMode"/> = 1, clamped to 1..100.</summary>
    public int ProcessTopNCount { get; set; } = 10;
}
