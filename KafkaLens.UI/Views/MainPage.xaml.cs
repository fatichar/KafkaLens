
using Microsoft.Extensions.DependencyInjection;

namespace KafkaLens.Views;

public sealed partial class MainPage : Page
{
    private MainViewModel dataContext => (MainViewModel)DataContext;

    public MainPage()
	{
        this.InitializeComponent();

         DataContext = (Application.Current as App1).Host.Services.GetRequiredService<MainViewModel>();
    }
}
