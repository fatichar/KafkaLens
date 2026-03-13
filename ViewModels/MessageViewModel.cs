using System.Collections.Generic;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Shared.Models;
using KafkaLens.Formatting;

namespace KafkaLens.ViewModels;

public sealed partial class MessageViewModel : ViewModelBase
{
    public const int MAX_SUMMARY_LEN = 150;

    private readonly Message message;

    public Message Message => message;
    private IMessageFormatter formatter = null!;
    private IMessageFormatter keyFormatter = null!;

    public int Partition => message.Partition;
    public long Offset => message.Offset;
    public IReadOnlyList<MessageHeaderViewModel> Headers { get; }
    [ObservableProperty] private string? key;
    [ObservableProperty] private string summary = null!;
    [ObservableProperty] private string decodedMessage = null!;
    [ObservableProperty] private string formattedMessage = null!;

    public string Timestamp
    {
        get
        {
            var localTime = DateTime.UnixEpoch.AddMilliseconds(message.EpochMillis)
                .ToLocalTime();
            return localTime.ToShortDateString() + " " + localTime.ToLongTimeString();
        }
    }

    public string FormatterName
    {
        get;
        set
        {
            if (value == field) return;

            SetProperty(ref field, value);
            formatter = FormatterFactory.Instance.GetFormatter(value);
            var decoded = formatter.Format(message.Value ?? Array.Empty<byte>(), false) ?? message.ValueText;
            var limit = Math.Min(MAX_SUMMARY_LEN, decoded.Length);
            Summary = decoded[..limit].ReplaceLineEndings(" ")
                      + (limit < decoded.Length ? "..." : "");
            DecodedMessage = decoded;

            UpdateText();
        }
    }

    public string KeyFormatterName
    {
        get;
        set
        {
            if (value == field) return;

            SetProperty(ref field, value);
            keyFormatter = FormatterFactory.Instance.GetFormatter(value);
            Key = keyFormatter.Format(message.Key ?? Array.Empty<byte>(), false) ?? message.KeyText;
        }
    }

    public MessageViewModel(Message message, string formatterName, string keyFormatterName)
    {
        this.message = message;
        Headers = message.Headers
            .Select(h => new MessageHeaderViewModel(h.Key, Encoding.UTF8.GetString(h.Value)))
            .ToList();
        FormatterName = formatterName;
        KeyFormatterName = keyFormatterName;

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