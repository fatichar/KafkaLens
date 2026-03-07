using KafkaLens.Shared;
using KafkaLens.Shared.Models;

namespace KafkaLens.ViewModels;

public partial class OpenedClusterViewModel
{
    private OpenedTabState? pendingRestoreState;
    private bool suppressFetchOnSelectionChange;

    internal OpenedTabState CaptureOpenedTabState()
    {
        string? selectedNodeType = null;
        string? selectedTopicName = null;
        int? selectedPartitionId = null;

        switch (selectedNode)
        {
            case TopicViewModel topic:
                selectedNodeType = nameof(ITreeNode.NodeType.Topic);
                selectedTopicName = topic.Name;
                break;
            case PartitionViewModel partition:
                selectedNodeType = nameof(ITreeNode.NodeType.Partition);
                selectedTopicName = partition.TopicName;
                selectedPartitionId = partition.Id;
                break;
        }

        return new OpenedTabState
        {
            ClusterId = ClusterId,
            SavedMessagesPath = cluster.Client is ISavedMessagesClient ? cluster.Address : null,
            SelectedNodeType = selectedNodeType,
            SelectedTopicName = selectedTopicName,
            SelectedPartitionId = selectedPartitionId,
            FetchPosition = FetchPosition,
            FetchCount = FetchCount,
            FetchBackward = FetchBackward,
            StartOffset = StartOffset,
            StartDate = StartDate,
            StartTimeText = StartTimeText,
            MessagesSortColumn = MessagesSortColumn,
            MessagesSortAscending = MessagesSortAscending,
            PositiveFilter = CurrentMessages.PositiveFilter,
            NegativeFilter = CurrentMessages.NegativeFilter,
            LineFilter = CurrentMessages.LineFilter,
            UseObjectFilter = CurrentMessages.UseObjectFilter
        };
    }

    internal void ApplyOpenedTabState(OpenedTabState? state)
    {
        if (state == null) return;

        MessagesSortColumn = state.MessagesSortColumn;
        MessagesSortAscending = state.MessagesSortAscending;
        CurrentMessages.PositiveFilter = state.PositiveFilter ?? "";
        CurrentMessages.NegativeFilter = state.NegativeFilter ?? "";
        CurrentMessages.LineFilter = state.LineFilter ?? "";
        CurrentMessages.UseObjectFilter = state.UseObjectFilter;
        pendingRestoreState = state;
    }

    private void RestorePendingSessionState()
    {
        var state = pendingRestoreState;
        if (state == null) return;

        pendingRestoreState = null;

        ITreeNode? targetNode = null;
        if (!string.IsNullOrWhiteSpace(state.SelectedTopicName))
        {
            var topic = Topics.FirstOrDefault(t =>
                string.Equals(t.Name, state.SelectedTopicName, StringComparison.Ordinal));

            if (topic != null)
            {
                if (string.Equals(state.SelectedNodeType, nameof(ITreeNode.NodeType.Partition), StringComparison.Ordinal)
                    && state.SelectedPartitionId.HasValue)
                {
                    var partition = topic.Partitions.FirstOrDefault(p => p.Id == state.SelectedPartitionId.Value);
                    targetNode = partition ?? (ITreeNode)topic;
                    topic.IsExpanded = true;
                }
                else
                {
                    targetNode = topic;
                }
            }
        }

        if (targetNode != null)
        {
            suppressFetchOnSelectionChange = true;
            try
            {
                SelectedNode = targetNode;
                targetNode.IsSelected = true;
            }
            finally
            {
                suppressFetchOnSelectionChange = false;
            }
        }

        if (state.FetchCount > 0) FetchCount = state.FetchCount;
        StartOffset = state.StartOffset;
        if (state.StartDate.HasValue) StartDate = state.StartDate.Value;
        if (!string.IsNullOrWhiteSpace(state.StartTimeText)) StartTimeText = state.StartTimeText!;

        if (!string.IsNullOrWhiteSpace(state.FetchPosition) && FetchPositions.Contains(state.FetchPosition))
            FetchPosition = state.FetchPosition;

        if (IsFetchBackwardEnabled) FetchBackward = state.FetchBackward;

        if (IsCurrent && targetNode is { Type: ITreeNode.NodeType.Topic or ITreeNode.NodeType.Partition })
            FetchMessages();
    }
}
