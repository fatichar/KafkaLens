using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using IntegrationTests.Fakes;
using KafkaLens.Shared.Models;
using KafkaLens.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntegrationTests;

public class ComprehensiveTests
{
    private TestApp App => (TestApp)AvaloniaApp.App.Current;
    private MainViewModel MainViewModel => App.Services.GetRequiredService<MainViewModel>();
    private FakeKafkaClient FakeKafkaClient => (FakeKafkaClient)App.Services.GetRequiredService<KafkaLens.Shared.IKafkaLensClient>();

    private async Task ResetStateAsync()
    {
        await MainViewModel.LoadClusters();
        MainViewModel.OpenedClusters.Clear();
        MainViewModel.Clusters.Clear();
        MainViewModel.ClusterInfoRepository.DeleteAll();
    }

    [AvaloniaFact]
    public async Task AddCluster_ShouldAppearInList()
    {
        // Arrange
        await ResetStateAsync();
        var editClustersVm = new EditClustersViewModel(
            MainViewModel.Clusters,
            MainViewModel.ClusterInfoRepository,
            MainViewModel.ClientInfoRepository,
            MainViewModel.ClientFactory);

        string clusterName = "New Test Cluster";
        string clusterAddress = "localhost:9092";

        // Act
        await editClustersVm.AddClusterAsync(clusterName, clusterAddress);

        // Assert
        MainViewModel.Clusters.Should().Contain(c => c.Name == clusterName && c.Address == clusterAddress);
    }

    [AvaloniaFact]
    public async Task OpenCluster_ShouldLoadTopics()
    {
        // Arrange
        await ResetStateAsync();
        var cluster = new KafkaCluster(Guid.NewGuid().ToString(), "Topic Test Cluster", "localhost:9092");
        await FakeKafkaClient.AddClusterAsync(cluster);
        await MainViewModel.LoadClusters();
        
        // Wait a bit for the UI thread to process the cluster addition
        await Task.Delay(100);
        
        var clusterVm = MainViewModel.Clusters.First(c => c.Id == cluster.Id);
        Serilog.Log.Information("Found cluster in MainViewModel.Clusters: {ClusterId}. Total clusters: {Count}", clusterVm.Id, MainViewModel.Clusters.Count);

        // Act
        MainViewModel.OpenClusterCommand.Execute(cluster.Id);
        
        // Wait for OpenedClusters to be updated
        await Task.Delay(100);
        var openedCluster = MainViewModel.OpenedClusters.Last();

        // Wait for topics to load
        await Task.Delay(500);

        // Assert
        openedCluster.Topics.Should().NotBeEmpty();
        openedCluster.Children.Should().HaveCount(openedCluster.Topics.Count);
    }

    [AvaloniaFact]
    public async Task FilterTopics_ShouldWork()
    {
        // Arrange
        await ResetStateAsync();
        var cluster = new KafkaCluster(Guid.NewGuid().ToString(), "Filter Test Cluster", "localhost:9092");
        await FakeKafkaClient.AddClusterAsync(cluster);
        await MainViewModel.LoadClusters();
        await Task.Delay(100);
        
        MainViewModel.OpenClusterCommand.Execute(cluster.Id);
        await Task.Delay(100);
        var openedCluster = MainViewModel.OpenedClusters.Last();
        await Task.Delay(500); // Wait for topics

        var firstTopicName = openedCluster.Topics.First().Name;

        // Act
        openedCluster.FilterText = firstTopicName;

        // Assert
        openedCluster.Children.Should().Contain(t => t.Name == firstTopicName);
        openedCluster.Children.Should().HaveCount(openedCluster.Topics.Count(t => t.Name.Contains(firstTopicName, StringComparison.OrdinalIgnoreCase)));
    }

    [AvaloniaFact]
    public async Task SelectTopic_ShouldLoadMessages()
    {
        // Arrange
        await ResetStateAsync();
        var cluster = new KafkaCluster(Guid.NewGuid().ToString(), "Message Test Cluster", "localhost:9092");
        await FakeKafkaClient.AddClusterAsync(cluster);
        await MainViewModel.LoadClusters();
        await Task.Delay(100);
        
        MainViewModel.OpenClusterCommand.Execute(cluster.Id);
        await Task.Delay(100);
        var openedCluster = MainViewModel.OpenedClusters.Last();
        openedCluster.IsCurrent = true; // Required for fetch on selection change
        await Task.Delay(500); // Wait for topics

        var topicVm = openedCluster.Topics.First();

        // Act
        openedCluster.SelectedNode = topicVm;
        
        // Give some time for messages to "stream" in from FakeKafkaClient
        await Task.Delay(1000);

        // Assert
        openedCluster.CurrentMessages.Messages.Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public async Task OpenMultipleTabs_ForSameCluster_ShouldHaveUniqueNames()
    {
        // Arrange
        await ResetStateAsync();
        var cluster = new KafkaCluster(Guid.NewGuid().ToString(), "Multi Tab Cluster", "localhost:9092");
        await FakeKafkaClient.AddClusterAsync(cluster);
        await MainViewModel.LoadClusters();
        await Task.Delay(100);

        // Act
        MainViewModel.OpenClusterCommand.Execute(cluster.Id);
        await Task.Delay(100);
        MainViewModel.OpenClusterCommand.Execute(cluster.Id);
        await Task.Delay(100);

        // Assert
        MainViewModel.OpenedClusters.Should().HaveCount(2);
        MainViewModel.OpenedClusters[0].Name.Should().Be("Multi Tab Cluster");
        MainViewModel.OpenedClusters[1].Name.Should().Be("Multi Tab Cluster (1)");
    }

    [AvaloniaFact]
    public async Task CloseTab_ShouldRemoveFromOpenedClusters()
    {
        // Arrange
        await ResetStateAsync();
        var cluster = new KafkaCluster(Guid.NewGuid().ToString(), "Close Tab Cluster", "localhost:9092");
        await FakeKafkaClient.AddClusterAsync(cluster);
        await MainViewModel.LoadClusters();
        await Task.Delay(100);
        
        MainViewModel.OpenClusterCommand.Execute(cluster.Id);
        await Task.Delay(100);
        var openedCluster = MainViewModel.OpenedClusters.Last();

        // Act
        MainViewModel.CloseCurrentTabCommand.Execute(null);

        // Assert
        MainViewModel.OpenedClusters.Should().NotContain(openedCluster);
    }
}
