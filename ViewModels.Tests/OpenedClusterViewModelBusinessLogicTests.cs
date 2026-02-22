using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using Avalonia.Headless.XUnit;
using KafkaLens.Formatting;
using KafkaLens.Shared;
using KafkaLens.Shared.Models;

namespace KafkaLens.ViewModels.Tests;

public class OpenedClusterViewModelBusinessLogicTests
{
    private readonly IKafkaLensClient mockClient;
    private readonly ISettingsService settingsService;
    private readonly ITopicSettingsService topicSettingsService;

    public OpenedClusterViewModelBusinessLogicTests()
    {
        mockClient = Substitute.For<IKafkaLensClient>();
        settingsService = Substitute.For<ISettingsService>();
        topicSettingsService = Substitute.For<ITopicSettingsService>();
        topicSettingsService.GetSettings(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new TopicSettings());
        OpenedClusterViewModel.FormatterFactory = FormatterFactory.Instance;
    }

    private OpenedClusterViewModel CreateViewModel(string clusterId = "c1", string clusterName = "TestCluster")
    {
        settingsService.GetBrowserConfig().Returns(new BrowserConfig());
        var cluster = new KafkaCluster(clusterId, clusterName, "localhost:9092");
        var clusterVm = new ClusterViewModel(cluster, mockClient);
        return new OpenedClusterViewModel(settingsService, topicSettingsService, clusterVm, clusterName);
    }

