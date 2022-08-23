using System;

namespace KafkaLens.App.Formating
{
    public interface IMessageFormatter
    {
        public string Name();

        public string? Format(byte[] data);
    }
}