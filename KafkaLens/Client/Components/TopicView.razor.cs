using KafkaLens.Client.ViewModels;
using Microsoft.AspNetCore.Components;

namespace KafkaLens.Client.Components
{
    public partial class TopicView : ComponentBase
    {
        [Parameter]
        public KafkaCluster Cluster { get; set; }
        [Parameter]
        public string TopicName { get; set; }
    }
}