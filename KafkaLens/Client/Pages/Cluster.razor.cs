using KafkaLens.Client.DataAccess;
using KafkaLens.Shared.Models;
using Microsoft.AspNetCore.Components;
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
        KafkaContext KafkaContext { get; set; }

        [Parameter]
        public string clusterId { get; set; }

        public KafkaCluster cluster { get; set; }

        protected override void OnParametersSet()
        {
            if (KafkaContext != null)
                cluster = KafkaContext.GetById(clusterId);
        }
    }
}
