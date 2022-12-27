
namespace KafkaLens.Views;

public sealed partial class MainPage : Page
{
    private MainViewModel dataContext => (MainViewModel)DataContext;

    public MainPage()
	{
		this.InitializeComponent();
	}
}
