using System.Collections.ObjectModel;

namespace KafkaLens.Shared.Models
{
    public sealed class MessageStream
    {
        private bool hasMore = true;
        public ObservableCollection<Message> Messages { get; set; } = new ObservableCollection<Message>();

        public bool HasMore
        {
            get => hasMore;
            set
            {
                hasMore = value;
                if (!hasMore)
                {
                    Finished?.Invoke();
                }
            }
        }

        public delegate void FinishedEventHandler();

        public event FinishedEventHandler? Finished;
    }
}