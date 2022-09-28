using KafkaLens.ViewModels;

namespace KafkaLens.Views
{
    public partial class Navigator : UserControl
    {
        public Navigator()
        {
            InitializeComponent();
        }

        private OpenedClusterViewModel dataContext => (OpenedClusterViewModel)DataContext; 
    }

    public class TopicTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? TopicTemplate { get; set; }
        public DataTemplate? PartitionTemplate { get; set; }

        protected override DataTemplate? SelectTemplateCore(object item)
        {
            var explorerItem = (ITreeNode)item;
            return explorerItem.Type == ITreeNode.NodeType.TOPIC ? TopicTemplate : PartitionTemplate;
        }
    }
}
