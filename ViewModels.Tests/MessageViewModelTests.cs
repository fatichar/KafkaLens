namespace KafkaLens.ViewModels.Tests;

using System.Text;

public class MessageViewModelTests
{
    private readonly IFixture _fixture;

    public MessageViewModelTests()
    {
        _fixture = new Fixture();
    }

    [Fact]
    public void Constructor_ShouldInitializeProperties()
    {
        // Arrange
        var message = CreateTestMessage();
        
        // Act
        var viewModel = new MessageViewModel(message, "Text", "Text");
        
        // Assert
        Assert.Equal(message, viewModel.Message);
        Assert.Equal(message.Partition, viewModel.Partition);
        Assert.Equal(message.Offset, viewModel.Offset);
        Assert.Equal("Text", viewModel.FormatterName);
        Assert.Equal("Text", viewModel.KeyFormatterName);
    }

    [Fact]
    public void Timestamp_ShouldFormatCorrectly()
    {
        // Arrange
        var message = CreateTestMessage(epochMillis: 1640995200000); // 2022-01-01 00:00:00 UTC
        var viewModel = new MessageViewModel(message, "Text", "Text");
        
        // Act
        var timestamp = viewModel.Timestamp;
        
        // Assert
        Assert.Contains("2022", timestamp);
        Assert.Contains("01-01-2022", timestamp);
    }

    [Fact]
    public void FormatterName_WhenSet_ShouldUpdateDecodedMessageAndSummary()
    {
        // Arrange
        var message = CreateTestMessage(value: Encoding.UTF8.GetBytes("test message content"));
        var viewModel = new MessageViewModel(message, "Text", "Text");
        
        // Act
        viewModel.FormatterName = "Text";
        
        // Assert
        Assert.Equal("test message content", viewModel.DecodedMessage);
        Assert.Equal("test message content", viewModel.Summary);
    }

    [Fact]
    public void FormatterName_WhenSetWithLongContent_ShouldTruncateSummary()
    {
        // Arrange
        var longContent = new string('a', 200);
        var message = CreateTestMessage(value: Encoding.UTF8.GetBytes(longContent));
        var viewModel = new MessageViewModel(message, "Text", "Text");
        
        // Act
        viewModel.FormatterName = "Text";
        
        // Assert
        Assert.True(viewModel.Summary.Length <= 103); // 100 + "..."
        Assert.EndsWith("...", viewModel.Summary);
    }

    [Fact]
    public void KeyFormatterName_WhenSet_ShouldUpdateKey()
    {
        // Arrange
        var message = CreateTestMessage(key: Encoding.UTF8.GetBytes("test key"));
        var viewModel = new MessageViewModel(message, "Text", "Text");
        
        // Act
        viewModel.KeyFormatterName = "Text";
        
        // Assert
        Assert.Equal("test key", viewModel.Key);
    }

    [Fact]
    public void UseObjectFilter_WhenSet_ShouldUpdateDisplayText()
    {
        // Arrange
        var message = CreateTestMessage(value: Encoding.UTF8.GetBytes("test content"));
        var viewModel = new MessageViewModel(message, "Text", "Text");
        
        // Act
        viewModel.UseObjectFilter = false;
        
        // Assert
        Assert.Equal("test content", viewModel.DisplayText);
    }

    [Fact]
    public void LineFilter_WhenSet_ShouldUpdateDisplayText()
    {
        // Arrange
        var message = CreateTestMessage(value: Encoding.UTF8.GetBytes("test content"));
        var viewModel = new MessageViewModel(message, "Text", "Text");
        
        // Act
        viewModel.LineFilter = "test";
        
        // Assert
        Assert.Contains("test", viewModel.DisplayText);
    }

    [Fact]
    public void PrettyFormat_ShouldUpdateDisplayText()
    {
        // Arrange
        var message = CreateTestMessage(value: Encoding.UTF8.GetBytes("test content"));
        var viewModel = new MessageViewModel(message, "Text", "Text");
        
        // Act
        viewModel.PrettyFormat();
        
        // Assert
        Assert.Equal("test content", viewModel.DisplayText);
    }

    [Fact]
    public void Cleanup_ShouldClearDisplayText()
    {
        // Arrange
        var message = CreateTestMessage();
        var viewModel = new MessageViewModel(message, "Text", "Text");
        
        // Act
        viewModel.Cleanup();
        
        // Assert
        Assert.Equal("", viewModel.DisplayText);
    }

    [Fact]
    public void FormatterName_WhenSameValue_ShouldNotRaisePropertyChanged()
    {
        // Arrange
        var message = CreateTestMessage();
        var viewModel = new MessageViewModel(message, "Text", "Text");
        var propertyChangedCount = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MessageViewModel.FormatterName))
                propertyChangedCount++;
        };
        
        // Act
        viewModel.FormatterName = "Text";
        
        // Assert
        Assert.Equal("Text", viewModel.FormatterName);
        Assert.Equal(0, propertyChangedCount);
    }

    [Fact]
    public void KeyFormatterName_WhenSameValue_ShouldNotRaisePropertyChanged()
    {
        // Arrange
        var message = CreateTestMessage();
        var viewModel = new MessageViewModel(message, "Text", "Text");
        var propertyChangedCount = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MessageViewModel.KeyFormatterName))
                propertyChangedCount++;
        };
        
        // Act
        viewModel.KeyFormatterName = "Text";
        
        // Assert
        Assert.Equal("Text", viewModel.KeyFormatterName);
        Assert.Equal(0, propertyChangedCount);
    }

    [Fact]
    public void FormatterName_WhenChanged_ShouldRaisePropertyChanged()
    {
        // Arrange
        var message = CreateTestMessage(value: Encoding.UTF8.GetBytes("test"));
        var viewModel = new MessageViewModel(message, "Text", "Text");
        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName!);
        
        // Act
        viewModel.FormatterName = "Number";
        
        // Assert
        Assert.Contains(nameof(MessageViewModel.FormatterName), changedProperties);
    }

    [Fact]
    public void KeyFormatterName_WhenChanged_ShouldRaisePropertyChanged()
    {
        // Arrange
        var message = CreateTestMessage(key: Encoding.UTF8.GetBytes("test"));
        var viewModel = new MessageViewModel(message, "Text", "Text");
        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName!);
        
        // Act
        viewModel.KeyFormatterName = "Number";
        
        // Assert
        Assert.Contains(nameof(MessageViewModel.KeyFormatterName), changedProperties);
    }

    [Fact]
    public void Constructor_WithNullValue_ShouldNotThrow()
    {
        // Arrange
        var message = CreateTestMessage(key: null, value: null);
        
        // Act
        var viewModel = new MessageViewModel(message, "Text", "Text");
        
        // Assert
        Assert.NotNull(viewModel.DecodedMessage);
        Assert.NotNull(viewModel.DisplayText);
    }

    [Fact]
    public void Constructor_WithNullKey_ShouldNotThrow()
    {
        // Arrange
        var message = CreateTestMessage(key: null, value: Encoding.UTF8.GetBytes("value"));
        
        // Act
        var viewModel = new MessageViewModel(message, "Text", "Text");
        
        // Assert
        Assert.NotNull(viewModel.Key);
    }

    private Message CreateTestMessage(byte[]? key = null, byte[]? value = null, long epochMillis = 1640995200000)
    {
        return new Message(epochMillis, new Dictionary<string, byte[]>(), key, value)
        {
            Partition = 0,
            Offset = 1
        };
    }
}
