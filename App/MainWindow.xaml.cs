using KafkaLens.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace KafkaLens.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
            
        DataContext = App.Current.Services.GetService<MainViewModel>();
    }

    private MainViewModel Context => (MainViewModel) DataContext;
}