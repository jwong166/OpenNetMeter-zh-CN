// 简体中文汉化修改：本文件的用户界面文案已译为简体中文，其余逻辑与官方版本一致。
using OpenNetMeter.Localization;
using System;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpenNetMeter.Properties;
using OpenNetMeter.Utilities;

namespace OpenNetMeter.ViewModels;

public sealed class MiniWidgetViewModel : INotifyPropertyChanged
{
    private static readonly Bitmap PinLightImage = LoadBitmap("avares://OpenNetMeter/Assets/pin/pin.png");
    private static readonly Bitmap PinDarkImage = LoadBitmap("avares://OpenNetMeter/Assets/pin/pin-dark.png");
    private static readonly Bitmap UnpinLightImage = LoadBitmap("avares://OpenNetMeter/Assets/pin/unpin.png");
    private static readonly Bitmap UnpinDarkImage = LoadBitmap("avares://OpenNetMeter/Assets/pin/unpin-dark.png");

    private string downloadSpeedText = "35.2 Mbps";
    private string uploadSpeedText = "4.8 Mbps";
    private string currentSessionDownloadText = "1.24 GB";
    private string currentSessionUploadText = "98.4 MB";
    private IBrush backgroundColor = new SolidColorBrush(Color.FromArgb(0xCC, 0x25, 0x25, 0x25));
    private IBrush valueBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
    private IBrush mutedBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0xF0, 0xF0, 0xF0));
    private bool isPinned;
    private bool darkMode;
    private ICommand togglePinnedCommand;

    public string DownloadSpeedText
    {
        get => downloadSpeedText;
        set
        {
            if (downloadSpeedText == value)
                return;
            downloadSpeedText = value;
            OnPropertyChanged(nameof(DownloadSpeedText));
        }
    }

    public string UploadSpeedText
    {
        get => uploadSpeedText;
        set
        {
            if (uploadSpeedText == value)
                return;
            uploadSpeedText = value;
            OnPropertyChanged(nameof(UploadSpeedText));
        }
    }

    public string CurrentSessionDownloadText
    {
        get => currentSessionDownloadText;
        set
        {
            if (currentSessionDownloadText == value)
                return;
            currentSessionDownloadText = value;
            OnPropertyChanged(nameof(CurrentSessionDownloadText));
        }
    }

    public string CurrentSessionUploadText
    {
        get => currentSessionUploadText;
        set
        {
            if (currentSessionUploadText == value)
                return;
            currentSessionUploadText = value;
            OnPropertyChanged(nameof(CurrentSessionUploadText));
        }
    }

    /// <summary>实时数字的颜色，可在设置里自定义；空设置时跟随深浅主题。</summary>
    public IBrush ValueBrush
    {
        get => valueBrush;
        private set
        {
            if (ReferenceEquals(valueBrush, value))
                return;
            valueBrush = value;
            OnPropertyChanged(nameof(ValueBrush));
        }
    }

    /// <summary>“总计”等次要文字的颜色，取主色略降不透明度。</summary>
    public IBrush MutedBrush
    {
        get => mutedBrush;
        private set
        {
            if (ReferenceEquals(mutedBrush, value))
                return;
            mutedBrush = value;
            OnPropertyChanged(nameof(MutedBrush));
        }
    }

    public bool IsPinned
    {
        get => isPinned;
        set
        {
            if (isPinned == value)
                return;
            isPinned = value;
            SettingsManager.Current.MiniWidgetPinned = value;
            SettingsManager.Save();
            OnPropertyChanged(nameof(IsPinned));
            OnPropertyChanged(nameof(PinIconSource));
            OnPropertyChanged(nameof(PinToolTip));
        }
    }

    /// <summary>
    /// 挂件底色。必须是 <see cref="IBrush"/> 而不是字符串：
    /// 绑定管道没有 string→IBrush 的隐式转换，用字符串会让 Border.Background 恒为 null，透明度调节也就无从生效。
    /// </summary>
    public IBrush BackgroundColor
    {
        get => backgroundColor;
        private set
        {
            if (ReferenceEquals(backgroundColor, value))
                return;
            backgroundColor = value;
            OnPropertyChanged(nameof(BackgroundColor));
        }
    }

    public IImage PinIconSource => IsPinned
        ? (darkMode ? UnpinDarkImage : UnpinLightImage)
        : (darkMode ? PinDarkImage : PinLightImage);

    public string PinToolTip => Loc.S(IsPinned ? "St.Unpin" : "St.Pin");

    public ICommand OpenMainWindowCommand { get; private set; } = new RelayCommand(() => { });
    public ICommand HideWidgetCommand { get; private set; } = new RelayCommand(() => { });
    public ICommand TogglePinnedCommand => togglePinnedCommand;

    public MiniWidgetViewModel()
    {
        togglePinnedCommand = new RelayCommand(() => IsPinned = !IsPinned);
        isPinned = SettingsManager.Current.MiniWidgetPinned;
        RefreshBackground(SettingsManager.Current.DarkMode, SettingsManager.Current.MiniWidgetTransparentSlider);
        RefreshTextColor(SettingsManager.Current.DarkMode, SettingsManager.Current.MiniWidgetTextColor);
    }

    public void SetActions(Action openMainWindow, Action hideWidget)
    {
        OpenMainWindowCommand = new RelayCommand(openMainWindow);
        HideWidgetCommand = new RelayCommand(hideWidget);
        OnPropertyChanged(nameof(OpenMainWindowCommand));
        OnPropertyChanged(nameof(HideWidgetCommand));
    }

    public void RefreshBackground(bool darkMode, int transparency)
    {
        var clampedTransparency = Math.Clamp(transparency, 0, 100);
        var alpha = (byte)(((100 - clampedTransparency) * 255) / 100);
        this.darkMode = darkMode;

        var rgb = darkMode ? Color.FromRgb(0x25, 0x25, 0x25) : Color.FromRgb(0xF1, 0xF1, 0xF1);
        BackgroundColor = new SolidColorBrush(Color.FromArgb(alpha, rgb.R, rgb.G, rgb.B));

        OnPropertyChanged(nameof(PinIconSource));
    }

    /// <summary>
    /// 设置数字颜色。<paramref name="colorHex"/> 为空或解析失败时回落到主题默认色，
    /// 因此用户在浅色/深色主题间切换、或把透明度调高后依然能看清。
    /// </summary>
    public void RefreshTextColor(bool darkMode, string? colorHex)
    {
        this.darkMode = darkMode;

        var color = !string.IsNullOrWhiteSpace(colorHex) && Color.TryParse(colorHex, out var parsed)
            ? parsed
            : (darkMode ? Color.FromRgb(0xF0, 0xF0, 0xF0) : Color.FromRgb(0x1F, 0x29, 0x37));

        ValueBrush = new SolidColorBrush(color);
        MutedBrush = new SolidColorBrush(Color.FromArgb(0xCC, color.R, color.G, color.B));

        OnPropertyChanged(nameof(PinIconSource));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static Bitmap LoadBitmap(string assetUri)
    {
        try
        {
            using var assetStream = AssetLoader.Open(new Uri(assetUri));
            return new Bitmap(assetStream);
        }
        catch (Exception ex)
        {
            EventLogger.Error($"Failed to load mini widget pin asset '{assetUri}'", ex);
            throw;
        }
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

