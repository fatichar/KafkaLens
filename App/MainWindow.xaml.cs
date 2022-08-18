using CommunityToolkit.Mvvm.DependencyInjection;
using KafkaLens.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace KafkaLens.App
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            DataContext = App.Current.Services.GetService<ClustersViewModel>();
            
            Context.LoadClustersCommand.Execute(null);
        }

        private ClustersViewModel Context => (ClustersViewModel) DataContext;
    }
}
