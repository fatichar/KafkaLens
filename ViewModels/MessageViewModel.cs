using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Formatting;
using KafkaLens.Shared.Models;

namespace KafkaLens.ViewModels
{
    public sealed class MessageViewModel : ObservableRecipient
    {
        private readonly Message message;
        private readonly IMessageFormatter formatter;

        public int Partition => message.Partition;
        public long Offset => message.Offset;
        public string? Key => message.KeyText;
        public string Summary { get; }
        public string FormattedMessage { get; }
        public string DisplayText
        {
            get => displayText;
            set
            {
                SetProperty(ref displayText, value);
            }
        }
        public DateTime Timestamp => DateTime.UnixEpoch.AddMilliseconds(message.EpochMillis).ToLocalTime();

        public string FormatterName => formatter.Name;

        public MessageViewModel(Message message, IMessageFormatter formatter)
        {
            this.message = message;
            this.formatter = formatter;
            FormattedMessage = formatter.Format(message.Value ?? Array.Empty<byte>()) ?? message.ValueText;
            Summary = message.ValueText[..100].ReplaceLineEndings(" ");

            DisplayText = FormattedMessage;

            IsActive = true;
        }

        private string lineFilter = "";
        private string displayText = "";

        public string LineFilter
        {
            get => lineFilter;
            set
            {
                lineFilter = value;
                if (lineFilter.Length > 0)
                {
                    var lines = FormattedMessage.Split(Environment.NewLine);
                    var filteredLines = new List<string>();
                    foreach (var line in lines)
                    {
                        if (line.Contains(lineFilter, StringComparison.OrdinalIgnoreCase))
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
}
