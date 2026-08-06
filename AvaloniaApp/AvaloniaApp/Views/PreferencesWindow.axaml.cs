using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
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

    public async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select saved messages folder",
            AllowMultiple = false
        });

        return folders.FirstOrDefault()?.Path.LocalPath;
    }

    protected override void OnCancel()
    {
        if (DataContext is PreferencesViewModel vm)
            vm.CancelCommand.Execute(null);
        else
            base.OnCancel();
    }
}
