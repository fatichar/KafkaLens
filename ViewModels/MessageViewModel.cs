using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Shared.Models;
using KafkaLens.ViewModels.Formatting;

namespace KafkaLens.ViewModels
{
    public sealed class MessageViewModel : ObservableRecipient
    {
        private readonly Message message;
        private readonly IMessageFormatter formatter;

        public int Partition => message.Partition;
        public long Offset => message.Offset;
        public string Key => message.KeyText;
        public string Summary { get; }
        public string FormattedMessage { get; }
        public string DisplayText { get; set; }
        public DateTime Timestamp => DateTime.UnixEpoch.AddMilliseconds(message.EpochMillis).ToLocalTime();

        public string FormatterName => formatter.Name;

        public MessageViewModel(Message message, IMessageFormatter formatter)
        {
            this.message = message;
            this.formatter = formatter;
            FormattedMessage = formatter.Format(message.Value) ?? message.ValueText;
            Summary = message.ValueText[..100].ReplaceLineEndings(" ");

            DisplayText = FormattedMessage;

            IsActive = true;
        }

        public void ApplyFilter(string filter)
        {
            if (filter.Length > 0)
            {
                var lines = FormattedMessage.Split(Environment.NewLine);
                var filteredLines = new List<string>();
                foreach (var line in lines)
                {
                    if (line.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    {
                        filteredLines.Add(line);
                    }
                }
                DisplayText = string.Join(Environment.NewLine, filteredLines);
            }
            else
            {
                DisplayText = FormattedMessage;
            }
        }
    }
}
