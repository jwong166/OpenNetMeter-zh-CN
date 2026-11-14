using System.Collections.Generic;
using System.Globalization;

namespace OpenNetMeter.Localization;

/// <summary>
/// 文案查表的唯一入口。界面（XAML）通过 DynamicResource 读取同一份资源字典；
/// 代码（视图模型、导出器）通过这里读取，保证两处永远一致。
/// 未初始化时使用简体中文，测试宿主也走这条默认路径。
/// </summary>
internal static class Loc
{
    private static IReadOnlyDictionary<string, string> current = StringsZhCn.Create();

    public static IReadOnlyDictionary<string, string> Current => current;

    public static void Use(IReadOnlyDictionary<string, string> strings)
    {
        if (strings is { Count: > 0 })
            current = strings;
    }

    /// <summary>取文案；键不存在时返回键名本身，便于在界面上直接发现漏配。</summary>
    public static string T(string key) =>
        current.TryGetValue(key, out var value) ? value : key;

    /// <summary>取带占位符的文案并格式化，例如 “已导出 {0} 行至”。</summary>
    public static string F(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);

    // 简写别名，调用处更短：Loc.S("Tab.Summary")
    public static string S(string key) => T(key);
}
