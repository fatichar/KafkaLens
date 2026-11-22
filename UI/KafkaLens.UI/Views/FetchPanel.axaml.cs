using Avalonia.Controls;
using KafkaLens.ViewModels;

namespace KafkaLens.UI.Views;

public partial class FetchPanel : UserControl
{
    public FetchPanel()
    {
        InitializeComponent();
    }

    private void OnTimeTextBoxLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OpenedClusterViewModel viewModel)
        {
            viewModel.OnStartTimeTextLostFocus();
        }
    }
}