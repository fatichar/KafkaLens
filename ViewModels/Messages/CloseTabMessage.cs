namespace KafkaLens.ViewModels.Messages
{
    public class CloseTabMessage
    {
        public OpenedClusterViewModel OpenedClusterViewModel { get; }
        public CloseTabMessage(OpenedClusterViewModel openedClusterViewModel)
        {
            OpenedClusterViewModel = openedClusterViewModel;
        }
    }
}
