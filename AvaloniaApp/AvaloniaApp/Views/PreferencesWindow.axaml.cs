using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KafkaLens.ViewModels;

namespace AvaloniaApp.Views;

public partial class PreferencesWindow : DialogBase
{
    public PreferencesWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnCancel()
    {
        if (DataContext is PreferencesViewModel vm)
            vm.CancelCommand.Execute(null);
        else
            base.OnCancel();
    }
}
