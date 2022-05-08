using KafkaLens.Client.DataAccess;
using KafkaLens.Client.ViewModels;
using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Syncfusion.Blazor.Grids;

namespace KafkaLens.Client.Components
{
    public partial class MessageView : ComponentBase
    {
        #region Data
        [Parameter]
        public Message Message { get; set; }

        private string Text
        {
            get
            {
                if (Message == null)
                {
                    return "No message selected";
                }
                return Message.FormattedBody ?? Message.Body ?? "";
            }
        }
        #endregion Data
    }
}