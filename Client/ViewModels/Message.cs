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
        public string Summary => Body?.Substring(0, 50);
        public int Partition { get; set; }
        public long Offset { get; set; }
        public System.DateTime TimeStamp { get; set; }
        public string FormattedBody { get; set; }
    }
}
