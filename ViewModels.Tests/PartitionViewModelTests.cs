namespace KafkaLens.ViewModels.Tests;

public class PartitionViewModelTests
{
    private readonly IFixture _fixture;

    public PartitionViewModelTests()
    {
        _fixture = new Fixture();
    }

    [Fact]
    public void Constructor_ShouldInitializeProperties()
    {
        // Arrange
        var topic = new Topic("test-topic", new List<Partition>());
        var topicViewModel = new TopicViewModel(topic, "JSON", "Text");
        var partition = new Partition(0);
        
        // Act
        var viewModel = new PartitionViewModel(topicViewModel, partition);
        
        // Assert
        Assert.Equal(0, viewModel.Id);
        Assert.Equal("Partition 0", viewModel.Name);
        Assert.Equal("test-topic", viewModel.TopicName);
        Assert.False(viewModel.IsExpandable);
        Assert.Equal(ITreeNode.NodeType.PARTITION, viewModel.Type);
        Assert.NotNull(viewModel.Children);
        Assert.NotNull(viewModel.Messages);
        Assert.NotNull(viewModel.LoadMessagesCommand);
    }

    [Fact]
    public void FormatterName_ShouldDelegateToTopic()
    {
        // Arrange
        var topic = new Topic("test-topic", new List<Partition>());
        var topicViewModel = new TopicViewModel(topic, "JSON", "Text");
        var partition = new Partition(0);
        var viewModel = new PartitionViewModel(topicViewModel, partition);
        
        // Act & Assert
        Assert.Equal("JSON", viewModel.FormatterName);
        
        // Act
        viewModel.FormatterName = "XML";
        
        // Assert
        Assert.Equal("XML", topicViewModel.FormatterName);
        Assert.Equal("XML", viewModel.FormatterName);
    }

    [Fact]
    public void KeyFormatterName_ShouldDelegateToTopic()
    {
        // Arrange
        var topic = new Topic("test-topic", new List<Partition>());
        var topicViewModel = new TopicViewModel(topic, "JSON", "Text");
        var partition = new Partition(0);
        var viewModel = new PartitionViewModel(topicViewModel, partition);
        
        // Act & Assert
        Assert.Equal("Text", viewModel.KeyFormatterName);
        
        // Act
        viewModel.KeyFormatterName = "Avro";
        
        // Assert
        Assert.Equal("Avro", topicViewModel.KeyFormatterName);
        Assert.Equal("Avro", viewModel.KeyFormatterName);
    }

    [Fact]
    public void TopicFormatterNameChange_ShouldUpdatePartition()
    {
        // Arrange
        var topic = new Topic("test-topic", new List<Partition>());
        var topicViewModel = new TopicViewModel(topic, "JSON", "Text");
        var partition = new Partition(0);
        var viewModel = new PartitionViewModel(topicViewModel, partition);
        
        // Act
        topicViewModel.FormatterName = "XML";
        
        // Assert
        Assert.Equal("XML", viewModel.FormatterName);
    }

    [Fact]
    public void TopicKeyFormatterNameChange_ShouldUpdatePartition()
    {
        // Arrange
        var topic = new Topic("test-topic", new List<Partition>());
        var topicViewModel = new TopicViewModel(topic, "JSON", "Text");
        var partition = new Partition(0);
        var viewModel = new PartitionViewModel(topicViewModel, partition);
        
        // Act
        topicViewModel.KeyFormatterName = "Avro";
        
        // Assert
        Assert.Equal("Avro", viewModel.KeyFormatterName);
    }

    [Fact]
    public async Task LoadMessagesCommand_ShouldThrowNotImplementedException()
    {
        // Arrange
        var topic = new Topic("test-topic", new List<Partition>());
        var topicViewModel = new TopicViewModel(topic, "JSON", "Text");
        var partition = new Partition(0);
        var viewModel = new PartitionViewModel(topicViewModel, partition);
        
        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(
            () => viewModel.LoadMessagesCommand.ExecuteAsync(null));
    }

    [Fact]
    public void Receive_ShouldNotThrow()
    {
        // Arrange
        var topic = new Topic("test-topic", new List<Partition>());
        var topicViewModel = new TopicViewModel(topic, "JSON", "Text");
        var partition = new Partition(0);
        var viewModel = new PartitionViewModel(topicViewModel, partition);
        var oldPartition = new TopicPartition("test-topic", 0);
        var newPartition = new TopicPartition("test-topic", 1);
        var message = new CommunityToolkit.Mvvm.Messaging.Messages.PropertyChangedMessage<TopicPartition>("", "Partition", oldPartition, newPartition);
        
        // Act & Assert
        viewModel.Receive(message);
    }
}
