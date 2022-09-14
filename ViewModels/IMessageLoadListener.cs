namespace KafkaLens.ViewModels;

public interface IMessageLoadListener
{
    void MessageLoadingStarted();
    void MessageLoadingFinished();
}