using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Shared.Models;
using KafkaLens.Formatting;

namespace KafkaLens.ViewModels;

public sealed partial class MessageViewModel : ViewModelBase
{
    const int MaxSummaryLen = 100;

    public readonly Message message;
    private IMessageFormatter formatter = null!;

    public int Partition => message.Partition;
    public long Offset => message.Offset;
    public string? Key => message.KeyText;
    public string Summary { get; set; } = null!;
    public string DecodedMessage { get; set; } = null!;
    public string FormattedMessage { get; set; } = null!;

    public string Timestamp
    {
        get
        {
            var localTime = DateTime.UnixEpoch.AddMilliseconds(message.EpochMillis)
                .ToLocalTime();
            return localTime.ToShortDateString() + " " + localTime.ToLongTimeString();
        }
    }

    private string formatterName = null!;

    public string FormatterName
    {
        get => formatterName;
        set
        {
            if (value == formatterName) return;

            SetProperty(ref formatterName, value);
            formatter = FormatterFactory.Instance.GetFormatter(value);
            DecodedMessage = formatter.Format(message.Value ?? Array.Empty<byte>(), false) ?? message.ValueText;
            var limit = Math.Min(MaxSummaryLen, DecodedMessage.Length);
            Summary = DecodedMessage[..limit].ReplaceLineEndings(" ")
                      + (limit < DecodedMessage.Length ? "..." : "");

            UpdateText();
        }
    }

    public MessageViewModel(Message message, string formatterName)
    {
        this.message = message;
        FormatterName = formatterName;

        IsActive = true;
    }

    [ObservableProperty] private string displayText = null!;

    private string filterText = "";
    private bool useObjectFilter = true;

    public bool UseObjectFilter
    {
        get => useObjectFilter;
        set
        {
            useObjectFilter = value;
            UpdateText();
        }
    }

    public string LineFilter
    {
        get => filterText;
        set
        {
            filterText = value;
            UpdateText();
        }
    }

    public string Topic { get; set; } = null!;

    private void UpdateText()
    {
        DisplayText = formatter.Format(message.Value ?? [], filterText, useObjectFilter) ?? DecodedMessage;
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