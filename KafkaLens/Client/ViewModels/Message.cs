namespace KafkaLens.Client.ViewModels
{
    public class Message
    {
        public Message(byte[] key, byte[] body)
        {
            Key = System.Text.Encoding.Default.GetString(key);
            Body = System.Text.Encoding.Default.GetString(body);
        }

        public string Key { get; }
        public string Body { get; }
    }
}
