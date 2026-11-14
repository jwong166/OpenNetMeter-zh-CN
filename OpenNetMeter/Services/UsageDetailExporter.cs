using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OpenNetMeter.Localization;
using OpenNetMeter.PlatformAbstractions;
using OpenNetMeter.Utilities;
using OpenNetMeter.ViewModels;

namespace OpenNetMeter.Services;

/// <summary>长表的一行：某进程某天的某个方向。</summary>
internal sealed record UsageDetailRow(
    DateOnly Date,
    string Process,
    bool IsDownload,
    long DayTotalBytes,
    long CumulativeBytes);

/// <summary>
/// 历史流量明细导出（长表）。
/// 与“按天/按进程”的宽表不同，这里每个（进程, 日期, 方向）一行，便于在 Excel 里做透视。
///
/// 两个累计值的口径：
/// - <see cref="UsageDetailRow.DayTotalBytes"/>：该进程在该日期、该方向当天的总量。
/// - <see cref="UsageDetailRow.CumulativeBytes"/>：该进程该方向从**最早一条记录**到该日期（含）的累计值，
///   不受导出时所选日期范围的下限影响，因此同一天在不同时间段导出结果一致。
/// </summary>
internal static class UsageDetailExporter
{
    /// <summary>CSV/JSON 的字段名保持英文，避免破坏下游脚本；显示语言只影响 HTML 报告。</summary>
    private const string CsvHeader = "Date,Adapter,Process,Direction,DayTotalBytes,CumulativeBytes";

