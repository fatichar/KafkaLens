using System;

namespace KafkaLens.ViewModels.Formatting
{
    public interface IMessageFormatter
    {
        public string Name { get; }

        public string? Format(byte[] data);
    }
}