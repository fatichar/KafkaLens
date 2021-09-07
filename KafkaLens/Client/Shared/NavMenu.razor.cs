using KafkaLens.Client.AppConsatnts;
using KafkaLens.Client.DataAccess;
using KafkaLens.Client.ViewModels;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Syncfusion.Blazor.Navigations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KafkaLens.Client.Shared
{
    public partial class NavMenu : ComponentBase
    {
        [Inject]
        private KafkaContext KafkaContext { get; set; }

        [Inject]
        ILogger<NavMenu> Logger { get; set; }

        private IDictionary<string, KafkaCluster> Clusters { get; set; } = new Dictionary<string, KafkaCluster>();

        private SfTreeView<INode> tree;
        [Inject]
        NavigationManager NavigationManager { get; set; }

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

        private void NodeSelectionChanged(NodeSelectEventArgs args)
        {
            var nodeId = args.NodeData.Id;
            var selectedNode = tree.GetTreeData(nodeId).FirstOrDefault();
            if (selectedNode == null)
            {
                return;
            }
            var uri = "Cluster/" + selectedNode.Id;
            NavigationManager.NavigateTo(uri);
        }
    }
}
