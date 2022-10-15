using KafkaLens.Client.DataAccess;
using KafkaLens.Client.ViewModels;
using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KafkaLens.Client.Formatters;

namespace KafkaLens.Client.Components;

public partial class TopicView : ComponentBase
{
    #region Data
    [Inject]
    private KafkaContext KafkaContext { get; set; }

    [Parameter]
    public KafkaCluster Cluster { get; set; }

    [Parameter]
    public string TopicName { get; set; }

    [Parameter]
    public int? PartitionNumber { get; set; }

    private List<Message> _messages;
    private List<IMessageFormatter> _formatters;

    public bool HidePartitionColumn = false;
    private Message selectedMessage;

    public Message SelectedMessage
    {
        get => selectedMessage;
        set
        {
            selectedMessage = value;
            StateHasChanged();
        }
    }
    #endregion Data

    protected override async Task OnParametersSetAsync()
    {
        HidePartitionColumn = PartitionNumber != null;
        _formatters = new List<IMessageFormatter> { new JsonFormatter() };

        await FetchMessagesAsync();
    }

    private async Task FetchMessagesAsync()
    {
        Clear();

        _messages = PartitionNumber == null ?
            await KafkaContext.GetMessagesAsync(Cluster.Name, TopicName) :
            await KafkaContext.GetMessagesAsync(Cluster.Name, TopicName, PartitionNumber.Value);

        Format(_messages);

        StateHasChanged();
    }

    private void Format(List<Message> messages)
    {
        var formatter = _formatters[0];
        messages.ForEach(msg => msg.FormattedBody = formatter.Format(msg.Body));
    }

    private void Clear()
    {
        _messages?.Clear();
        StateHasChanged();
    }
}