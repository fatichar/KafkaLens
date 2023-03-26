using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Shared.Models;
using KafkaLens.Formatting;

namespace KafkaLens.ViewModels;

public sealed class MessageViewModel : ObservableRecipient
{
    private readonly Message message;
    private readonly IMessageFormatter formatter;

    public int Partition => message.Partition;
    public long Offset => message.Offset;
    public string? Key => message.KeyText;
    public string Summary { get; set; }
    public string FormattedMessage { get; set; }

    public string DisplayText
    {
        get => displayText;
        set
        {
            SetProperty(ref displayText, value);
        }
    }
    
    public string Timestamp
    {
        get
        {
            var localTime = DateTime.UnixEpoch.AddMilliseconds(message.EpochMillis)
                .ToLocalTime();
            return localTime.ToShortDateString() + " " + localTime.ToLongTimeString();
        }
    }

    public string formatterName;
    public string FormatterName
    {
        get => formatterName;
        set
        {
            if (value != formatterName)
            {
                SetProperty(ref formatterName, value);
                var formatter = FormatterFactory.Instance.GetFormatter(value);
                FormattedMessage = formatter.Format(message.Value ?? Array.Empty<byte>()) ?? message.ValueText;
                int limit = Math.Min(100, message.ValueText.Length);
                Summary = message.ValueText[..limit].ReplaceLineEndings(" ");

                UpdateText();
            }
        }
    }

    public MessageViewModel(Message message, string formatterName)
    {
        this.message = message;
        FormatterName = formatterName;

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
            UpdateText();
        }
    }

    private void UpdateText()
    {
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