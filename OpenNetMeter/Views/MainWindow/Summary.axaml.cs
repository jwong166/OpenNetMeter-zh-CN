using Avalonia.Controls;
using Avalonia.Input;
using OpenNetMeter.ViewModels;

namespace OpenNetMeter.Views.MainWindowTabs;

public partial class SummaryView : UserControl
{
    public SummaryView()
    {
        InitializeComponent();
    }

    /// <summary>点击（含右键）进程行时把它设为选中行，选中态直接驱动行的强调样式。</summary>
    private void ProcessRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: SummaryProcessRowViewModel row } &&
            DataContext is SummaryViewModel viewModel)
        {
            viewModel.SelectProcess(row);
        }
    }
}
