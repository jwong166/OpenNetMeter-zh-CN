using Avalonia.Controls;

namespace OpenNetMeter.Services;

public interface IMiniWidgetService : System.IDisposable
{
    event System.Action<bool>? VisibilityChanged;

    void Show();
    void Hide();
    void RefreshAppearance(bool darkMode, int transparency);

    /// <summary>重新应用“全屏自动隐藏 / 贴靠任务栏”等行为设置。</summary>
    void RefreshBehavior();

    /// <summary>把挂件移回屏幕中央并短暂闪烁，供“找不到挂件”时定位。</summary>
    void RevealCentered();
    void ResetPosition(Window mainWindow);
    void EnsurePositionOnScreen(Window mainWindow);
}

