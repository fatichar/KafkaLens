using KafkaLens.Client.DataAccess;
using KafkaLens.Client.ViewModels;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KafkaLens.Client.Pages;

public partial class Cluster : ComponentBase
{
    [Inject]
    private KafkaContext KafkaContext { get; set; }

    [Inject]
    ILogger<Cluster> Logger { get; set; }

    [Parameter]
    public string ClusterName { get; set; }

    [Parameter]
    public string TopicName { get; set; }

    [Parameter]
    public int? PartitionNo { get; set; }

    private KafkaCluster KafkaCluster { get; set; }
    private List<KafkaCluster> KafkaClusters => new() { KafkaCluster };

    public IList<INode> Topics => KafkaCluster?.Children;

    protected override async Task OnParametersSetAsync()
    {
        if (KafkaContext == null)
        {
            Logger.LogError("KafkaContext is not set");
            return;
        }
        KafkaCluster = await KafkaContext.GetByNameAsync(ClusterName);

        StateHasChanged();
    }
}