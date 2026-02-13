using Avalonia;
using Avalonia.Markup.Xaml;

namespace KafkaLens.ViewModels.Tests;

public class TestApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
