// 简体中文汉化修改：本文件的用户界面文案已译为简体中文，其余逻辑与官方版本一致。
using OpenNetMeter.Models;

namespace OpenNetMeter.Tests;

public class PinnedAdapterStatusTests
{
    [Fact]
    public void PinnedAndDisconnected_WarnsWithAdapterName()
    {
        var text = PinnedAdapterStatus.GetWarningText("{A}", "WireGuard", isConnected: false);

        Assert.Equal("已固定的适配器“WireGuard”未连接 — 当前不计入任何流量", text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void PinnedAndDisconnected_WithoutName_UsesFallbackName(string? name)
    {
        var text = PinnedAdapterStatus.GetWarningText("{A}", name, isConnected: false);

        Assert.Equal("已固定的适配器“所选适配器”未连接 — 当前不计入任何流量", text);
    }

    [Fact]
    public void PinnedAndConnected_HasNoWarning()
    {
        Assert.Equal(string.Empty, PinnedAdapterStatus.GetWarningText("{A}", "WireGuard", isConnected: true));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData(null, false)]
    public void Automatic_NeverWarns(string? pinnedId, bool isConnected)
    {
        Assert.Equal(string.Empty, PinnedAdapterStatus.GetWarningText(pinnedId, "WireGuard", isConnected));
    }
}
