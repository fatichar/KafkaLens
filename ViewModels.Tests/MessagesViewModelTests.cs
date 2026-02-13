namespace KafkaLens.ViewModels.Tests;

using System.Text;

public class MessagesViewModelTests
{
    private readonly IFixture _fixture;

    public MessagesViewModelTests()
    {
        _fixture = new Fixture();
    }

    [Fact]
    public void Constructor_ShouldInitializeCollections()
    {
        // Act
        var viewModel = new MessagesViewModel();
        
        // Assert
        Assert.NotNull(viewModel.Messages);
        Assert.NotNull(viewModel.Filtered);
        Assert.True(viewModel.UseObjectFilter);
        Assert.Null(viewModel.CurrentMessage);
        Assert.False(viewModel.IsMessageSelected);
        Assert.Equal("", viewModel.PositiveFilter);
        Assert.Equal("", viewModel.NegativeFilter);
        Assert.Equal("", viewModel.LineFilter);
    }

    [Fact]
    public void UseObjectFilter_WhenSet_ShouldUpdateCurrentMessage()
    {
        // Arrange
        var viewModel = new MessagesViewModel();
        var message = CreateMockMessageViewModel();
        viewModel.CurrentMessage = message;
        
        // Act
        viewModel.UseObjectFilter = false;
        
        // Assert
        Assert.False(viewModel.UseObjectFilter);
        Assert.False(message.UseObjectFilter);
    }

    [Fact]
    public void CurrentMessage_WhenSet_ShouldUpdateProperties()
    {
        // Arrange
        var viewModel = new MessagesViewModel();
        var message = CreateMockMessageViewModel();
        viewModel.LineFilter = "test filter";
        viewModel.UseObjectFilter = false;
        
        // Act
        viewModel.CurrentMessage = message;
        
        // Assert
        Assert.Equal(message, viewModel.CurrentMessage);
        Assert.True(viewModel.IsMessageSelected);
        Assert.Equal("test filter", message.LineFilter);
        Assert.False(message.UseObjectFilter);
    }

    [Fact]
    public void CurrentMessage_WhenChanged_ShouldUpdateCorrectly()
    {
        // Arrange
        var viewModel = new MessagesViewModel();
        var firstMessage = CreateMockMessageViewModel("first");
        var secondMessage = CreateMockMessageViewModel("second");
        viewModel.CurrentMessage = firstMessage;
        
        // Act
        viewModel.CurrentMessage = secondMessage;
        
        // Assert
        Assert.Equal(secondMessage, viewModel.CurrentMessage);
        Assert.NotEqual(firstMessage, viewModel.CurrentMessage);
    }

    [Fact]
    public void CurrentMessage_WhenSetToNullWithExistingSelection_ShouldNotClear()
    {
        // Arrange
        var viewModel = new MessagesViewModel();
        var message = CreateMockMessageViewModel();
        viewModel.Add(message);
        viewModel.CurrentMessage = message;
        
        // Act
        viewModel.CurrentMessage = null;
        
        // Assert
        Assert.Equal(message, viewModel.CurrentMessage);
        Assert.True(viewModel.IsMessageSelected);
    }

    [Fact]
    public void PositiveFilter_WhenSet_ShouldApplyFilter()
    {
        // Arrange
        var viewModel = new MessagesViewModel();
        var matchingMessage = CreateMockMessageViewModel("test content");
        var nonMatchingMessage = CreateMockMessageViewModel("other content");
        viewModel.Add(matchingMessage);
        viewModel.Add(nonMatchingMessage);
        
        // Act
        viewModel.PositiveFilter = "test";
        
        // Assert
        Assert.Single(viewModel.Filtered);
        Assert.Equal(matchingMessage, viewModel.Filtered.First());
    }

    [Fact]
    public void NegativeFilter_WhenSet_ShouldApplyFilter()
    {
        // Arrange
        var viewModel = new MessagesViewModel();
        var goodMessage = CreateMockMessageViewModel("good content");
        var badMessage = CreateMockMessageViewModel("bad content");
        viewModel.Add(goodMessage);
        viewModel.Add(badMessage);
        
        // Act
        viewModel.NegativeFilter = "bad";
        
        // Assert
        Assert.Single(viewModel.Filtered);
        Assert.Equal(goodMessage, viewModel.Filtered.First());
    }

    [Fact]
    public void Add_ShouldAddToFilteredWhenMatches()
    {
        // Arrange
        var viewModel = new MessagesViewModel();
        viewModel.PositiveFilter = "test";
        var message = CreateMockMessageViewModel("test content");
        
        // Act
        viewModel.Add(message);
        
        // Assert
        Assert.Contains(message, viewModel.Messages);
        Assert.Contains(message, viewModel.Filtered);
    }

