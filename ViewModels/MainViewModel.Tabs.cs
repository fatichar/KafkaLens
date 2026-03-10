using System.IO;
using KafkaLens.Shared.Models;
using Serilog;

namespace KafkaLens.ViewModels;

public partial class MainViewModel
{
    private readonly IDictionary<string, List<OpenedClusterViewModel>> openedClustersMap
        = new Dictionary<string, List<OpenedClusterViewModel>>();

    internal void OpenCluster(ClusterViewModel clusterViewModel) => OpenCluster(clusterViewModel, null);

    private void OpenCluster(string? clusterId)
    {
        var cluster = Clusters.FirstOrDefault(c => c.Id == clusterId);
        if (cluster == null)
        {
            Log.Error("Failed to find cluster with id {ClusterId}", clusterId);
            return;
        }
        OpenCluster(cluster);
    }

    private void OpenCluster(ClusterViewModel clusterViewModel, OpenedTabState? tabState)
    {
        Log.Information("Opening cluster: {ClusterName}", clusterViewModel.Name);
        var newName = clusterViewModel.Name;

        openedClustersMap.TryGetValue(clusterViewModel.Id, out var alreadyOpened);
        if (alreadyOpened == null)
        {
            alreadyOpened = new List<OpenedClusterViewModel>();
            openedClustersMap.Add(clusterViewModel.Id, alreadyOpened);
        }
        else
        {
            newName = GenerateNewName(clusterViewModel.Name, alreadyOpened);
        }

        var openedCluster = new OpenedClusterViewModel(
            settingsService, topicSettingsService, messageSaver, formatterService, clusterViewModel, newName);
        openedCluster.ApplyOpenedTabState(tabState);
        alreadyOpened.Add(openedCluster);
        OpenedClusters.Add(openedCluster);
        _ = openedCluster.LoadTopicsAsync();
        SelectedIndex = OpenedClusters.Count - 1;
    }

    public async void OpenSavedMessages(string path) => await OpenSavedMessagesAsync(path, null);

    private async Task OpenSavedMessagesAsync(string path, OpenedTabState? tabState)
    {
        if (path.EndsWith('\\') || path.EndsWith('/'))
            path = path.Remove(path.Length - 1, 1);

        var clusterName = Path.GetFileName(path) + "(saved)";
        var clusterViewModel = await AddOrGetCluster(clusterName, path);
        OpenCluster(clusterViewModel, tabState);
    }

    private async Task<ClusterViewModel> AddOrGetCluster(string clusterName, string path)
    {
        var existing = Clusters.FirstOrDefault(c => c.Name == clusterName);
        if (existing != null) return existing;

        var newCluster = new NewKafkaCluster(clusterName, path);
        var cluster = await savedMessagesClient.AddAsync(newCluster);
        var clusterViewModel = new ClusterViewModel(cluster, savedMessagesClient);
        Clusters.Add(clusterViewModel);
        return clusterViewModel;
    }

    internal void CloseTab(OpenedClusterViewModel openedCluster)
    {
        Log.Information("Closing tab: {TabName}", openedCluster.Name);
        if (openedClustersMap.TryGetValue(openedCluster.ClusterId, out var openedList))
        {
            openedList.Remove(openedCluster);
            if (openedList.Count == 0)
                openedClustersMap.Remove(openedCluster.ClusterId);
        }
        OpenedClusters.Remove(openedCluster);
    }

    private void CloseCurrentTab()
    {
        if (SelectedIndex >= 0)
            CloseTab(OpenedClusters[SelectedIndex]);
    }

    private void NextTab()
    {
        if (OpenedClusters.Count > 1)
            SelectedIndex = (SelectedIndex + 1) % OpenedClusters.Count;
    }

    private void PreviousTab()
    {
        if (OpenedClusters.Count > 1)
            SelectedIndex = (SelectedIndex - 1 + OpenedClusters.Count) % OpenedClusters.Count;
    }

    private void SelectTab(int index)
    {
        if (index > 0 && index <= OpenedClusters.Count)
            SelectedIndex = index - 1;
    }

    internal static string GenerateNewName(string clusterName, List<OpenedClusterViewModel> alreadyOpened)
    {
        var numbers = new List<int>();
        int clusterNameLength = clusterName.Length;
        int expectedPrefixLength = clusterNameLength + 2; // "ClusterName ("

        foreach (var cluster in alreadyOpened)
        {
            var name = cluster.Name;

            // Expected format: "ClusterName (N)" or just "ClusterName"
            if (name.Length > expectedPrefixLength &&
                name.StartsWith(clusterName, StringComparison.Ordinal) &&
                name[clusterNameLength] == ' ' &&
                name[clusterNameLength + 1] == '(' &&
                name[name.Length - 1] == ')')
            {
                var numSpan = name.AsSpan(clusterNameLength + 2, name.Length - expectedPrefixLength - 1);
                if (int.TryParse(numSpan, out int number))
                {
                    numbers.Add(number);
                }
            }
        }

        numbers.Sort();

        var smallestAvailable = numbers.Count + 1;
        for (var i = 0; i < numbers.Count; i++)
        {
            if (numbers[i] > i + 1)
            {
                smallestAvailable = i + 1;
                break;
            }
        }

        return $"{clusterName} ({smallestAvailable})";
    }
}