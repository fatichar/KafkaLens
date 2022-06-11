namespace KafkaLens.Core.Services
{
    public class ConsumerFactory
    {
        public IKafkaConsumer CreateNew(string url)
        {
            return new ConfluentConsumer(url);
        }
    }
}
