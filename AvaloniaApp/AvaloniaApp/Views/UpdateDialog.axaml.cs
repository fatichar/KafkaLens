using System;
using Avalonia.Controls;
using KafkaLens.ViewModels;

namespace AvaloniaApp.Views;

public partial class UpdateDialog : Window
{
    public UpdateDialog()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is UpdateViewModel vm)
        {
            vm.OnUpdate += () => Close(true);
            vm.OnSkip += () => Close(false);
        }
    }
}
