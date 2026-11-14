// 简体中文汉化修改：本文件的用户界面文案已译为简体中文，其余逻辑与官方版本一致。
using OpenNetMeter.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Microsoft.Data.Sqlite;
using OpenNetMeter.Models;
using OpenNetMeter.PlatformAbstractions;
using OpenNetMeter.Services;
using OpenNetMeter.Utilities;

namespace OpenNetMeter.ViewModels;

public sealed class HistoryViewModel : INotifyPropertyChanged
{
    private readonly string dbPath;
    private readonly IProcessIconService processIconService;
    private readonly IExternalLinkService externalLinkService;
    private readonly IFileSaveDialogService fileSaveDialogService;
    private readonly RelayCommand exportCommand;
    private string? selectedProfile;
    private DateTimeOffset? dateStart;
    private DateTimeOffset? dateEnd;
    private long totalDownload;
    private long totalUpload;
    private string? currentSortColumn;
    private bool sortDescending;
    private bool isExporting;
    private DateTime? lastFilteredAt;
    private string? lastFilteredProfile;

    /// <summary>数据库查出的完整结果；<see cref="Rows"/> 只是它按进程名过滤后的投影。</summary>
    private readonly List<HistoryRowViewModel> allRows = [];
    private string processFilter = string.Empty;
    private ProcessDetailViewModel? detail;

    public HistoryViewModel()
        : this(new NoOpProcessIconService(), new NoOpExternalLinkService(), new PlaceholderFileSaveDialogService(), new PlaceholderFileRevealService())
    {
    }

    public HistoryViewModel(IProcessIconService processIconService, IExternalLinkService externalLinkService, IFileSaveDialogService fileSaveDialogService, IFileRevealService fileRevealService)
    {
        this.processIconService = processIconService;
        this.externalLinkService = externalLinkService;
        this.fileSaveDialogService = fileSaveDialogService;
        ExportStatus = new ExportStatusViewModel(fileRevealService);
        dbPath = UsageDatabase.ResolveDatabasePath();

        Profiles = [];
        Rows = [];
        FilterCommand = new RelayCommand(ApplyFilter);
        exportCommand = new RelayCommand(async () => await ExportAsync(), () => !string.IsNullOrWhiteSpace(SelectedProfile) && !isExporting);
        SortRowsCommand = new ParameterRelayCommand(parameter =>
        {
            var column = parameter?.ToString();
            if (string.IsNullOrWhiteSpace(column))
                return;

            SortRows(column);
        });

        ShowDetailCommand = new ParameterRelayCommand(parameter => ShowDetail(parameter?.ToString()));
        CloseDetailCommand = new RelayCommand(CloseDetail);
        ExportDetailCommand = new RelayCommand(async () => await ExportDetailAsync());

        DateStart = DateTimeOffset.Now.Date.AddDays(-7);
        DateEnd = DateTimeOffset.Now.Date;

        LocalizationService.LanguageChanged += OnLanguageChanged;

        LoadProfiles();
        if (Profiles.Count > 0)
        {
            SelectedProfile = Profiles[0];
            ApplyFilter();
        }
    }

    public ObservableCollection<string> Profiles { get; }

