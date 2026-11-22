using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Shared.Models;
using KafkaLens.Formatting;

namespace KafkaLens.ViewModels;

public sealed partial class MessageViewModel : ViewModelBase
{
    public const int MAX_SUMMARY_LEN = 150;

    private readonly Message message;

    public Message Message => message;
    private static readonly JsonFormatter DecodedJsonFormatter = new();
    private string? schemaRegistryUrl;
    private CancellationTokenSource? valueDecode;
    private CancellationTokenSource? keyDecode;
    private byte[]? schemaJson;

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
            valueDecode?.Cancel();
            valueDecode = null;
            schemaJson = null;
            if (value == SchemaRegistryFormatter.Name)
            {
                StartRegistryDecode(isKey: false);
                return;
            }

            var formatter = FormatterFactory.Instance.GetFormatter(value);
            try
            {
                SetDecodedValue(formatter.Format(message.Value ?? [], false) ?? message.ValueText);
            }
            catch (Exception ex)
            {
                SetDecodedValue($"[Error: {ex.Message}]\n{message.ValueText}");
            }
        }
    }

    public string KeyFormatterName
    {
        get;
        set
        {
            if (value == field) return;

            SetProperty(ref field, value);
            keyDecode?.Cancel();
            keyDecode = null;
            if (value == SchemaRegistryFormatter.Name)
            {
                StartRegistryDecode(isKey: true);
                return;
            }

            var keyFormatter = FormatterFactory.Instance.GetFormatter(value);
            try
            {
                Key = keyFormatter.Format(message.Key ?? Array.Empty<byte>(), false) ?? message.KeyText;
            }
            catch (Exception ex)
            {
                Key = $"[Error: {ex.Message}] {message.KeyText}";
            }
        }
    }

    public MessageViewModel(Message message, string formatterName, string keyFormatterName,
        string? schemaRegistryUrl = null, string? topic = null)
    {
        this.message = message;
        this.schemaRegistryUrl = schemaRegistryUrl;
        Topic = topic!;
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

    internal void UpdateSchemaRegistryUrl(string? url)
    {
        if (schemaRegistryUrl == url) return;
        schemaRegistryUrl = url;
        if (FormatterName == SchemaRegistryFormatter.Name) StartRegistryDecode(isKey: false);
        if (KeyFormatterName == SchemaRegistryFormatter.Name) StartRegistryDecode(isKey: true);
    }

    private void StartRegistryDecode(bool isKey)
    {
        var operation = new CancellationTokenSource();
        if (isKey)
        {
            keyDecode?.Cancel();
            keyDecode = operation;
            Key = message.KeyText;
        }
        else
        {
            valueDecode?.Cancel();
            valueDecode = operation;
            schemaJson = null;
            SetDecodedValue(message.ValueText);
        }

        _ = DecodeFromRegistryAsync(isKey, operation);
    }

    private async Task DecodeFromRegistryAsync(bool isKey, CancellationTokenSource operation)
    {
        var rawText = isKey ? message.KeyText : message.ValueText;
        string? decoded;
        try
        {
            var url = schemaRegistryUrl;
            var data = isKey ? message.Key : message.Value;
            var topic = Topic ?? "";
            var token = operation.Token;
            decoded = await Task.Run(() => SchemaRegistryFormatter.DecodeAsync(url, data, topic, isKey, token), token);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            decoded = null;
        }
        catch (Exception ex)
        {
            rawText = $"[Schema Registry error: {ex.Message}]\n{rawText}";
            decoded = null;
        }

        var cancelled = operation.IsCancellationRequested;
        if (isKey && ReferenceEquals(keyDecode, operation)) keyDecode = null;
        if (!isKey && ReferenceEquals(valueDecode, operation)) valueDecode = null;
        operation.Dispose();
        if (cancelled) return;

        if (isKey)
        {
            Key = decoded ?? rawText;
        }
        else
        {
            schemaJson = decoded == null ? null : Encoding.UTF8.GetBytes(decoded);
            SetDecodedValue(decoded ?? rawText);
        }
    }

    internal void CancelRegistryDecodes()
    {
        keyDecode?.Cancel();
        valueDecode?.Cancel();
    }

    private void SetDecodedValue(string decoded)
    {
        var limit = Math.Min(MAX_SUMMARY_LEN, decoded.Length);
        Summary = decoded[..limit].ReplaceLineEndings(" ") + (limit < decoded.Length ? "..." : "");
        DecodedMessage = decoded;
        UpdateText();
    }

    private void UpdateText()
    {
        try
        {
            if (FormatterName == SchemaRegistryFormatter.Name)
            {
                DisplayText = schemaJson == null ? DecodedMessage
                    : DecodedJsonFormatter.Format(schemaJson, filterText, useObjectFilter) ?? DecodedMessage;
                return;
            }

            var formatter = FormatterFactory.Instance.GetFormatter(FormatterName);
            DisplayText = formatter.Format(message.Value ?? [], filterText, useObjectFilter) ?? DecodedMessage;
        }
        catch (Exception ex)
        {
            DisplayText = $"[Error formatting message with {FormatterName}: {ex.Message}]\n\nRaw Message:\n{message.ValueText}";
        }
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