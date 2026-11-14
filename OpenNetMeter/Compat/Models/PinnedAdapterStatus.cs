// 简体中文汉化修改：本文件的用户界面文案已译为简体中文，其余逻辑与官方版本一致。
using OpenNetMeter.Localization;

namespace OpenNetMeter.Models;

/// <summary>
/// Wording for the warning shown when a pinned adapter is selected but not connected.
/// Pure logic so it can be unit tested without the view models.
/// </summary>
internal static class PinnedAdapterStatus
{
    /// <summary>
    /// Returns the warning text, or an empty string when there is nothing to warn about:
    /// automatic selection (no pinned adapter) or the network is connected.
    /// </summary>
    public static string GetWarningText(string? pinnedAdapterId, string? pinnedAdapterName, bool isConnected)
    {
        if (string.IsNullOrEmpty(pinnedAdapterId) || isConnected)
            return string.Empty;

        var name = string.IsNullOrWhiteSpace(pinnedAdapterName) ? Loc.S("St.PinnedFallback") : pinnedAdapterName;
        return Loc.F("St.PinnedWarning", name);
    }
}