    public string? SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (selectedProfile == value)
                return;
            selectedProfile = value;
            OnPropertyChanged(nameof(SelectedProfile));
            ExportStatus.Clear();
            exportCommand.RaiseCanExecuteChanged();
        }
    }

    public DateTimeOffset? DateStart
    {
        get => dateStart;
        set
        {
            if (dateStart == value)
                return;
            dateStart = value;
            OnPropertyChanged(nameof(DateStart));
        }
    }

    public DateTimeOffset? DateEnd
    {
        get => dateEnd;
        set
        {
            if (dateEnd == value)
                return;
            dateEnd = value;
            OnPropertyChanged(nameof(DateEnd));
        }
    }

    public ObservableCollection<HistoryRowViewModel> Rows { get; }

    public string? CurrentSortColumn => currentSortColumn;
    public bool IsSortDescending => sortDescending;

    public long TotalDownload
    {
        get => totalDownload;
        private set
        {
            if (totalDownload == value)
                return;
            totalDownload = value;
            OnPropertyChanged(nameof(TotalDownload));
            OnPropertyChanged(nameof(TotalDownloadText));
        }
    }

    public long TotalUpload
    {
        get => totalUpload;
        private set
        {
            if (totalUpload == value)
                return;
            totalUpload = value;
            OnPropertyChanged(nameof(TotalUpload));
            OnPropertyChanged(nameof(TotalUploadText));
        }
    }

    public string TotalDownloadText => ByteSizeFormatter.FormatBytes(TotalDownload);
    public string TotalUploadText => ByteSizeFormatter.FormatBytes(TotalUpload);

    /// <summary>
    /// The profile the current results were loaded for; it can differ from <see cref="SelectedProfile"/> until the next filter.
    /// </summary>
    public string LastFilteredProfile => lastFilteredProfile ?? string.Empty;

    /// <summary>
    /// When the current results were loaded, e.g. "2026年10月22日 14:05"; empty until a filter has run.
    /// </summary>
    public string LastFilteredTimeText => lastFilteredAt is { } at
        ? at.ToString("g", CultureInfo.CurrentCulture)
        : string.Empty;

    public bool HasLastFiltered => lastFilteredAt != null;

    public ExportStatusViewModel ExportStatus { get; }

    public ICommand FilterCommand { get; }
    public ICommand SortRowsCommand { get; }
    public ICommand ExportCommand => exportCommand;
    public ICommand ShowDetailCommand { get; }
    public ICommand CloseDetailCommand { get; }
    public ICommand ExportDetailCommand { get; }

    /// <summary>按进程名过滤当前结果；为空表示不过滤。</summary>
    public string ProcessFilter
    {
        get => processFilter;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(processFilter, next, StringComparison.Ordinal))
                return;

            processFilter = next;
            OnPropertyChanged(nameof(ProcessFilter));
            ApplyProcessFilter();
        }
    }

    /// <summary>选中进程的按日明细；null 表示明细面板关闭。</summary>
    public ProcessDetailViewModel? Detail
    {
        get => detail;
        private set
        {
            if (ReferenceEquals(detail, value))
                return;

            detail = value;
            OnPropertyChanged(nameof(Detail));
            OnPropertyChanged(nameof(IsDetailOpen));
        }
    }

    public bool IsDetailOpen => detail != null;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Re-reads profiles and the current filter, e.g. after old history was purged.
    /// </summary>
    public void Refresh()
    {
        ReloadProfiles();
        ApplyFilter();
    }

    public void ReloadProfiles()
    {
        string? previousSelection = SelectedProfile;
        LoadProfiles();

        if (!string.IsNullOrWhiteSpace(previousSelection) && Profiles.Contains(previousSelection))
        {
            SelectedProfile = previousSelection;
        }
        else
        {
            SelectedProfile = Profiles.Count > 0 ? Profiles[0] : null;
        }
    }

    public void DeleteAllDbFiles()
    {
        try
        {
            ApplicationDB.CloseSharedConnection();
            // Pooled read-only connections (History, Summary, export) would otherwise keep the file open.
            SqliteConnection.ClearAllPools();
            DeleteIfExists(dbPath);
            DeleteIfExists($"{dbPath}-wal");
            DeleteIfExists($"{dbPath}-shm");
            DeleteIfExists($"{dbPath}-journal");

            Profiles.Clear();
            Rows.Clear();
            allRows.Clear();
            CloseDetail();
            SelectedProfile = null;
            TotalDownload = 0;
            TotalUpload = 0;
            SetLastFiltered(null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EventLogger.Error("Failed to delete usage database file", ex);
        }
    }

    private void LoadProfiles()
    {
        Profiles.Clear();

        if (!File.Exists(dbPath))
            return;

        using var connection = UsageDatabase.OpenReadOnlyConnection(dbPath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name FROM Adapter ORDER BY Name";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
                Profiles.Add(reader.GetString(0));
        }
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        allRows.Clear();
        CloseDetail();
        TotalDownload = 0;
        TotalUpload = 0;

        // Read once: the same profile feeds the query and the "Last filtered" line.
        var profile = SelectedProfile;
        if (string.IsNullOrWhiteSpace(profile) || !File.Exists(dbPath))
            return;

        var (start, end) = GetSelectedRange();

        using var connection = UsageDatabase.OpenReadOnlyConnection(dbPath);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT p.Name, SUM(pd.DataReceived) AS TotalRecv, SUM(pd.DataSent) AS TotalSent " +
            "FROM ProcessDate pd " +
            "JOIN Process p ON p.ID = pd.ProcessID " +
            "JOIN Adapter a ON a.ID = pd.AdapterID " +
            "JOIN Date d ON d.ID = pd.DateID " +
            "WHERE a.Name = @AdapterName " +
            "AND (d.Year * 10000 + d.Month * 100 + d.Day) BETWEEN @StartDate AND @EndDate " +
            "GROUP BY p.ID, p.Name " +
            "ORDER BY p.Name";
        command.Parameters.AddWithValue("@AdapterName", profile);
        command.Parameters.AddWithValue("@StartDate", UsageDatabase.ToDateInt(start));
        command.Parameters.AddWithValue("@EndDate", UsageDatabase.ToDateInt(end));

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var processName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            var download = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
            var upload = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);

            var icon = processIconService.GetProcessIcon(processName) as IImage;
            allRows.Add(new HistoryRowViewModel(processName, download, upload, externalLinkService, icon, ShowDetail));
            TotalDownload += download;
            TotalUpload += upload;
        }

        ApplyProcessFilter();
        SetLastFiltered(profile, DateTime.Now);
    }

    private void SetLastFiltered(string? profile, DateTime? at)
    {
        if (lastFilteredProfile == profile && lastFilteredAt == at)
            return;
        lastFilteredProfile = profile;
        lastFilteredAt = at;
        OnPropertyChanged(nameof(LastFilteredProfile));
        OnPropertyChanged(nameof(LastFilteredTimeText));
        OnPropertyChanged(nameof(HasLastFiltered));
    }

    private void SortRows(string column)
    {
        sortDescending = string.Equals(currentSortColumn, column, StringComparison.Ordinal) ? !sortDescending : false;
        currentSortColumn = column;

        OnPropertyChanged(nameof(CurrentSortColumn));
        OnPropertyChanged(nameof(IsSortDescending));

        ApplyProcessFilter();
    }

    /// <summary>按进程名过滤并对全量结果重新排序，再投影到界面集合。</summary>
    private void ApplyProcessFilter()
    {
        Rows.Clear();

        IEnumerable<HistoryRowViewModel> query = allRows;

        var needle = processFilter.Trim();
        if (needle.Length > 0)
            query = query.Where(r => r.ProcessName.Contains(needle, StringComparison.OrdinalIgnoreCase));

        query = currentSortColumn switch
        {
            "Download" => sortDescending
                ? query.OrderByDescending(r => r.DownloadBytes)
                : query.OrderBy(r => r.DownloadBytes),
            "Upload" => sortDescending
                ? query.OrderByDescending(r => r.UploadBytes)
                : query.OrderBy(r => r.UploadBytes),
            "Process" => sortDescending
                ? query.OrderByDescending(r => r.ProcessName, StringComparer.OrdinalIgnoreCase)
                : query.OrderBy(r => r.ProcessName, StringComparer.OrdinalIgnoreCase),
            _ => query
        };

        foreach (var row in query)
            Rows.Add(row);
    }

    private void ShowDetail(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(SelectedProfile))
            return;

        var (start, end) = GetSelectedRange();
        Detail = ProcessDetailViewModel.Load(dbPath, SelectedProfile!, processName!, start, end);
    }

    private void CloseDetail() => Detail = null;

    private async Task ExportDetailAsync()
    {
        var profile = SelectedProfile;
        if (string.IsNullOrWhiteSpace(profile) || isExporting)
            return;

        isExporting = true;
        try
        {
            var (start, end) = GetSelectedRange();
            string? path = await fileSaveDialogService.PickSavePathAsync(
                Loc.S("Export.DetailTitle"),
                SanitizeFileName($"OpenNetMeter-detail-{profile}-{start:yyyyMMdd}-{end:yyyyMMdd}"),
                UsageExporter.FileTypes);
            if (string.IsNullOrWhiteSpace(path))
                return;

            ExportStatus.ShowProgress();
            int count = await UsageDetailExporter.ExportAsync(
                dbPath, path, profile, start, end, processFilter.Trim().Length > 0 ? processFilter.Trim() : null);
            ExportStatus.ShowSuccess(count, path);
        }
        catch (Exception ex)
        {
            EventLogger.Error("Error exporting usage detail", ex);
            ExportStatus.ShowError();
        }
        finally
        {
            isExporting = false;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(string.Empty);

        // 明细表里的日期格式也随语言变化，重新载入当前详情。
        if (detail is { } current && !string.IsNullOrWhiteSpace(SelectedProfile))
        {
            var (start, end) = GetSelectedRange();
            Detail = ProcessDetailViewModel.Load(dbPath, SelectedProfile!, current.ProcessName, start, end);
        }
    }

    private (DateTime Start, DateTime End) GetSelectedRange()
    {
        var start = (DateStart ?? DateTimeOffset.Now.Date).Date;
        var end = (DateEnd ?? DateTimeOffset.Now.Date).Date;
        return end < start ? (end, start) : (start, end);
    }

    private async Task ExportAsync()
    {
        var profile = SelectedProfile;
        if (string.IsNullOrWhiteSpace(profile) || isExporting)
            return;

        isExporting = true;
        ExportStatus.Clear();
        exportCommand.RaiseCanExecuteChanged();
        try
        {
            var (start, end) = GetSelectedRange();
            string? path = await fileSaveDialogService.PickSavePathAsync(
                Loc.S("Export.HistoryTitle"),
                SanitizeFileName($"OpenNetMeter-{profile}-{start:yyyyMMdd}-{end:yyyyMMdd}"),
                UsageExporter.FileTypes);
            if (string.IsNullOrWhiteSpace(path))
                return;

            ExportStatus.ShowProgress();
            int count = await UsageExporter.ExportAsync(dbPath, path, profile, start, end);
            ExportStatus.ShowSuccess(count, path);
        }
        catch (Exception ex)
        {
            EventLogger.Error("Error exporting usage history", ex);
            ExportStatus.ShowError();
        }
        finally
        {
            isExporting = false;
            exportCommand.RaiseCanExecuteChanged();
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(fileName.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class NoOpProcessIconService : IProcessIconService
    {
        public object? GetProcessIcon(string processName) => null;
    }

    private sealed class NoOpExternalLinkService : IExternalLinkService
    {
        public void Open(string uri) { }
    }
}

public sealed class HistoryRowViewModel
{
    private readonly IExternalLinkService externalLinkService;

    public HistoryRowViewModel(
        string processName,
        long downloadBytes,
        long uploadBytes,
        IExternalLinkService externalLinkService,
        IImage? icon = null,
        Action<string>? showDetail = null)
    {
        ProcessName = processName;
        DownloadBytes = downloadBytes;
        UploadBytes = uploadBytes;
        this.externalLinkService = externalLinkService;
        Icon = icon;
        SearchProcessCommand = new RelayCommand(() =>
        {
            var encodedName = Uri.EscapeDataString(ProcessName);
            externalLinkService.Open($"https://www.google.com/search?q={encodedName}");
        });

        // 行内“查看明细”由历史页注入，录制结果等其它使用方不传即无动作。
        ShowDetailCommand = new RelayCommand(() => showDetail?.Invoke(ProcessName));
    }

    public IImage? Icon { get; }
    public string ProcessName { get; }
    public long DownloadBytes { get; }
    public long UploadBytes { get; }
    public ICommand SearchProcessCommand { get; }
    public ICommand ShowDetailCommand { get; }
    public string DownloadText => ByteSizeFormatter.FormatBytes(DownloadBytes);
    public string UploadText => ByteSizeFormatter.FormatBytes(UploadBytes);
}

/// <summary>某个进程在所选日期范围内的按日明细。</summary>
public sealed class ProcessDetailViewModel
{
    private ProcessDetailViewModel(string processName, IReadOnlyList<ProcessDayRowViewModel> days)
    {
        ProcessName = processName;
        Days = days;
    }

    public string ProcessName { get; }
    public IReadOnlyList<ProcessDayRowViewModel> Days { get; }
    public bool HasRows => Days.Count > 0;
    public string TotalDownloadText => ByteSizeFormatter.FormatBytes(Days.Sum(d => d.DownloadBytes));
    public string TotalUploadText => ByteSizeFormatter.FormatBytes(Days.Sum(d => d.UploadBytes));

    internal static ProcessDetailViewModel Load(string dbPath, string adapter, string processName, DateTime start, DateTime end)
    {
        var days = new List<ProcessDayRowViewModel>();

        try
        {
            if (File.Exists(dbPath))
            {
                using var connection = UsageDatabase.OpenReadOnlyConnection(dbPath);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT d.Year, d.Month, d.Day, SUM(pd.DataReceived), SUM(pd.DataSent) " +
                    "FROM ProcessDate pd " +
                    "JOIN Process p ON p.ID = pd.ProcessID " +
                    "JOIN Adapter a ON a.ID = pd.AdapterID " +
                    "JOIN Date d ON d.ID = pd.DateID " +
                    "WHERE a.Name = @AdapterName AND p.Name = @ProcessName " +
                    "AND (d.Year * 10000 + d.Month * 100 + d.Day) BETWEEN @StartDate AND @EndDate " +
                    "GROUP BY d.Year, d.Month, d.Day";
                command.Parameters.AddWithValue("@AdapterName", adapter);
                command.Parameters.AddWithValue("@ProcessName", processName);
                command.Parameters.AddWithValue("@StartDate", UsageDatabase.ToDateInt(start));
                command.Parameters.AddWithValue("@EndDate", UsageDatabase.ToDateInt(end));

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var date = new DateOnly(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
                    var download = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
                    var upload = reader.IsDBNull(4) ? 0 : reader.GetInt64(4);
                    days.Add(new ProcessDayRowViewModel(date, download, upload));
                }
            }
        }
        catch (Exception ex)
        {
            EventLogger.Error($"Failed to load daily detail for process '{processName}'", ex);
        }

        days.Sort((a, b) => a.Date.CompareTo(b.Date));
        return new ProcessDetailViewModel(processName, days);
    }
}

/// <summary>明细表的一行：某一天的下载/上传/合计。</summary>
public sealed record ProcessDayRowViewModel(DateOnly Date, long DownloadBytes, long UploadBytes)
{
    public string DateText => Date.ToString("d", CultureInfo.CurrentCulture);
    public string DownloadText => ByteSizeFormatter.FormatBytes(DownloadBytes);
    public string UploadText => ByteSizeFormatter.FormatBytes(UploadBytes);
    public string TotalText => ByteSizeFormatter.FormatBytes(DownloadBytes + UploadBytes);
}
