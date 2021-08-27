namespace KafkaLens.Client.ViewModels
{
    public class Message
    {
        public Message(object key, object body)
        {
            Key = key;
            Body = body;
        }

        public object Key { get; }
        public object Body { get; }
    }
}
