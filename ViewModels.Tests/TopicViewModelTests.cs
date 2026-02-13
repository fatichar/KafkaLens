namespace KafkaLens.ViewModels.Tests;

public class TopicViewModelTests
{
    private readonly IFixture _fixture;

    public TopicViewModelTests()
    {
        _fixture = new Fixture();
    }

    [Fact]
    public void Constructor_ShouldInitializeProperties()
    {
        // Arrange
        var topic = new Topic("test-topic", new List<Partition>
        {
            new(0),
            new(1)
        });
        
        // Act
        var viewModel = new TopicViewModel(topic, "JSON", "Text");
        
        // Assert
        Assert.Equal("test-topic", viewModel.Name);
        Assert.True(viewModel.IsExpandable);
        Assert.Equal("JSON", viewModel.FormatterName);
        Assert.Equal("Text", viewModel.KeyFormatterName);
        Assert.Equal(2, viewModel.Partitions.Count);
        Assert.Equal(2, viewModel.Children.Count);
        Assert.Equal(ITreeNode.NodeType.TOPIC, viewModel.Type);
        Assert.NotNull(viewModel.Messages);
        Assert.NotNull(viewModel.Formatters);
    }

    [Fact]
    public void Constructor_ShouldCreatePartitionViewModels()
    {
        // Arrange
        var topic = new Topic("test-topic", new List<Partition>
        {
            new(0),
            new(1)
        });
        
        // Act
        var viewModel = new TopicViewModel(topic, "JSON", "Text");
        
        // Assert
        Assert.All(viewModel.Partitions, p => Assert.IsType<PartitionViewModel>(p));
        Assert.All(viewModel.Children, c => Assert.IsType<PartitionViewModel>(c));
        
        for (int i = 0; i < viewModel.Partitions.Count; i++)
        {
            var partition = viewModel.Partitions[i];
            Assert.Equal($"Partition {i}", partition.Name);
            Assert.Equal(i, partition.Id);
            Assert.Equal("test-topic", partition.TopicName);
        }
    }

    [Fact]
    public void FormatterName_ShouldSetAndGetCorrectly()
    {
        // Arrange
        var topic = new Topic("test-topic", new List<Partition>());
        var viewModel = new TopicViewModel(topic, "JSON", "Text");
        
        // Act
        viewModel.FormatterName = "XML";
        
        // Assert
        Assert.Equal("XML", viewModel.FormatterName);
    }

    [Fact]
    public void KeyFormatterName_ShouldSetAndGetCorrectly()
    {
        // Arrange
        var topic = new Topic("test-topic", new List<Partition>());
        var viewModel = new TopicViewModel(topic, "JSON", "Text");
        
        // Act
        viewModel.KeyFormatterName = "Avro";
        
        // Assert
        Assert.Equal("Avro", viewModel.KeyFormatterName);
    }

    [Fact]
    public void FormatterNameChange_ShouldNotifyPartitions()
    {
        // Arrange
        var topic = new Topic("test-topic", new List<Partition>
        {
            new(0)
        });
        var viewModel = new TopicViewModel(topic, "JSON", "Text");
        var partition = viewModel.Partitions.First();
        
        // Act
        viewModel.FormatterName = "XML";
        
        // Assert
        Assert.Equal("XML", partition.FormatterName);
    }

    [Fact]
    public void KeyFormatterNameChange_ShouldNotifyPartitions()
    {
        // Arrange
        var topic = new Topic("test-topic", new List<Partition>
        {
            new(0)
        });
        var viewModel = new TopicViewModel(topic, "JSON", "Text");
        var partition = viewModel.Partitions.First();
        
        // Act
        viewModel.KeyFormatterName = "Avro";
        
        // Assert
        Assert.Equal("Avro", partition.KeyFormatterName);
    }
}
