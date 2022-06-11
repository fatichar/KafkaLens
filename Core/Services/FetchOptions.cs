﻿namespace KafkaLens.Core.Services
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
        public FetchPosition From { get; set; } = FetchPosition.END;
        public FetchPosition To { get; set; }
        public int Limit { get; set; }
        public int Offset { get; set; }
    }
}