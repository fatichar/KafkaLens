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
            CheckedTopicNames = Topics.Where(t => t.IsChecked).Select(t => t.Name).ToList(),
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

        // Restored before the selection so the SelectedNode setter sees multi-topic mode and
        // leaves both the fetch positions and the viewer alone.
        RestoreCheckedTopics(state.CheckedTopicNames);

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
        if (!string.IsNullOrWhiteSpace(state.StartTimeText)) 
        {
            StartTimeText = state.StartTimeText!;
            // Ensure proper formatting after setting programmatically
            if (IsStartTimeValid)
                UpdateStartTimeText();
        }

        if (HasCheckedTopics) UseTopicFetchPositions();

        if (!string.IsNullOrWhiteSpace(state.FetchPosition) && FetchPositions.Contains(state.FetchPosition))
            FetchPosition = state.FetchPosition;

        if (IsFetchBackwardEnabled) FetchBackward = state.FetchBackward;

        if (IsCurrent &&
            (HasCheckedTopics || targetNode is { Type: ITreeNode.NodeType.Topic or ITreeNode.NodeType.Partition }))
            FetchMessages();
    }

    private void RestoreCheckedTopics(List<string>? checkedTopicNames)
    {
        if (checkedTopicNames == null || checkedTopicNames.Count == 0) return;

        var names = checkedTopicNames.ToHashSet(StringComparer.Ordinal);

        // Suppressed so restoring N topics does not kick off N separate fetches; the single
        // FetchMessages() at the end of the restore covers them all.
        suppressCheckedTopicFetch = true;
        try
        {
            foreach (var topic in Topics.Where(t => names.Contains(t.Name)))
            {
                topic.IsChecked = true;
            }
        }
        finally
        {
            suppressCheckedTopicFetch = false;
        }
    }
}
