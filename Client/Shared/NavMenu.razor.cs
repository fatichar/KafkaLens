using KafkaLens.Client.AppConsatnts;
using KafkaLens.Client.DataAccess;
using KafkaLens.Client.ViewModels;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KafkaLens.Client.Shared
{
    public partial class NavMenu : ComponentBase
    {
        private INode selectedNode;

        [Inject]
        private KafkaContext KafkaContext { get; set; }

        [Inject]
        ILogger<NavMenu> Logger { get; set; }

        private IDictionary<string, KafkaCluster> Clusters { get; set; } = new Dictionary<string, KafkaCluster>();

        //private SfTreeView<INode> tree;
        [Inject]
        NavigationManager NavigationManager { get; set; }

        public IEnumerable<INode> GetChildren(INode node) => node?.Children;

        private INode SelectedNode
        {
            get => selectedNode; 
            set
            {
                selectedNode = value;
                NodeSelectionChanged();
            }
        }

        protected override async Task OnParametersSetAsync()
        {
            if (KafkaContext == null)
            {
                Logger.LogError("KafkaContext is not set");
                return;
            }
            Clusters = await KafkaContext.GetAllClustersAsync();

            foreach (var cluster in Clusters.Values)
            {
                cluster.Children = await KafkaContext.GetTopicsAsync(cluster.Id);
            }
            if (Clusters.Count > 0)
            {
                Clusters.Values.First().Expanded = true;
            }
            StateHasChanged();
        }

        private void NodeSelectionChanged()
        {
            if (selectedNode == null)
            {
                return;
            }
            var uri = "Cluster/" + selectedNode.Id;
            NavigationManager.NavigateTo(uri);
        }
    }
}
