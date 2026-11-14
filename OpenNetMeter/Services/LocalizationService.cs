using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using OpenNetMeter.Localization;
using OpenNetMeter.Properties;
using OpenNetMeter.Utilities;

namespace OpenNetMeter.Services;

public sealed record LanguageOption(string Code, string DisplayName);

/// <summary>
/// 界面语言切换。切换时同时做三件事：
/// 1) 替换 <see cref="Loc"/> 的代码查表内容（视图模型、导出器立即取到新语言）；
/// 2) 替换 Application 资源字典里的文案字典（XAML 的 DynamicResource 自动刷新）；
/// 3) 切换线程区域设置（日期、数字、日历控件随之变化）。
/// </summary>
public static class LocalizationService
{
    /// <summary>语言切换完成后触发，视图模型用它刷新与语言相关的属性。</summary>
    public static event EventHandler? LanguageChanged;

    public static IReadOnlyList<LanguageOption> Options { get; } =
    [
        new LanguageOption("en-US", "English"),
        new LanguageOption("zh-CN", "简体中文"),
        new LanguageOption("zh-TW", "繁體中文")
    ];

    public static string CurrentCode { get; private set; } = "zh-CN";

    private static IResourceProvider? installedProvider;

    /// <summary>启动时调用一次。读取已保存的语言；未保存过则跟随系统 UI 语言。</summary>
    public static void Initialize(Application? application)
    {
        Apply(application, Normalize(SettingsManager.Current.Language), persist: false, raiseEvent: false);
    }

    /// <summary>用户在设置页切换语言。立即生效并写入设置。</summary>
    public static void SetLanguage(Application? application, string? code)
    {
        var normalized = Normalize(code);
        if (normalized == CurrentCode && installedProvider != null)
        {
            // 仍写回设置，保证“选择即保存”的语义
            SettingsManager.Current.Language = normalized;
            SettingsManager.Save();
            return;
        }

        Apply(application, normalized, persist: true, raiseEvent: true);
    }

    /// <summary>把任意语言码/系统区域规范化为受支持的三选一。</summary>
    public static string Normalize(string? code)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            var match = Options.FirstOrDefault(o => string.Equals(o.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match.Code;

            var lowered = code.Trim().ToLowerInvariant();
            if (lowered.StartsWith("zh", StringComparison.Ordinal))
            {
                return lowered.Contains("tw") || lowered.Contains("hk") || lowered.Contains("mo") || lowered.Contains("hant")
                    ? "zh-TW"
                    : "zh-CN";
            }

            if (lowered.StartsWith("en", StringComparison.Ordinal))
                return "en-US";
        }

        var ui = CultureInfo.CurrentUICulture;
        if (string.Equals(ui.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase))
        {
            return ui.Name.Contains("TW", StringComparison.OrdinalIgnoreCase)
                || ui.Name.Contains("HK", StringComparison.OrdinalIgnoreCase)
                || ui.Name.Contains("MO", StringComparison.OrdinalIgnoreCase)
                ? "zh-TW"
                : "zh-CN";
        }

        return "en-US";
    }

    private static void Apply(Application? application, string code, bool persist, bool raiseEvent)
    {
        var strings = code switch
        {
            "en-US" => StringsEnUs.Create(),
            "zh-TW" => StringsZhTw.Create(),
            _ => StringsZhCn.Create()
        };

        try
        {
            Loc.Use(strings);
            CurrentCode = code;

            var culture = CultureInfo.GetCultureInfo(code);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            if (application is not null)
            {
                var resources = application.Resources;
                if (resources is null)
                {
                    resources = new ResourceDictionary();
                    application.Resources = resources;
                }

                if (installedProvider is not null)
                    resources.MergedDictionaries.Remove(installedProvider);

                var dictionary = new ResourceDictionary();
                foreach (var pair in strings)
                    dictionary[pair.Key] = pair.Value;

                installedProvider = dictionary;
                resources.MergedDictionaries.Add(dictionary);
            }

            if (persist)
            {
                SettingsManager.Current.Language = code;
                SettingsManager.Save();
            }
        }
        catch (Exception ex)
        {
            EventLogger.Error($"Failed to apply UI language '{code}'", ex);
            return;
        }

        if (raiseEvent)
            LanguageChanged?.Invoke(null, EventArgs.Empty);
    }
}
