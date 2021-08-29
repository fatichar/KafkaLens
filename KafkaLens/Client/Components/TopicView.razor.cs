using KafkaLens.Client.DataAccess;
using KafkaLens.Client.ViewModels;
using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Syncfusion.Blazor.Grids;

namespace KafkaLens.Client.Components
{
    public partial class TopicView : ComponentBase
    {
        #region Data
        [Inject]
        private KafkaContext KafkaContext { get; set; }

        [Parameter]
        public KafkaCluster Cluster { get; set; }

        [Parameter]
        public string TopicName { get; set; }

        private List<Message> _messages;

        private SfGrid<Message> dataGrid;
        #endregion Data

        protected override async Task OnParametersSetAsync()
        {
            await FetchMessagesAsync();
            StateHasChanged();
        }

        private async Task FetchMessagesAsync()
        {
            _messages = await KafkaContext.GetMessagesAsync(Cluster.Name, TopicName);
        }
    }
}