using KafkaLens.Client.ViewModels;
using Microsoft.AspNetCore.Components;

namespace KafkaLens.Client.Components
{
    public partial class ClusterView : ComponentBase
    {
        [Parameter]
        public KafkaCluster Cluster { get; set; }
    }
}