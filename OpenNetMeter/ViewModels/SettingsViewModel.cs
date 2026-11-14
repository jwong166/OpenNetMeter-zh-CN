// 简体中文汉化修改：本文件的用户界面文案已译为简体中文，其余逻辑与官方版本一致。
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Threading;
using OpenNetMeter.Localization;
using OpenNetMeter.Models;
using OpenNetMeter.Services;
using OpenNetMeter.PlatformAbstractions;
using OpenNetMeter.Properties;
using OpenNetMeter.Utilities;

namespace OpenNetMeter.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private const string RepositoryUri = "https://github.com/Ashfaaq18/OpenNetMeter";
    private const string PrivacyPolicyUri = "https://github.com/Ashfaaq18/OpenNetMeter/blob/main/PRIVACY.md";

    private readonly IMiniWidgetService miniWidgetService;
    private readonly IStartupRegistrationService startupRegistrationService;
    private readonly IExternalLinkService externalLinkService;
    private readonly IThemeService themeService;
    private readonly IFileSaveDialogService fileSaveDialogService;
    private readonly INetworkCaptureService networkCaptureService;
    private readonly Dictionary<string, string> adapterNames = new(StringComparer.OrdinalIgnoreCase);
    private AdapterChoice? selectedAdapterChoice;
    private bool isRebuildingAdapterChoices;
    private bool isPinnedAdapterMissing;
    private string missingAdapterId = string.Empty;
    private int adapterRefreshVersion;
    private bool startWithWindows;
    private bool minimizeOnStart;
    private bool darkMode;
    private bool miniWidgetVisible = true;
    private double miniWidgetTransparency = 20;
    private int selectedNetworkTargetIndex = 2;
    private int selectedSpeedMagnitudeIndex;
    private int selectedSpeedUnitIndex;
    private bool isCheckingForUpdates;
    private bool isUpdateAvailable;
    private string updateStatusMessage = string.Empty;
    private string downloadUrl = string.Empty;
    private int selectedRetentionIndex;
    private bool isExporting;
    private bool isConfirmationOpen;
    private string confirmationMessage = string.Empty;
    private Action? pendingConfirm;
    private Action? pendingCancel;
    private bool miniWidgetAutoHideFullscreen;
    private int selectedProcessScopeIndex;
    private decimal processTopNValue = 10;

    public SettingsViewModel(MiniWidgetViewModel miniWidgetViewModel, IMiniWidgetService miniWidgetService, IStartupRegistrationService startupRegistrationService, IExternalLinkService externalLinkService, IThemeService themeService, IFileSaveDialogService fileSaveDialogService, IFileRevealService fileRevealService, INetworkCaptureService networkCaptureService)
    {
        ExportStatus = new ExportStatusViewModel(fileRevealService);
        this.miniWidgetService = miniWidgetService;
        this.startupRegistrationService = startupRegistrationService;
        this.externalLinkService = externalLinkService;
        this.themeService = themeService;
        this.fileSaveDialogService = fileSaveDialogService;
        this.networkCaptureService = networkCaptureService;
        var settings = SettingsManager.Current;
        startWithWindows = settings.StartWithWin;
        minimizeOnStart = settings.MinimizeOnStart;

        // 程序被移动过后，旧的自启任务会指向不存在的路径（登录时静默失败），这里静默修正。
        if (startWithWindows)
            startupRegistrationService.EnsureUpToDate(minimizeOnStart);
        darkMode = settings.DarkMode;
        miniWidgetVisible = settings.MiniWidgetVisibility;
        miniWidgetTransparency = settings.MiniWidgetTransparentSlider;
        selectedNetworkTargetIndex = settings.NetworkType;
        selectedSpeedMagnitudeIndex = settings.NetworkSpeedMagnitude;
        selectedSpeedUnitIndex = settings.NetworkSpeedFormat;
        selectedRetentionIndex = HistoryRetention.IndexOf(settings.HistoryRetentionDays);

        miniWidgetAutoHideFullscreen = settings.MiniWidgetAutoHideFullscreen;
        selectedProcessScopeIndex = settings.ProcessDisplayMode == 1 ? 1 : 0;
        processTopNValue = Math.Clamp(settings.ProcessTopNCount, 1, 100);

        RefreshLocalizedLists();
        LocalizationService.LanguageChanged += OnLanguageChanged;

        ResetDataCommand = new RelayCommand(ResetData);
        RevealWidgetCommand = new RelayCommand(() => this.miniWidgetService.RevealCentered());
        ExportAllCommand = new RelayCommand(async () => await ExportAllAsync());
        CheckForUpdatesCommand = new RelayCommand(async () => await CheckForUpdatesAsync());
        DownloadUpdateCommand = new RelayCommand(DownloadUpdate);
        OpenRepositoryCommand = new RelayCommand(() => this.externalLinkService.Open(RepositoryUri));
        OpenPrivacyPolicyCommand = new RelayCommand(() => this.externalLinkService.Open(PrivacyPolicyUri));
        ConfirmCommand = new RelayCommand(Confirm);
        CancelConfirmationCommand = new RelayCommand(CancelConfirmation);

        this.miniWidgetService.VisibilityChanged += SyncMiniWidgetVisibility;
        this.themeService.ApplyDarkMode(darkMode);
        this.miniWidgetService.RefreshAppearance(darkMode, (int)Math.Round(miniWidgetTransparency));

        // Show Automatic (and any saved pinned adapter) right away; the real list follows asynchronously.
        ApplyAdapterChoices([]);
        RefreshAdapterChoices();
    }

    public SettingsViewModel()
        : this(new MiniWidgetViewModel(), new PlaceholderMiniWidgetService(), new PlaceholderStartupRegistrationService(), new NoOpExternalLinkService(), new NoOpThemeService(), new PlaceholderFileSaveDialogService(), new PlaceholderFileRevealService(), new PlaceholderNetworkCaptureService())
    {
    }

    public bool StartWithWindows
    {
        get => startWithWindows;
        set
        {
            if (startWithWindows == value)
                return;

            startWithWindows = value;
            SettingsManager.Current.StartWithWin = value;
            SettingsManager.Save();
            startupRegistrationService.SetEnabled(value, MinimizeOnStart);
            OnPropertyChanged(nameof(StartWithWindows));
            OnPropertyChanged(nameof(CanChangeMinimizeOnStart));
        }
    }

    public bool MinimizeOnStart
    {
        get => minimizeOnStart;
        set
        {
            if (minimizeOnStart == value)
                return;
            minimizeOnStart = value;
            SettingsManager.Current.MinimizeOnStart = value;
            SettingsManager.Save();
            OnPropertyChanged(nameof(MinimizeOnStart));
        }
    }

    public bool CanChangeMinimizeOnStart => !StartWithWindows;

    public bool DarkMode
    {
        get => darkMode;
        set
        {
            if (darkMode == value)
                return;
            darkMode = value;
            SettingsManager.Current.DarkMode = value;
            SettingsManager.Save();
            themeService.ApplyDarkMode(value);
            miniWidgetService.RefreshAppearance(value, (int)Math.Round(MiniWidgetTransparency));
            OnPropertyChanged(nameof(DarkMode));
        }
    }

    public bool MiniWidgetVisible
    {
        get => miniWidgetVisible;
        set
        {
            if (miniWidgetVisible == value)
                return;
            miniWidgetVisible = value;
            SettingsManager.Current.MiniWidgetVisibility = value;
            SettingsManager.Save();
            if (value)
                miniWidgetService.Show();
            else
                miniWidgetService.Hide();
            OnPropertyChanged(nameof(MiniWidgetVisible));
        }
    }

    public double MiniWidgetTransparency
    {
        get => miniWidgetTransparency;
        set
        {
            // 滑块给的是连续 double，这里收敛到 1% 步进：
            // 既避免界面显示成 37.8383838%，也保证写盘与应用的值是同一个整数。
            var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            if (miniWidgetTransparency.Equals(rounded))
                return;

            miniWidgetTransparency = rounded;
            SettingsManager.Current.MiniWidgetTransparentSlider = (int)rounded;
            SettingsManager.Save();
            miniWidgetService.RefreshAppearance(DarkMode, (int)rounded);
            OnPropertyChanged(nameof(MiniWidgetTransparency));
        }
    }

    /// <summary>界面语言下拉框的候选项（用各语言的自称，不翻译）。</summary>
    public ObservableCollection<string> LanguageNames { get; } = [];

    public int SelectedNetworkTargetIndex
    {
        get => selectedNetworkTargetIndex;
        set
        {
            if (selectedNetworkTargetIndex == value)
                return;
            selectedNetworkTargetIndex = value;
            SettingsManager.Current.NetworkType = value;
            SettingsManager.Save();
            OnPropertyChanged(nameof(SelectedNetworkTargetIndex));
            OnPropertyChanged(nameof(IsNetworkTargetPrivate));
            OnPropertyChanged(nameof(IsNetworkTargetPublic));
            OnPropertyChanged(nameof(IsNetworkTargetBoth));
        }
    }

    public bool IsNetworkTargetPrivate
    {
        get => SelectedNetworkTargetIndex == 0;
        set
        {
            if (value)
                SelectedNetworkTargetIndex = 0;
        }
    }

    public bool IsNetworkTargetPublic
    {
        get => SelectedNetworkTargetIndex == 1;
        set
        {
            if (value)
                SelectedNetworkTargetIndex = 1;
        }
    }

    public bool IsNetworkTargetBoth
    {
        get => SelectedNetworkTargetIndex == 2;
        set
        {
            if (value)
                SelectedNetworkTargetIndex = 2;
        }
    }

    public ObservableCollection<string> SpeedMagnitudes { get; } = [];

    public int SelectedSpeedMagnitudeIndex
    {
        get => selectedSpeedMagnitudeIndex;
        set
        {
            if (selectedSpeedMagnitudeIndex == value)
                return;
            selectedSpeedMagnitudeIndex = value;
            SettingsManager.Current.NetworkSpeedMagnitude = value;
            SettingsManager.Save();
            OnPropertyChanged(nameof(SelectedSpeedMagnitudeIndex));
        }
    }

    public ObservableCollection<string> SpeedUnits { get; } = [];

    public int SelectedSpeedUnitIndex
    {
        get => selectedSpeedUnitIndex;
        set
        {
            if (selectedSpeedUnitIndex == value)
                return;
            selectedSpeedUnitIndex = value;
            SettingsManager.Current.NetworkSpeedFormat = value;
            SettingsManager.Save();
            OnPropertyChanged(nameof(SelectedSpeedUnitIndex));
        }
    }

    // ---------- 界面语言 ----------

    public int SelectedLanguageIndex
    {
        get
        {
            for (var i = 0; i < LocalizationService.Options.Count; i++)
            {
                if (string.Equals(LocalizationService.Options[i].Code, LocalizationService.CurrentCode, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return 0;
        }
        set
        {
            if (value < 0 || value >= LocalizationService.Options.Count)
                return;

            var code = LocalizationService.Options[value].Code;
            if (string.Equals(code, LocalizationService.CurrentCode, StringComparison.OrdinalIgnoreCase))
                return;

            LocalizationService.SetLanguage(Application.Current, code);
            OnPropertyChanged(nameof(SelectedLanguageIndex));
        }
    }

    // ---------- 迷你挂件行为 ----------

    public bool MiniWidgetAutoHideFullscreen
    {
        get => miniWidgetAutoHideFullscreen;
        set
        {
            if (miniWidgetAutoHideFullscreen == value)
                return;

            miniWidgetAutoHideFullscreen = value;
            SettingsManager.Current.MiniWidgetAutoHideFullscreen = value;
            SettingsManager.Save();
            miniWidgetService.RefreshBehavior();
            OnPropertyChanged(nameof(MiniWidgetAutoHideFullscreen));
        }
    }

    // ---------- 迷你挂件数字颜色 ----------

    private static readonly string[] WidgetTextColorCodes =
    [
        string.Empty, "#FFFFFF", "#D0D0D0", "#111111", "#4AA98C", "#E17717", "#5FA8FF", "#FFD166"
    ];

    private static readonly string[] WidgetTextColorKeys =
    [
        "Color.Auto", "Color.White", "Color.LightGray", "Color.Black",
        "Color.Green", "Color.Orange", "Color.Blue", "Color.Yellow"
    ];

    public ObservableCollection<string> MiniWidgetTextColors { get; } = [];

    public int SelectedMiniWidgetTextColorIndex
    {
        get
        {
            var current = SettingsManager.Current.MiniWidgetTextColor ?? string.Empty;
            for (var i = 0; i < WidgetTextColorCodes.Length; i++)
            {
                if (string.Equals(WidgetTextColorCodes[i], current, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return 0;
        }
        set
        {
            if (value < 0 || value >= WidgetTextColorCodes.Length)
                return;

            var code = WidgetTextColorCodes[value];
            if (string.Equals(code, SettingsManager.Current.MiniWidgetTextColor, StringComparison.OrdinalIgnoreCase))
                return;

            SettingsManager.Current.MiniWidgetTextColor = code;
            SettingsManager.Save();
            miniWidgetService.RefreshAppearance(DarkMode, (int)Math.Round(MiniWidgetTransparency));
            OnPropertyChanged(nameof(SelectedMiniWidgetTextColorIndex));
        }
    }

    // ---------- 概览显示范围 ----------

    public ObservableCollection<string> ProcessScopeOptions { get; } = [];

    public int SelectedProcessScopeIndex
    {
        get => selectedProcessScopeIndex;
        set
        {
            if (value < 0 || value > 1 || selectedProcessScopeIndex == value)
                return;

            selectedProcessScopeIndex = value;
            SettingsManager.Current.ProcessDisplayMode = value;
            SettingsManager.Save();
            OnPropertyChanged(nameof(SelectedProcessScopeIndex));
            OnPropertyChanged(nameof(IsTopNSelected));
            DisplayScopeChanged?.Invoke();
        }
    }

    public bool IsTopNSelected => selectedProcessScopeIndex == 1;

    /// <summary>N 值，允许 1–100；NumericUpDown 传 decimal?，这里收敛后写入设置。</summary>
    public decimal? ProcessTopNValue
    {
        get => processTopNValue;
        set
        {
            if (value is not { } raw)
                return;

            var clamped = Math.Clamp((int)Math.Round(raw), 1, 100);
            if ((int)processTopNValue == clamped)
                return;

            processTopNValue = clamped;
            SettingsManager.Current.ProcessTopNCount = clamped;
            SettingsManager.Save();
            OnPropertyChanged(nameof(ProcessTopNValue));
            DisplayScopeChanged?.Invoke();
        }
    }

    /// <summary>显示范围或 N 变化时通知外壳刷新概览列表。</summary>
    public event Action? DisplayScopeChanged;

    private static void SetItems(ObservableCollection<string> target, IReadOnlyList<string> values)
    {
        while (target.Count < values.Count)
            target.Add(string.Empty);

        while (target.Count > values.Count)
            target.RemoveAt(target.Count - 1);

        for (var i = 0; i < values.Count; i++)
            target[i] = values[i];
    }

    /// <summary>重建所有随语言变化的选项文本，用逐项赋值避免 ComboBox 选区被重置。</summary>
    private void RefreshLocalizedLists()
    {
        SetItems(LanguageNames, LocalizationService.Options.Select(o => o.DisplayName).ToList());
        SetItems(SpeedMagnitudes,
        [
            Loc.S("Set.MagAuto"), Loc.S("Set.MagKilo"), Loc.S("Set.MagMega"), Loc.S("Set.MagGiga")
        ]);
        SetItems(SpeedUnits, [Loc.S("Set.UnitBits"), Loc.S("Set.UnitBytes")]);
        SetItems(RetentionOptions, HistoryRetention.Options.Select(o => HistoryRetention.LabelFor(o.Days)).ToList());
        SetItems(ProcessScopeOptions, [Loc.S("Set.ScopeAll"), Loc.S("Set.ScopeTopN")]);
        SetItems(MiniWidgetTextColors, WidgetTextColorKeys.Select(Loc.S).ToList());
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshLocalizedLists();
        OnPropertyChanged(string.Empty);
    }

    public ObservableCollection<AdapterChoice> AdapterChoices { get; } = [];

    public AdapterChoice? SelectedAdapterChoice
    {
        get => selectedAdapterChoice;
        set
        {
            // The ComboBox clears its selection while the list is rebuilt; ApplyAdapterChoices restores it.
            if (isRebuildingAdapterChoices)
                return;

            if (value == null)
            {
                // Nothing to save; snap the ComboBox back to the current choice.
                OnPropertyChanged(nameof(SelectedAdapterChoice));
                return;
            }

            if (Equals(selectedAdapterChoice, value))
                return;

            selectedAdapterChoice = value;

            // An empty Id is Automatic. For a pinned adapter keep its real display name,
            // not the "(not connected)" label, so it can be shown again later.
            var settings = SettingsManager.Current;
            settings.NetworkAdapterId = value.Id;
            settings.NetworkAdapterName = value.Id.Length == 0
                ? string.Empty
                : adapterNames.GetValueOrDefault(value.Id, value.Label);
            SettingsManager.Save();

            IsPinnedAdapterMissing = value.Id.Length > 0 &&
                string.Equals(value.Id, missingAdapterId, StringComparison.OrdinalIgnoreCase);
            OnPropertyChanged(nameof(SelectedAdapterChoice));

            // Takes effect immediately: the capture service switches adapters or disconnects.
            networkCaptureService.RefreshAdapterSelection();
        }
    }

    /// <summary>
    /// True when a pinned adapter is saved but isn't currently available, i.e. the
    /// "(not connected)" choice is the selected one. Nothing is counted in that state.
    /// </summary>
    public bool IsPinnedAdapterMissing
    {
        get => isPinnedAdapterMissing;
        private set
        {
            if (isPinnedAdapterMissing == value)
                return;
            isPinnedAdapterMissing = value;
            OnPropertyChanged(nameof(IsPinnedAdapterMissing));
        }
    }

    /// <summary>
    /// Reloads the adapter list. Enumeration runs off the UI thread; the collection is rebuilt on it.
    /// </summary>
    public async void RefreshAdapterChoices()
    {
        // Only the latest refresh may rebuild the list, so a slow earlier one can't overwrite it.
        int version = ++adapterRefreshVersion;
        try
        {
            var adapters = await Task.Run(networkCaptureService.GetAvailableAdapters);
            Dispatcher.UIThread.Post(() =>
            {
                if (version == adapterRefreshVersion)
                    ApplyAdapterChoices(adapters);
            });
        }
        catch (Exception ex)
        {
            EventLogger.Error("Failed to refresh network adapter list", ex);
        }
    }

    private void ApplyAdapterChoices(IReadOnlyList<NetworkAdapterOption> adapters)
    {
        var settings = SettingsManager.Current;
        var savedId = settings.NetworkAdapterId ?? string.Empty;

        var choices = new List<AdapterChoice> { new(string.Empty, Loc.S("St.AutoAdapter")) };
        adapterNames.Clear();
        foreach (var adapter in adapters)
        {
            choices.Add(new AdapterChoice(adapter.Id, adapter.DisplayName));
            adapterNames[adapter.Id] = adapter.DisplayName;
        }

        // A pinned adapter that isn't available stays selectable so the saved choice isn't lost.
        bool isMissing = savedId.Length > 0 && !adapterNames.ContainsKey(savedId);
        missingAdapterId = isMissing ? savedId : string.Empty;
        if (isMissing)
        {
            var name = string.IsNullOrWhiteSpace(settings.NetworkAdapterName) ? savedId : settings.NetworkAdapterName;
            adapterNames[savedId] = name;
            choices.Add(new AdapterChoice(savedId, Loc.F("St.AdapterMissingSuffix", name)));
        }

        var selected = choices.FirstOrDefault(c => string.Equals(c.Id, savedId, StringComparison.OrdinalIgnoreCase)) ?? choices[0];

        isRebuildingAdapterChoices = true;
        try
        {
            AdapterChoices.Clear();
            foreach (var choice in choices)
                AdapterChoices.Add(choice);

            selectedAdapterChoice = selected;
        }
        finally
        {
            isRebuildingAdapterChoices = false;
        }

        OnPropertyChanged(nameof(SelectedAdapterChoice));
        IsPinnedAdapterMissing = isMissing;
    }

    public ObservableCollection<string> RetentionOptions { get; } = [];

    public int SelectedRetentionIndex
    {
        get => selectedRetentionIndex;
        set
        {
            if (selectedRetentionIndex == value || value < 0 || value >= HistoryRetention.Options.Length)
                return;

            if (IsConfirmationOpen)
            {
                // Keyboard input can still reach the ComboBox behind the overlay; snap it back.
                OnPropertyChanged(nameof(SelectedRetentionIndex));
                return;
            }

            selectedRetentionIndex = value;
            NotifyRetentionSelectionChanged();

            var (_, newDays) = HistoryRetention.Options[value];
            if (!HistoryRetention.IsShorter(newDays, SettingsManager.Current.HistoryRetentionDays))
            {
                SaveRetention(newDays);
                return;
            }

            OpenConfirmation(
                Loc.F("St.RetentionConfirm", HistoryRetention.LabelFor(newDays)),
                onConfirm: () =>
                {
                    SaveRetention(newDays);
                    RetentionShortened?.Invoke();
                },
                onCancel: () =>
                {
                    selectedRetentionIndex = HistoryRetention.IndexOf(SettingsManager.Current.HistoryRetentionDays);
                    NotifyRetentionSelectionChanged();
                });
        }
    }

    public bool IsForeverRetentionSelected =>
        HistoryRetention.Options[selectedRetentionIndex].Days == HistoryRetention.Forever;

    public string ForeverRetentionWarning =>
        Loc.F("St.RetentionForever", ByteSizeFormatter.FormatBytes(UsageDatabase.GetDatabaseSizeBytes()));

    public ExportStatusViewModel ExportStatus { get; }

    public string VersionText { get; } = FormatVersion(Assembly.GetExecutingAssembly().GetName().Version);

    public bool IsCheckingForUpdates
    {
        get => isCheckingForUpdates;
        private set
        {
            if (isCheckingForUpdates == value)
                return;
            isCheckingForUpdates = value;
            OnPropertyChanged(nameof(IsCheckingForUpdates));
        }
    }

    public bool IsUpdateAvailable
    {
        get => isUpdateAvailable;
        private set
        {
            if (isUpdateAvailable == value)
                return;
            isUpdateAvailable = value;
            OnPropertyChanged(nameof(IsUpdateAvailable));
        }
    }

    public string UpdateStatusMessage
    {
        get => updateStatusMessage;
        private set
        {
            if (updateStatusMessage == value)
                return;
            updateStatusMessage = value;
            OnPropertyChanged(nameof(UpdateStatusMessage));
        }
    }

    public bool IsConfirmationOpen
    {
        get => isConfirmationOpen;
        private set
        {
            if (isConfirmationOpen == value)
                return;
            isConfirmationOpen = value;
            OnPropertyChanged(nameof(IsConfirmationOpen));
        }
    }

    public string ConfirmationMessage
    {
        get => confirmationMessage;
        private set
        {
            if (confirmationMessage == value)
                return;
            confirmationMessage = value;
            OnPropertyChanged(nameof(ConfirmationMessage));
        }
    }

    public ICommand ResetDataCommand { get; }
    public ICommand RevealWidgetCommand { get; }
    public ICommand ExportAllCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }
    public ICommand DownloadUpdateCommand { get; }
    public ICommand OpenRepositoryCommand { get; }
    public ICommand OpenPrivacyPolicyCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CancelConfirmationCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? DeleteAllDataConfirmed;
    public event Action? RetentionShortened;

    public void SyncMiniWidgetVisibility(bool isVisible)
    {
        if (miniWidgetVisible == isVisible)
            return;

        miniWidgetVisible = isVisible;
        SettingsManager.Current.MiniWidgetVisibility = isVisible;
        SettingsManager.Save();
        OnPropertyChanged(nameof(MiniWidgetVisible));
    }

    private void ResetData()
    {
        if (IsConfirmationOpen)
            return;

        OpenConfirmation(
            Loc.S("St.DeleteConfirm"),
            onConfirm: () => DeleteAllDataConfirmed?.Invoke());
    }

    private void NotifyRetentionSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedRetentionIndex));
        OnPropertyChanged(nameof(IsForeverRetentionSelected));
        OnPropertyChanged(nameof(ForeverRetentionWarning));
    }

    private static string FormatVersion(Version? version) =>
        version == null ? string.Empty : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

    private static void SaveRetention(int days)
    {
        SettingsManager.Current.HistoryRetentionDays = days;
        SettingsManager.Save();
    }

    private async Task ExportAllAsync()
    {
        if (isExporting)
            return;

        isExporting = true;
        ExportStatus.Clear();
        try
        {
            string? path = await fileSaveDialogService.PickSavePathAsync(
                Loc.S("Export.UsageTitle"),
                $"OpenNetMeter-usage-{DateTime.Today:yyyyMMdd}",
                UsageExporter.FileTypes);
            if (string.IsNullOrWhiteSpace(path))
                return;

            ExportStatus.ShowProgress();
            int count = await UsageExporter.ExportAsync(UsageDatabase.ResolveDatabasePath(), path);
            ExportStatus.ShowSuccess(count, path);
        }
        catch (Exception ex)
        {
            EventLogger.Error("Error exporting usage data", ex);
            ExportStatus.ShowError();
        }
        finally
        {
            isExporting = false;
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        UpdateStatusMessage = Loc.S("St.UpdateChecking");
        IsUpdateAvailable = false;
        downloadUrl = string.Empty;
        string statusMessage = string.Empty;

        const int minDisplayTimeMs = 2000;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            (Version? latestVersion, string? nextDownloadUrl) = await UpdateChecker.CheckForUpdatesAsync();
            if (latestVersion != null && nextDownloadUrl != null)
            {
                Version? currentVersion = Assembly.GetExecutingAssembly()?.GetName()?.Version;
                if (currentVersion != null && latestVersion > currentVersion)
                {
                    downloadUrl = nextDownloadUrl;
                    statusMessage = Loc.F("St.UpdateAvailable", latestVersion);
                    IsUpdateAvailable = true;
                }
                else
                {
                    statusMessage = Loc.S("St.UpdateLatest");
                }
            }
            else
            {
                statusMessage = Loc.S("St.UpdateError");
            }
        }
        catch (Exception ex)
        {
            statusMessage = Loc.S("St.UpdateError");
            EventLogger.Error("Error checking for updates", ex);
        }
        finally
        {
            stopwatch.Stop();

            int remainingTime = minDisplayTimeMs - (int)stopwatch.ElapsedMilliseconds;
            if (remainingTime > 0)
            {
                await Task.Delay(remainingTime);
            }

            IsCheckingForUpdates = false;
            UpdateStatusMessage = statusMessage;
        }
    }

    private void DownloadUpdate()
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            UpdateStatusMessage = Loc.S("St.UpdateNoDownload");
            return;
        }

        try
        {
            externalLinkService.Open(downloadUrl);
        }
        catch (Exception ex)
        {
            EventLogger.Error("Error launching update download URL", ex);
            UpdateStatusMessage = Loc.S("St.UpdateLaunchError");
        }
    }

    private void OpenConfirmation(string message, Action onConfirm, Action? onCancel = null)
    {
        pendingConfirm = onConfirm;
        pendingCancel = onCancel;
        ConfirmationMessage = message;
        IsConfirmationOpen = true;
    }

    private void Confirm()
    {
        var action = pendingConfirm;
        CloseConfirmation();
        action?.Invoke();
    }

    private void CancelConfirmation()
    {
        var action = pendingCancel;
        CloseConfirmation();
        action?.Invoke();
    }

    private void CloseConfirmation()
    {
        pendingConfirm = null;
        pendingCancel = null;
        IsConfirmationOpen = false;
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class NoOpExternalLinkService : IExternalLinkService
    {
        public void Open(string uri)
        {
        }
    }
}

