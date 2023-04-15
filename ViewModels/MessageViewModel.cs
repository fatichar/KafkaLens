using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Shared.Models;
using KafkaLens.Formatting;

namespace KafkaLens.ViewModels;

public sealed partial class MessageViewModel: ViewModelBase
{
    const int MaxSummaryLen = 200;

    public readonly Message message;
    private IMessageFormatter formatter;

    public int Partition => message.Partition;
    public long Offset => message.Offset;
    public string? Key => message.KeyText;
    public string Summary { get; set; }
    public string DecodedMessage { get; set; }
    public string FormattedMessage { get; set; }

    public string Timestamp
    {
        get
        {
            var localTime = DateTime.UnixEpoch.AddMilliseconds(message.EpochMillis)
                .ToLocalTime();
            return localTime.ToShortDateString() + " " + localTime.ToLongTimeString();
        }
    }

    private string formatterName;
    public string FormatterName
    {
        get => formatterName;
        set
        {
            if (value != null && value != formatterName)
            {
                SetProperty(ref formatterName, value);
                formatter = FormatterFactory.Instance.GetFormatter(value);
                DecodedMessage = formatter.Format(message.Value ?? Array.Empty<byte>(), false) ?? message.ValueText;
                int limit = Math.Min(MaxSummaryLen, message.ValueText.Length);
                Summary = DecodedMessage[..limit].ReplaceLineEndings(" ");

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

    [ObservableProperty]
    private string displayText;

    private string lineFilter = "";
    public string LineFilter
    {
        get => lineFilter;
        set
        {
            lineFilter = value;
            UpdateText();
        }
    }

    public string Topic { get; set; }

    private void UpdateText()
    {
        DisplayText = formatter.Format(message.Value, lineFilter) ?? DecodedMessage;
    }

    public void PrettyFormat()
    {
        UpdateText();
    }

    public void Cleanup()
    {
        DisplayText = "";
    }
}