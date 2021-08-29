namespace KafkaLens.Shared.Models
{
    public class FetchOptions
    {
        public enum FetchPosition
        {
            START,
            END,
            TIMESTAMP,
            OFFSET
        }
        public FetchPosition From { get; set; }
        public FetchPosition To { get; set; }
        public int Limit { get; set; }
        public int Offset { get; set; }
    }
}
