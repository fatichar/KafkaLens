using System.Text;

namespace KafkaLens.App.Formating
{
    public class TextFormatter : IMessageFormatter
    {
        public string? Format(byte[] data)
        {            
            return Encoding.UTF8.GetString(data);
        }

        public string Name => "Text";
    }
}
