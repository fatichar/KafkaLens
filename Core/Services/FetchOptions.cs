namespace KafkaLens.Core.Services
{
    public class FetchOptions
    {
        public FetchOptions(FetchPosition from = FetchPosition.END, int limit = 10)
        {
            From = from;
            Limit = limit;
        }

        public enum FetchPosition
        {
            START,
            END,
            TIMESTAMP,
            OFFSET
        }
        public FetchPosition From { get; set; } = FetchPosition.END;
        public FetchPosition To { get; set; }
        public int Limit { get; set; }
        public int Offset { get; set; }
    }
}
