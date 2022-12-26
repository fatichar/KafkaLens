namespace KafkaLens.Wasm;

public class Program
{
	private static App1? _app;

	static int Main(string[] args)
	{
		Microsoft.UI.Xaml.Application.Start(_ => _app = new App1());

		return 0;
	}
}
