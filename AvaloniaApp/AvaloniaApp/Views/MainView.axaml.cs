using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using KafkaLens.ViewModels;

namespace AvaloniaApp.Views
{
    public partial class MainView : UserControl
    {
        private Window? window;

        public MainView()
        {
            InitializeComponent();
            window = this.GetVisualRoot() as Window ??
                     this.FindAncestorOfType<Window>() ??
                    (Avalonia.Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null) ??
                    new Window();
            // {
            //     window = desktop.MainWindow ?? new Window();
            // }

            MainViewModel.ShowAboutDialog += () =>
            {
                var about = new About();
                about.ShowDialog(window);
            };

            MainViewModel.ShowFolderOpenDialog += OnShowFolderOpenDialog;
        }

        private async void OnShowFolderOpenDialog()
        {
            var dialog = new OpenFolderDialog();
            dialog.Title = "Select a folder";
            var result = dialog.ShowAsync(window);
            var path = await result;
            if (path == null)
            {
                return;
            }

            var dataContext = DataContext as MainViewModel;
            dataContext?.OpenSavedMessages(path);
        }
    }
}