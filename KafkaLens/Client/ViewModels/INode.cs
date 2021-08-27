using System.Collections.Generic;

namespace KafkaLens.Client.ViewModels
{
    public interface INode
    {
        public string Name { get; }
        IList<INode> Children { get; }
        bool HasChildren => (Children?.Count ?? 0) > 0;
        bool Expanded { get; set; }
    }
}