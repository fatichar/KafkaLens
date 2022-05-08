using KafkaLens.Client.DataAccess;
using KafkaLens.Client.ViewModels;
using Microsoft.AspNetCore.Components;
using System.Threading.Tasks;

namespace KafkaLens.Client.Components
{
    public partial class ClusterView : ComponentBase
    {
        [Parameter]
        public KafkaCluster Cluster { get; set; }

        [Inject]
        private KafkaContext KafkaContext { get; set; }

    }
}