    [Fact]
    public void Add_ShouldNotAddToFilteredWhenNotMatching()
    {
        // Arrange
        var viewModel = new MessagesViewModel();
        viewModel.PositiveFilter = "test";
        var message = CreateMockMessageViewModel("other content");
        
        // Act
        viewModel.Add(message);
        
        // Assert
        Assert.Contains(message, viewModel.Messages);
        Assert.DoesNotContain(message, viewModel.Filtered);
    }

    [Fact]
    public void Clear_ShouldClearAllCollectionsAndSelection()
    {
        // Arrange
        var viewModel = new MessagesViewModel();
        var message = CreateMockMessageViewModel();
        viewModel.Add(message);
        viewModel.CurrentMessage = message;
        
        // Act
        viewModel.Clear();
        
        // Assert
        Assert.Empty(viewModel.Messages);
        Assert.Empty(viewModel.Filtered);
        Assert.Null(viewModel.CurrentMessage);
    }

    [Fact]
    public void LineFilter_WhenSet_ShouldUpdateCurrentMessage()
    {
        // Arrange
        var viewModel = new MessagesViewModel();
        var message = CreateMockMessageViewModel();
        viewModel.CurrentMessage = message;
        
        // Act
        viewModel.LineFilter = "new filter";
        
        // Assert
        Assert.Equal("new filter", message.LineFilter);
    }

    [Fact]
    public void CurrentMessage_WhenSet_ShouldRaisePropertyChanged()
    {
        // Arrange
        var viewModel = new MessagesViewModel();
        var message = CreateMockMessageViewModel();
        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName!);
        
        // Act
        viewModel.CurrentMessage = message;
        
        // Assert
        Assert.Contains(nameof(MessagesViewModel.CurrentMessage), changedProperties);
        Assert.Contains(nameof(MessagesViewModel.IsMessageSelected), changedProperties);
    }

    [Fact]
    public void CombinedFilters_ShouldApplyBothPositiveAndNegative()
    {
        // Arrange
        var viewModel = new MessagesViewModel();
        var msg1 = CreateMockMessageViewModel("good test content");
        var msg2 = CreateMockMessageViewModel("bad test content");
        var msg3 = CreateMockMessageViewModel("good other content");
        var msg4 = CreateMockMessageViewModel("bad other content");
        viewModel.Add(msg1);
        viewModel.Add(msg2);
        viewModel.Add(msg3);
        viewModel.Add(msg4);
        
        // Act
        viewModel.PositiveFilter = "test";
        viewModel.NegativeFilter = "bad";
        
        // Assert
        Assert.Single(viewModel.Filtered);
        Assert.Equal(msg1, viewModel.Filtered.First());
    }

    [Fact]
    public void PositiveFilter_ShouldBeCaseInsensitive()
    {
        // Arrange
        var viewModel = new MessagesViewModel();
        var message = CreateMockMessageViewModel("Test Content");
        viewModel.Add(message);
        
        // Act
        viewModel.PositiveFilter = "test content";
        
        // Assert
        Assert.Single(viewModel.Filtered);
        Assert.Equal(message, viewModel.Filtered.First());
    }

    [Fact]
    public void NegativeFilter_ShouldBeCaseInsensitive()
    {
        // Arrange
        var viewModel = new MessagesViewModel();
        var msg1 = CreateMockMessageViewModel("Good Content");
        var msg2 = CreateMockMessageViewModel("BAD Content");
        viewModel.Add(msg1);
        viewModel.Add(msg2);
        
        // Act
        viewModel.NegativeFilter = "bad";
        
        // Assert
        Assert.Single(viewModel.Filtered);
        Assert.Equal(msg1, viewModel.Filtered.First());
    }

    [Fact]
    public void CurrentMessage_WhenChanged_ShouldCleanupPreviousMessage()
    {
        // Arrange
        var viewModel = new MessagesViewModel();
        var firstMessage = CreateMockMessageViewModel("first");
        var secondMessage = CreateMockMessageViewModel("second");
        viewModel.CurrentMessage = firstMessage;
        
        // Act
        viewModel.CurrentMessage = secondMessage;
        
        // Assert
        Assert.Equal("", firstMessage.DisplayText);
    }

    [Fact]
    public void LineFilter_WhenNoCurrentMessage_ShouldNotThrow()
    {
        // Arrange
        var viewModel = new MessagesViewModel();
        
        // Act & Assert
        viewModel.LineFilter = "some filter";
        Assert.Equal("some filter", viewModel.LineFilter);
    }

    [Fact]
    public void UseObjectFilter_WhenNoCurrentMessage_ShouldNotThrow()
    {
        // Arrange
        var viewModel = new MessagesViewModel();
        
        // Act & Assert
        viewModel.UseObjectFilter = false;
        Assert.False(viewModel.UseObjectFilter);
    }

    private MessageViewModel CreateMockMessageViewModel(string decodedMessage = "test message")
    {
        var message = new Message(1640995200000, new Dictionary<string, byte[]>(), 
            Encoding.UTF8.GetBytes("test key"), Encoding.UTF8.GetBytes(decodedMessage));
        return new MessageViewModel(message, "Text", "Text");
    }
}
