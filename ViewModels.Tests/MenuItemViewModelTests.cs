namespace KafkaLens.ViewModels.Tests;

public class MenuItemViewModelTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var menuItem = new MenuItemViewModel();
        
        // Assert
        Assert.Equal("", menuItem.Header);
        Assert.Null(menuItem.Command);
        Assert.Null(menuItem.CommandParameter);
        Assert.Null(menuItem.Items);
        Assert.True(menuItem.IsEnabled);
        Assert.Null(menuItem.Icon);
    }

    [Fact]
    public void Properties_ShouldSetAndGetCorrectly()
    {
        // Arrange
        var command = Substitute.For<System.Windows.Input.ICommand>();
        var items = new System.Collections.ObjectModel.ObservableCollection<MenuItemViewModel>();
        var icon = new object();
        
        var menuItem = new MenuItemViewModel
        {
            Header = "Test Menu",
            Command = command,
            CommandParameter = "parameter",
            Items = items,
            IsEnabled = false,
            Icon = icon
        };
        
        // Assert
        Assert.Equal("Test Menu", menuItem.Header);
        Assert.Equal(command, menuItem.Command);
        Assert.Equal("parameter", menuItem.CommandParameter);
        Assert.Equal(items, menuItem.Items);
        Assert.False(menuItem.IsEnabled);
        Assert.Equal(icon, menuItem.Icon);
    }
}
