using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using KafkaLens.ViewModels;

namespace AvaloniaApp.Views;

public partial class PluginManagerWindow : DialogBase
{
    public PluginManagerWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnNewRepoUrlBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is PluginManagerViewModel vm)
        {
            vm.AddRepositoryCommand.Execute(null);
        }
    }
}
