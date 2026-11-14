using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OpenNetMeter.Views.MainWindowTabs;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        // NumericUpDown 内部是普通 TextBox，Minimum/Maximum 只约束提交后的值，管不住输入过程。
        // 用隧道阶段拦截：事件从根向源传递，能在内部 TextBox 插入字符之前把非数字拒掉。
        TopNBox.AddHandler(TextInputEvent, OnTopNTextInput, RoutingStrategies.Tunnel);
    }

    private static void OnTopNTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
            return;

        foreach (var ch in e.Text)
        {
            if (ch is < '0' or > '9')
            {
                e.Handled = true;
                return;
            }
        }
    }
}
