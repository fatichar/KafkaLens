
using System.Collections.Specialized;
using Microsoft.Extensions.DependencyInjection;

namespace KafkaLens.Views;

public sealed partial class MainPage : Page
{
    private MainViewModel dataContext => (MainViewModel)DataContext;

    public MainPage()
	{
        this.InitializeComponent();

         DataContext = (Application.Current as App1).Host.Services.GetRequiredService<MainViewModel>();
         dataContext.Clusters.CollectionChanged += UpdateClusterNames;
    }

    private void UpdateClusterNames(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OpenMenu.Items.Clear();
        
        var clusters = dataContext.Clusters;
        foreach (var cluster in clusters)
        {
            var clusterItem = new MenuFlyoutItem
            {
                Text = cluster.Name,
                DataContext = cluster
            };
            clusterItem.Click += ClusterItemOnClick;
            OpenMenu.Items.Add(clusterItem);
        }
    }

    private void ClusterItemOnClick(object sender, RoutedEventArgs e)
    {
        var item = (MenuFlyoutItem)sender;
        var cluster = item.DataContext as ClusterViewModel;
        cluster?.OpenClusterCommand.Execute(null);
    }
}