    [Fact]
    public void Constructor_WhenNoKeyFormatterSetting_ShouldUseDefaultBasicKeyFormatters()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        Assert.Equal("Unknown", vm.KeyFormatterNames[0]);
        Assert.Contains("Text", vm.KeyFormatterNames);
        Assert.Contains("Int32", vm.KeyFormatterNames);
        Assert.Contains("UInt64", vm.KeyFormatterNames);
        Assert.DoesNotContain("Json", vm.KeyFormatterNames);
    }

    [Fact]
    public void Constructor_WhenConfiguredKeyFormatterNames_ShouldFilterToSupportedBuiltIns()
    {
        // Arrange
        settingsService.GetValue("KeyFormatterNames")
            .Returns("[\"Int16\", \"Text\", \"CustomPlugin\"]");

        // Act
        var vm = CreateViewModel();

        // Assert
        Assert.Equal(new List<string> { "Unknown", "Int16", "Text" }, vm.KeyFormatterNames);
    }

    [Fact]
    public void Constructor_WhenNoValueFormatterSetting_ShouldUseAllAvailableValueFormatters()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        Assert.Equal("Unknown", vm.ValueFormatterNames[0]);
        Assert.Contains("Json", vm.ValueFormatterNames);
        Assert.Contains("Text", vm.ValueFormatterNames);
        Assert.Contains("Int32", vm.ValueFormatterNames);
    }

    [Fact]
    public void Constructor_WhenConfiguredValueFormatterNames_ShouldAllowConfiguredSubsetIncludingPlugins()
    {
        // Arrange
        settingsService.GetValue("ValueFormatterNames")
            .Returns("[\"Json\", \"Text\", \"PluginX\"]");

        // Act
        var vm = CreateViewModel();

        // Assert
        Assert.Equal(new List<string> { "Unknown", "Json", "Text" }, vm.ValueFormatterNames);
    }

    #region LoadTopicsAsync

    [AvaloniaFact]
    public async Task LoadTopicsAsync_ShouldPopulateTopicsFromCluster()
    {
        // Arrange
        var vm = CreateViewModel();
        var topics = new List<Topic>
        {
            new("topic-1", new List<Partition> { new(0) }),
            new("topic-2", new List<Partition> { new(0), new(1) })
        };
        mockClient.GetTopicsAsync("c1").Returns(Task.FromResult<IList<Topic>>(topics));

        // Act
        await vm.LoadTopicsAsync();

        // Assert
        Assert.Equal(2, vm.Topics.Count);
        Assert.Equal("topic-1", vm.Topics[0].Name);
        Assert.Equal("topic-2", vm.Topics[1].Name);
        Assert.Equal(2, vm.Children.Count);
    }

    [AvaloniaFact]
    public async Task LoadTopicsAsync_ShouldApplyTopicSettingsFromService()
    {
        // Arrange
        var vm = CreateViewModel();
        var topics = new List<Topic> { new("my-topic", new List<Partition>()) };
        mockClient.GetTopicsAsync("c1").Returns(Task.FromResult<IList<Topic>>(topics));
        topicSettingsService.GetSettings("c1", "my-topic")
            .Returns(new TopicSettings { KeyFormatter = "Text", ValueFormatter = "Json" });

        // Act
        await vm.LoadTopicsAsync();

        // Assert
        Assert.Single(vm.Topics);
        Assert.Equal("Json", vm.Topics[0].FormatterName);
        Assert.Equal("Text", vm.Topics[0].KeyFormatterName);
    }

    [AvaloniaFact]
    public async Task LoadTopicsAsync_WhenClientThrows_ShouldNotCrash()
    {
        // Arrange
        var vm = CreateViewModel();
        mockClient.GetTopicsAsync("c1")
            .Returns(Task.FromException<IList<Topic>>(new Exception("Connection failed")));

        // Act & Assert — should not throw
        await vm.LoadTopicsAsync();
        Assert.Empty(vm.Topics);
    }

    [AvaloniaFact]
    public async Task LoadTopicsAsync_CalledTwice_ShouldReplacePreviousTopics()
    {
        // Arrange
        var vm = CreateViewModel();
        var firstTopics = new List<Topic> { new("topic-1", new List<Partition>()) };
        mockClient.GetTopicsAsync("c1").Returns(Task.FromResult<IList<Topic>>(firstTopics));

        // Act — first load
        await vm.LoadTopicsAsync();
        Assert.Single(vm.Topics);
        Assert.Equal("topic-1", vm.Topics[0].Name);

        // Arrange — change mock return for second call
        var secondTopics = new List<Topic> { new("topic-a", new List<Partition>()), new("topic-b", new List<Partition>()) };
        mockClient.GetTopicsAsync("c1").Returns(Task.FromResult<IList<Topic>>(secondTopics));

        // Act — second load
        await vm.LoadTopicsAsync();

        // Assert
        Assert.Equal(2, vm.Topics.Count);
        Assert.Equal("topic-a", vm.Topics[0].Name);
        Assert.Equal("topic-b", vm.Topics[1].Name);
    }

    #endregion

    #region FilterTopics

    [AvaloniaFact]
    public async Task FilterTopics_WithEmptyFilter_ShouldShowAllTopics()
    {
        // Arrange
        var vm = CreateViewModel();
        var topics = new List<Topic>
        {
            new("orders", new List<Partition>()),
            new("users", new List<Partition>()),
            new("events", new List<Partition>())
        };
        mockClient.GetTopicsAsync("c1").Returns(Task.FromResult<IList<Topic>>(topics));
        await vm.LoadTopicsAsync();

        // Act
        vm.FilterText = "";
        vm.FilterTopics();

        // Assert
        Assert.Equal(3, vm.Children.Count);
    }

    [AvaloniaFact]
    public async Task FilterTopics_WithMatchingFilter_ShouldShowOnlyMatchingTopics()
    {
        // Arrange
        var vm = CreateViewModel();
        var topics = new List<Topic>
        {
            new("orders-created", new List<Partition>()),
            new("orders-updated", new List<Partition>()),
            new("users-created", new List<Partition>())
        };
        mockClient.GetTopicsAsync("c1").Returns(Task.FromResult<IList<Topic>>(topics));
        await vm.LoadTopicsAsync();

        // Act
        vm.FilterText = "orders";

        // Assert
        Assert.Equal(2, vm.Children.Count);
    }

    [AvaloniaFact]
    public async Task FilterTopics_ShouldBeCaseInsensitive()
    {
        // Arrange
        var vm = CreateViewModel();
        var topics = new List<Topic>
        {
            new("OrderEvents", new List<Partition>()),
            new("UserEvents", new List<Partition>())
        };
        mockClient.GetTopicsAsync("c1").Returns(Task.FromResult<IList<Topic>>(topics));
        await vm.LoadTopicsAsync();

        // Act
        vm.FilterText = "order";

        // Assert
        Assert.Single(vm.Children);
        Assert.Equal("OrderEvents", ((TopicViewModel)vm.Children[0]).Name);
    }

    #endregion

    #region SelectedNode and FetchMessages

    [AvaloniaFact]
    public async Task SelectedNode_WhenSetToTopic_ShouldTriggerFetchMessages()
    {
        // Arrange
        var vm = CreateViewModel();
        var topics = new List<Topic> { new("test-topic", new List<Partition> { new(0) }) };
        mockClient.GetTopicsAsync("c1").Returns(Task.FromResult<IList<Topic>>(topics));
        await vm.LoadTopicsAsync();
        vm.IsCurrent = true;

        var messageStream = new MessageStream();
        mockClient.GetMessageStream("c1", "test-topic", Arg.Any<FetchOptions>(), Arg.Any<CancellationToken>())
            .Returns(messageStream);

        // Act
        vm.SelectedNode = vm.Topics[0];

        // Assert
        Assert.Equal(ITreeNode.NodeType.Topic, vm.SelectedNodeType);
        Assert.True(vm.IsFetchOptionsEnabled);
        mockClient.Received(1).GetMessageStream("c1", "test-topic", Arg.Any<FetchOptions>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task SelectedNode_WhenSetToPartition_ShouldTriggerFetchMessagesForPartition()
    {
        // Arrange
        var vm = CreateViewModel();
        var topics = new List<Topic> { new("test-topic", new List<Partition> { new(0), new(1) }) };
        mockClient.GetTopicsAsync("c1").Returns(Task.FromResult<IList<Topic>>(topics));
        await vm.LoadTopicsAsync();
        vm.IsCurrent = true;

        var messageStream = new MessageStream();
        mockClient.GetMessageStream("c1", "test-topic", 0, Arg.Any<FetchOptions>(), Arg.Any<CancellationToken>())
            .Returns(messageStream);

        var partition = vm.Topics[0].Partitions[0];

        // Act
        vm.SelectedNode = partition;

        // Assert
        Assert.Equal(ITreeNode.NodeType.Partition, vm.SelectedNodeType);
        mockClient.Received(1).GetMessageStream("c1", "test-topic", 0, Arg.Any<FetchOptions>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task SelectedNode_WhenNotCurrent_ShouldNotFetchMessages()
    {
        // Arrange
        var vm = CreateViewModel();
        var topics = new List<Topic> { new("test-topic", new List<Partition> { new(0) }) };
        mockClient.GetTopicsAsync("c1").Returns(Task.FromResult<IList<Topic>>(topics));
        await vm.LoadTopicsAsync();
        vm.IsCurrent = false;

        // Act
        vm.SelectedNode = vm.Topics[0];

        // Assert — FetchMessages should not be called because IsCurrent is false
        mockClient.DidNotReceive().GetMessageStream(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<FetchOptions>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task SelectedNode_WhenSetToTopic_ShouldUseFetchPositionsForTopic()
    {
        // Arrange
        var vm = CreateViewModel();
        var topics = new List<Topic> { new("test-topic", new List<Partition> { new(0) }) };
        mockClient.GetTopicsAsync("c1").Returns(Task.FromResult<IList<Topic>>(topics));
        await vm.LoadTopicsAsync();
        vm.IsCurrent = true;
        mockClient.GetMessageStream("c1", "test-topic", Arg.Any<FetchOptions>(), Arg.Any<CancellationToken>())
            .Returns(new MessageStream());

        // Act
        vm.SelectedNode = vm.Topics[0];

        // Assert
        Assert.Contains("End", vm.FetchPositions);
        Assert.Contains("Timestamp", vm.FetchPositions);
        Assert.Contains("Start", vm.FetchPositions);
        Assert.DoesNotContain("Offset", vm.FetchPositions);
    }

    [AvaloniaFact]
    public async Task SelectedNode_WhenSetToPartition_ShouldUseFetchPositionsForPartition()
    {
        // Arrange
        var vm = CreateViewModel();
        var topics = new List<Topic> { new("test-topic", new List<Partition> { new(0) }) };
        mockClient.GetTopicsAsync("c1").Returns(Task.FromResult<IList<Topic>>(topics));
        await vm.LoadTopicsAsync();
        vm.IsCurrent = true;
        mockClient.GetMessageStream("c1", "test-topic", 0, Arg.Any<FetchOptions>(), Arg.Any<CancellationToken>())
            .Returns(new MessageStream());

        // Act
        vm.SelectedNode = vm.Topics[0].Partitions[0];

        // Assert
        Assert.Contains("End", vm.FetchPositions);
        Assert.Contains("Timestamp", vm.FetchPositions);
        Assert.Contains("Offset", vm.FetchPositions);
        Assert.Contains("Start", vm.FetchPositions);
    }

    [AvaloniaFact]
    public async Task FetchMessages_ShouldClearCurrentMessages()
    {
        // Arrange
        var vm = CreateViewModel();
        var topics = new List<Topic> { new("test-topic", new List<Partition> { new(0) }) };
        mockClient.GetTopicsAsync("c1").Returns(Task.FromResult<IList<Topic>>(topics));
        await vm.LoadTopicsAsync();
        vm.IsCurrent = true;

        // Add a message to CurrentMessages first
        var msg = new Message(1640995200000, new Dictionary<string, byte[]>(), null, Encoding.UTF8.GetBytes("old"));
        vm.CurrentMessages.Add(new MessageViewModel(msg, "Text", "Text"));
        Assert.Single(vm.CurrentMessages.Messages);

        mockClient.GetMessageStream("c1", "test-topic", Arg.Any<FetchOptions>(), Arg.Any<CancellationToken>())
            .Returns(new MessageStream());

        // Act
        vm.SelectedNode = vm.Topics[0];

        // Assert — CurrentMessages should be cleared when new fetch starts
        Assert.Empty(vm.CurrentMessages.Messages);
    }

    #endregion

    #region CreateFetchOptions

    [AvaloniaFact]
    public void CreateFetchOptions_WithEndPosition_ShouldCreateCorrectOptions()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.FetchPosition = "End";
        vm.FetchCount = 50;

        // Act
        var options = vm.CreateFetchOptions();

        // Assert
        Assert.Equal(50, options.Limit);
        Assert.NotNull(options.End);
    }

    [AvaloniaFact]
    public void CreateFetchOptions_WithStartPosition_ShouldCreateCorrectOptions()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.FetchPosition = "Start";
        vm.FetchCount = 25;

        // Act
        var options = vm.CreateFetchOptions();

        // Assert
        Assert.Equal(25, options.Limit);
        Assert.Equal(PositionType.OFFSET, options.Start.Type);
        Assert.Equal(0, options.Start.Offset);
    }

    [AvaloniaFact]
    public void CreateFetchOptions_WithTimestampPosition_ShouldCreateCorrectOptions()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.FetchPosition = "Timestamp";
        vm.FetchCount = 10;

        // Act
        var options = vm.CreateFetchOptions();

        // Assert
        Assert.Equal(10, options.Limit);
        Assert.Equal(PositionType.TIMESTAMP, options.Start.Type);
    }

    [AvaloniaFact]
    public void CreateFetchOptions_WithOffsetPosition_ShouldCreateCorrectOptions()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.FetchPosition = "Offset";
        vm.StartOffset = "42";
        vm.FetchCount = 10;

        // Act
        var options = vm.CreateFetchOptions();

        // Assert
        Assert.Equal(10, options.Limit);
        Assert.Equal(PositionType.OFFSET, options.Start.Type);
        Assert.Equal(42, options.Start.Offset);
    }

    [AvaloniaFact]
    public void CreateFetchOptions_WithInvalidOffset_ShouldUseNegativeOne()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.FetchPosition = "Offset";
        vm.StartOffset = "not-a-number";
        vm.FetchCount = 10;

        // Act
        var options = vm.CreateFetchOptions();

        // Assert
        Assert.Equal(-1, options.Start.Offset);
    }

    #endregion

    #region SaveTopicSettings

    [AvaloniaFact]
    public async Task SaveTopicSettings_WhenTopicSelected_ShouldPersistSettings()
    {
        // Arrange
        var vm = CreateViewModel();
        var topics = new List<Topic> { new("test-topic", new List<Partition> { new(0) }) };
        mockClient.GetTopicsAsync("c1").Returns(Task.FromResult<IList<Topic>>(topics));
        await vm.LoadTopicsAsync();
        vm.IsCurrent = true;
        mockClient.GetMessageStream("c1", "test-topic", Arg.Any<FetchOptions>(), Arg.Any<CancellationToken>())
            .Returns(new MessageStream());

        vm.SelectedNode = vm.Topics[0];
        vm.Topics[0].FormatterName = "Text";
        vm.Topics[0].KeyFormatterName = "Int32";

        // Act
        await vm.SaveTopicSettingsAsync();

        // Assert
        topicSettingsService.Received(1).SetSettings("c1", "test-topic",
            Arg.Is<TopicSettings>(s => s.ValueFormatter == "Text" && s.KeyFormatter == "Int32"),
            false);
    }

    [AvaloniaFact]
    public async Task SaveTopicSettings_WithApplyToAllClusters_ShouldPassFlag()
    {
        // Arrange
        var vm = CreateViewModel();
        var topics = new List<Topic> { new("test-topic", new List<Partition> { new(0) }) };
        mockClient.GetTopicsAsync("c1").Returns(Task.FromResult<IList<Topic>>(topics));
        await vm.LoadTopicsAsync();
        vm.IsCurrent = true;
        mockClient.GetMessageStream("c1", "test-topic", Arg.Any<FetchOptions>(), Arg.Any<CancellationToken>())
            .Returns(new MessageStream());

        vm.SelectedNode = vm.Topics[0];
        vm.Topics[0].FormatterName = "Text";
        vm.Topics[0].KeyFormatterName = "Text";
        vm.ApplyToAllClusters = true;

        // Act
        await vm.SaveTopicSettingsAsync();

        // Assert
        topicSettingsService.Received(1).SetSettings("c1", "test-topic",
            Arg.Any<TopicSettings>(), true);
    }

    [AvaloniaFact]
    public async Task SaveTopicSettings_ShouldReformatExistingMessages()
    {
        // Arrange
        var vm = CreateViewModel();
        var topics = new List<Topic> { new("test-topic", new List<Partition> { new(0) }) };
        mockClient.GetTopicsAsync("c1").Returns(Task.FromResult<IList<Topic>>(topics));
        await vm.LoadTopicsAsync();
        vm.IsCurrent = true;
        mockClient.GetMessageStream("c1", "test-topic", Arg.Any<FetchOptions>(), Arg.Any<CancellationToken>())
            .Returns(new MessageStream());

        vm.SelectedNode = vm.Topics[0];

        // Add a message to CurrentMessages
        var msg = new Message(1640995200000, new Dictionary<string, byte[]>(),
            Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("value"));
        var msgVm = new MessageViewModel(msg, "Text", "Text");
        vm.CurrentMessages.Add(msgVm);

        // Set formatter to a valid non-Auto name
        vm.Topics[0].FormatterName = "Text";
        vm.Topics[0].KeyFormatterName = "Text";

        // Act
        await vm.SaveTopicSettingsAsync();

        // Assert — message should have been reformatted
        Assert.Equal("Text", msgVm.FormatterName);
        Assert.Equal("Text", msgVm.KeyFormatterName);
    }

    [AvaloniaFact]
    public async Task SaveTopicSettings_WhenAutoSelected_ShouldGuessAndPersistFormatterNames()
    {
        // Arrange
        var vm = CreateViewModel();
        var topics = new List<Topic> { new("test-topic", new List<Partition> { new(0) }) };
        mockClient.GetTopicsAsync("c1").Returns(Task.FromResult<IList<Topic>>(topics));
        await vm.LoadTopicsAsync();
        vm.IsCurrent = true;
        mockClient.GetMessageStream("c1", "test-topic", Arg.Any<FetchOptions>(), Arg.Any<CancellationToken>())
            .Returns(new MessageStream());

        vm.SelectedNode = vm.Topics[0];
        var msg = new Message(1640995200000, new Dictionary<string, byte[]>(),
            new byte[] { 0, 0, 0, 1 }, Encoding.UTF8.GetBytes("{\"id\":1}"));
        vm.CurrentMessages.Add(new MessageViewModel(msg, "Text", "Text"));

        vm.Topics[0].FormatterName = "Unknown";
        vm.Topics[0].KeyFormatterName = "Unknown";

        // Act
        await vm.SaveTopicSettingsAsync();

        // Assert
        Assert.NotEqual("Unknown", vm.Topics[0].FormatterName);
        Assert.NotEqual("Unknown", vm.Topics[0].KeyFormatterName);
        topicSettingsService.Received(1).SetSettings("c1", "test-topic",
            Arg.Is<TopicSettings>(s => s.ValueFormatter != null && s.KeyFormatter != null),
            false);
    }

    [AvaloniaFact]
    public async Task SaveTopicSettings_WhenNoNodeSelected_ShouldDoNothing()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act & Assert — should not throw
        await vm.SaveTopicSettingsAsync();
        topicSettingsService.DidNotReceive().SetSettings(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TopicSettings>(), Arg.Any<bool>());
    }

    #endregion

    #region OnMessagesChanged (message arrival flow)

    [AvaloniaFact]
    public async Task OnMessagesChanged_ShouldAddMessagesToPendingAndUpdateMessages()
    {
        // Arrange
        var vm = CreateViewModel();
        var topics = new List<Topic> { new("test-topic", new List<Partition> { new(0) }) };
        mockClient.GetTopicsAsync("c1").Returns(Task.FromResult<IList<Topic>>(topics));
        await vm.LoadTopicsAsync();
        vm.IsCurrent = true;

        var messageStream = new MessageStream();
        mockClient.GetMessageStream("c1", "test-topic", Arg.Any<FetchOptions>(), Arg.Any<CancellationToken>())
            .Returns(messageStream);

        // Set a non-Auto formatter so OnMessagesChanged doesn't try to guess
        vm.Topics[0].FormatterName = "Text";
        vm.Topics[0].KeyFormatterName = "Text";

        // Act — select topic to trigger FetchMessages, then simulate message arrival
        vm.SelectedNode = vm.Topics[0];

        var msg = new Message(1640995200000, new Dictionary<string, byte[]>(),
            Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("hello world"));
        messageStream.Messages.Add(msg);

        // Manually flush pending messages (normally done via Dispatcher)
        vm.UpdateMessages();

        // Assert
        Assert.Single(vm.CurrentMessages.Messages);
        Assert.Equal("hello world", vm.CurrentMessages.Messages[0].DecodedMessage);
    }

    #endregion

    #region OnClusterPropertyChanged

    [AvaloniaFact]
    public async Task OnClusterPropertyChanged_WhenConnectedAndNoTopics_ShouldLoadTopics()
    {
        // Arrange
        settingsService.GetBrowserConfig().Returns(new BrowserConfig());
        var cluster = new KafkaCluster("c1", "TestCluster", "localhost:9092");
        var clusterVm = new ClusterViewModel(cluster, mockClient);
        var vm = new OpenedClusterViewModel(settingsService, topicSettingsService, clusterVm, "TestCluster");

        var topics = new List<Topic> { new("auto-loaded-topic", new List<Partition>()) };
        mockClient.GetTopicsAsync("c1").Returns(Task.FromResult<IList<Topic>>(topics));

        // Act — simulate cluster becoming connected
        mockClient.ValidateConnectionAsync("localhost:9092").Returns(Task.FromResult(true));
        await clusterVm.CheckConnectionAsync();

        // Allow async LoadTopicsAsync to complete
        await Task.Delay(100);

        // Assert
        Assert.Single(vm.Topics);
        Assert.Equal("auto-loaded-topic", vm.Topics[0].Name);
    }

    #endregion
}
