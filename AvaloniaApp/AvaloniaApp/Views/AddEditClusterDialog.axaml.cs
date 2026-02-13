using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using KafkaLens.Shared.Entities;

namespace AvaloniaApp.Views;

public partial class AddEditClusterDialog : Window
{
    public ClusterInfo? Result { get; private set; }
    private readonly string? _originalName = null;
    private readonly string? _originalId = null;
    private readonly HashSet<string> _existingNames;
    private readonly Func<string, Task<bool>>? _connectionValidator;

    public AddEditClusterDialog()
    {
        InitializeComponent();
        _existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public AddEditClusterDialog(IEnumerable<string> existingNames, Func<string, Task<bool>>? connectionValidator = null) : this()
    {
        _existingNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        _connectionValidator = connectionValidator;
        UpdateTestButton();
    }

    public AddEditClusterDialog(ClusterInfo existing, IEnumerable<string> existingNames, Func<string, Task<bool>>? connectionValidator = null) : this(existingNames, connectionValidator)
    {
        _originalName = existing.Name;
        _originalId = existing.Id;
        NameBox.Text = existing.Name;
        AddressBox.Text = existing.Address;
        Title = "Edit Cluster";
    }

    private void UpdateTestButton()
    {
        if (TestButton != null)
        {
            TestButton.IsVisible = _connectionValidator != null;
        }
    }

    private async void TestButton_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AddressBox.Text) || _connectionValidator == null) return;

        TestButton.IsEnabled = false;
        StatusTextBlock.Text = "Testing connection...";
        StatusTextBlock.Foreground = Brushes.Blue;
        ErrorTextBlock.Text = "";

        try
        {
            bool connected = await _connectionValidator(AddressBox.Text.Trim());
            if (connected)
            {
                StatusTextBlock.Text = "Connected successfully.";
                StatusTextBlock.Foreground = Brushes.Green;
            }
            else
            {
                StatusTextBlock.Text = "";
                ErrorTextBlock.Text = "Failed to connect.";
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "";
            ErrorTextBlock.Text = $"Error: {ex.Message}";
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        StatusTextBlock.Text = "";
        ErrorTextBlock.Text = "";

        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(AddressBox.Text))
        {
            ErrorTextBlock.Text = "Name and Address are required.";
            return;
        }

        var newName = NameBox.Text.Trim();
        // If editing, the original name is allowed (no change), but duplicates are not.
        if (!string.Equals(newName, _originalName, StringComparison.OrdinalIgnoreCase) && _existingNames.Contains(newName))
        {
            ErrorTextBlock.Text = $"Cluster with name '{newName}' already exists.";
            return;
        }

        Result = new ClusterInfo(_originalId ?? Guid.NewGuid().ToString(), newName, AddressBox.Text.Trim());
        Close(Result);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}