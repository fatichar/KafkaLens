using CommunityToolkit.Mvvm.Messaging.Messages;

namespace KafkaLens.ViewModels.Messages;

public class ThemeChangedMessage : ValueChangedMessage<string>
{
    public ThemeChangedMessage(string theme) : base(theme)
    {
    }
}
