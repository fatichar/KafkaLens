using Blazored.LocalStorage;
using KafkaLens.Client.DataAccess;
using KafkaLens.Shared.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace KafkaLens.Client.Pages
{
    public partial class Cluster : ComponentBase
    {
        [Inject]
        private KafkaContext KafkaContext { get; set; }

        [Inject]
        ILogger<Cluster> Logger { get; set; }

        [Inject]
        private ILocalStorageService LocalStorage { get; set; }

        [Parameter]
        public string clusterId { get; set; }

        public KafkaCluster KafkaCluster { get; set; }

        protected override void OnParametersSet()
        {
            if (KafkaContext != null)
                KafkaCluster = KafkaContext.GetById(clusterId);
        }
    }
}
