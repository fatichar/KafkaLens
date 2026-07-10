using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Models;
using Serilog;

namespace AvaloniaApp.Views;

public partial class AddEditClusterDialog : DialogBase
{
    public ClusterInfo? Result { get; private set; }
    private readonly string? originalName;
    private readonly string? originalId;
    private readonly HashSet<string> existingNames;
    private readonly Func<string, Task<ConnectionValidationResult>>? connectionValidator;

    public AddEditClusterDialog()
    {
        InitializeComponent();
        existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public AddEditClusterDialog(IEnumerable<string> existingNames, Func<string, Task<ConnectionValidationResult>>? connectionValidator = null) : this()
    {
        this.existingNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        this.connectionValidator = connectionValidator;
        UpdateTestButton();
    }

    public AddEditClusterDialog(ClusterInfo existing, IEnumerable<string> existingNames, Func<string, Task<ConnectionValidationResult>>? connectionValidator = null) : this(existingNames, connectionValidator)
    {
        originalName = existing.Name;
        originalId = existing.Id;
        NameBox.Text = existing.Name;
        AddressBox.Text = existing.Address;
        Title = "Edit Cluster";
    }

    private void UpdateTestButton()
    {
        if (TestButton != null)
        {
            TestButton.IsVisible = connectionValidator != null;
        }
    }

    private async void TestButton_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AddressBox.Text) || connectionValidator == null) return;

        TestButton.IsEnabled = false;
        StatusTextBlock.Text = "Testing connection...";
        StatusTextBlock.Foreground = Brushes.Blue;
        ErrorTextBlock.Text = "";
        DetailsExpander.IsVisible = false;
        DetailsExpander.IsExpanded = false;
        DetailsTextBox.Text = "";

        try
        {
            var result = await connectionValidator(AddressBox.Text.Trim());
            if (result.Succeeded)
            {
                StatusTextBlock.Text = "Connected successfully.";
                StatusTextBlock.Foreground = Brushes.Green;
            }
            else
            {
                StatusTextBlock.Text = "";
                ErrorTextBlock.Text = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "Failed to connect."
                    : $"Failed to connect: {result.ErrorMessage}";
                DetailsTextBox.Text = result.ErrorDetails ?? "The connection check failed without additional technical details.";
                DetailsExpander.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error while testing Kafka connection to {Address}", AddressBox.Text.Trim());
            StatusTextBlock.Text = "";
            ErrorTextBlock.Text = $"Error: {ex.Message}";
            DetailsTextBox.Text = ex.ToString();
            DetailsExpander.IsVisible = true;
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
        DetailsExpander.IsVisible = false;
        DetailsExpander.IsExpanded = false;
        DetailsTextBox.Text = "";

        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(AddressBox.Text))
        {
            ErrorTextBlock.Text = "Name and Address are required.";
            return;
        }

        var newName = NameBox.Text.Trim();
        // If editing, the original name is allowed (no change), but duplicates are not.
        if (!string.Equals(newName, originalName, StringComparison.OrdinalIgnoreCase) && existingNames.Contains(newName))
        {
            ErrorTextBlock.Text = $"Cluster with name '{newName}' already exists.";
            return;
        }

        Result = new ClusterInfo(originalId ?? Guid.NewGuid().ToString(), newName, AddressBox.Text.Trim());
        Close(Result);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}