using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Blazored.LocalStorage;
using KafkaLens.Client.DataAccess;
using KafkaLens.Shared.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace KafkaLens.Client.Pages
{
    public partial class Clusters : ComponentBase
    {
        [Inject]
        private KafkaContext KafkaContext { get; set; }

        [Inject]
        ILogger<Clusters> Logger { get; set; }

        [Inject]
        private ILocalStorageService LocalStorage { get; set; }

        private IList<KafkaCluster> clusters = new List<KafkaCluster>();
        private string clusterName;
        private string kafkaUrl;

        protected override async Task OnInitializedAsync()
        {
        }

        protected override async Task OnParametersSetAsync()
        {
            if (KafkaContext != null)
            {
                clusters = await KafkaContext.GetAllClustersAsync();
            }
        }

        private async void AddClusterAsync()
        {
            //var state = await LocalStorage.GetItemAsync<string>("state");
            //await LocalStorage.SetItemAsync("state", "add cluster");
            Logger.LogDebug("Adding a cluster");
            try
            {
                var cluster = 
                    await KafkaContext.AddAsync(new NewKafkaCluster(clusterName, kafkaUrl));

                StateHasChanged();
            }
            catch (Exception e)
            {
            }
        }

        private async Task RemoveClusterAsync(String clusterId)
        {
            var removed = await KafkaContext.RemoveAsync(clusterId);
            if (removed)
            {
                clusters = await KafkaContext.GetAllClustersAsync();
                StateHasChanged();
            }
        }
    }
}
