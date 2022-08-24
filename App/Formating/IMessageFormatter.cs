using System;

namespace KafkaLens.App.Formating
{
    public interface IMessageFormatter
    {
        public string Name { get; }

        public string? Format(byte[] data);
    }
}