    public static List<UsageDetailRow> BuildRows(
        string dbPath,
        string adapter,
        DateTime rangeStart,
        DateTime rangeEnd,
        string? processFilter = null)
    {
        var result = new List<UsageDetailRow>();
        if (string.IsNullOrWhiteSpace(adapter) || !File.Exists(dbPath))
            return result;

        var daily = ReadDailyTotals(dbPath, adapter, processFilter);
        if (daily.Count == 0)
            return result;

        var from = DateOnly.FromDateTime(rangeStart);
        var to = DateOnly.FromDateTime(rangeEnd);
        if (to < from)
            (from, to) = (to, from);

        foreach (var byProcess in daily.GroupBy(d => d.Process, StringComparer.OrdinalIgnoreCase))
        {
            long cumulativeDownload = 0;
            long cumulativeUpload = 0;

            foreach (var day in byProcess.OrderBy(d => d.Date))
            {
                cumulativeDownload += day.Download;
                cumulativeUpload += day.Upload;

                if (day.Date < from || day.Date > to)
                    continue;

                if (day.Download > 0)
                    result.Add(new UsageDetailRow(day.Date, byProcess.Key, true, day.Download, cumulativeDownload));

                if (day.Upload > 0)
                    result.Add(new UsageDetailRow(day.Date, byProcess.Key, false, day.Upload, cumulativeUpload));
            }
        }

        return result
            .OrderBy(r => r.Date)
            .ThenBy(r => r.Process, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(r => r.IsDownload)
            .ToList();
    }

    public static Task<int> ExportAsync(
        string dbPath,
        string outputPath,
        string adapter,
        DateTime rangeStart,
        DateTime rangeEnd,
        string? processFilter = null)
    {
        return Task.Run(() =>
        {
            var rows = BuildRows(dbPath, adapter, rangeStart, rangeEnd, processFilter);

            UsageExporter.WriteFileAtomically(outputPath, stream =>
            {
                if (string.Equals(Path.GetExtension(outputPath), ".json", StringComparison.OrdinalIgnoreCase))
                {
                    WriteJson(stream, rows, adapter);
                }
                else if (HtmlReport.IsHtmlPath(outputPath))
                {
                    using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    WriteHtml(writer, rows, adapter, rangeStart, rangeEnd);
                }
                else
                {
                    using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                    WriteCsv(writer, rows, adapter);
                }
            });

            return rows.Count;
        });
    }

    public static void WriteCsv(TextWriter writer, IEnumerable<UsageDetailRow> rows, string adapter)
    {
        writer.WriteLine(CsvHeader);
        foreach (var row in rows)
        {
            writer.Write(row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(UsageExporter.EscapeCsv(adapter));
            writer.Write(',');
            writer.Write(UsageExporter.EscapeCsv(row.Process));
            writer.Write(',');
            writer.Write(row.IsDownload ? "Download" : "Upload");
            writer.Write(',');
            writer.Write(row.DayTotalBytes.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.WriteLine(row.CumulativeBytes.ToString(CultureInfo.InvariantCulture));
        }
    }

    public static void WriteJson(Stream stream, IEnumerable<UsageDetailRow> rows, string adapter)
    {
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("exportedAt", DateTimeOffset.Now);
        writer.WriteString("adapter", adapter);
        writer.WriteStartArray("rows");
        foreach (var row in rows)
        {
            writer.WriteStartObject();
            writer.WriteString("date", row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteString("process", row.Process);
            writer.WriteString("direction", row.IsDownload ? "download" : "upload");
            writer.WriteNumber("dayTotalBytes", row.DayTotalBytes);
            writer.WriteNumber("cumulativeBytes", row.CumulativeBytes);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    public static void WriteHtml(TextWriter writer, IReadOnlyList<UsageDetailRow> rows, string adapter, DateTime rangeStart, DateTime rangeEnd)
    {
        HtmlReport.WriteDocumentStart(writer, Loc.S("Rep.DetailTitle"));
        HtmlReport.WriteMeta(writer,
        [
            (Loc.S("Rep.Profile"), adapter),
            (Loc.S("Rep.Period"), $"{HtmlReport.FormatDate(DateOnly.FromDateTime(rangeStart))} \u2013 {HtmlReport.FormatDate(DateOnly.FromDateTime(rangeEnd))}"),
            (Loc.S("Common.Total"), ByteSizeFormatter.FormatBytes(rows.Sum(r => r.DayTotalBytes)))
        ]);

        if (rows.Count == 0)
        {
            HtmlReport.WriteNoTraffic(writer);
            HtmlReport.WriteDocumentEnd(writer);
            return;
        }

        writer.WriteLine("<div class=\"scroll\">");
        writer.WriteLine("<table>");
        writer.WriteLine(
            $"<thead><tr><th>{HtmlReport.Encode(Loc.S("Rep.ColDate"))}</th>" +
            $"<th>{HtmlReport.Encode(Loc.S("Col.Process"))}</th>" +
            $"<th>{HtmlReport.Encode(Loc.S("Rep.Direction"))}</th>" +
            $"<th class=\"num\">{HtmlReport.Encode(Loc.S("Rep.DayTotal"))}</th>" +
            $"<th class=\"num\">{HtmlReport.Encode(Loc.S("Rep.CumulativeTotal"))}</th></tr></thead>");
        writer.WriteLine("<tbody>");
        foreach (var row in rows)
        {
            var direction = Loc.S(row.IsDownload ? "Rep.DirectionDownload" : "Rep.DirectionUpload");
            writer.WriteLine(
                $"<tr><td>{HtmlReport.Encode(HtmlReport.FormatDate(row.Date))}</td>" +
                $"<td>{HtmlReport.Encode(row.Process)}</td>" +
                $"<td>{HtmlReport.Encode(direction)}</td>" +
                $"<td class=\"num\">{HtmlReport.Encode(ByteSizeFormatter.FormatBytes(row.DayTotalBytes))}</td>" +
                $"<td class=\"num\">{HtmlReport.Encode(ByteSizeFormatter.FormatBytes(row.CumulativeBytes))}</td></tr>");
        }

        writer.WriteLine("</tbody>");
        writer.WriteLine("</table>");
        writer.WriteLine("</div>");

        HtmlReport.WriteDocumentEnd(writer);
    }

    private static List<(DateOnly Date, string Process, long Download, long Upload)> ReadDailyTotals(
        string dbPath,
        string adapter,
        string? processFilter)
    {
        var totals = new List<(DateOnly, string, long, long)>();

        try
        {
            using var connection = UsageDatabase.OpenReadOnlyConnection(dbPath);
            connection.Open();
            using var command = connection.CreateCommand();

            var sql = new StringBuilder(
                "SELECT d.Year, d.Month, d.Day, p.Name, SUM(pd.DataReceived), SUM(pd.DataSent) " +
                "FROM ProcessDate pd " +
                "JOIN Process p ON p.ID = pd.ProcessID " +
                "JOIN Adapter a ON a.ID = pd.AdapterID " +
                "JOIN Date d ON d.ID = pd.DateID " +
                "WHERE a.Name = @AdapterName");

            command.Parameters.AddWithValue("@AdapterName", adapter);

            if (!string.IsNullOrWhiteSpace(processFilter))
            {
                sql.Append(" AND p.Name LIKE @ProcessFilter ESCAPE '\\'");
                command.Parameters.AddWithValue("@ProcessFilter", $"%{EscapeLike(processFilter.Trim())}%");
            }

            sql.Append(" GROUP BY d.Year, d.Month, d.Day, p.Name");
            command.CommandText = sql.ToString();

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var date = new DateOnly(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
                var process = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                var download = reader.IsDBNull(4) ? 0 : reader.GetInt64(4);
                var upload = reader.IsDBNull(5) ? 0 : reader.GetInt64(5);
                totals.Add((date, process, download, upload));
            }
        }
        catch (Exception ex)
        {
            EventLogger.Error($"Failed to read detail usage rows for adapter '{adapter}'", ex);
        }

        return totals;
    }

    /// <summary>LIKE 通配符转义：进程名里可能出现 % 或 _（例如某些安装包生成的奇怪名字）。</summary>
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
