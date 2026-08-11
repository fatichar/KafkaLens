using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using KafkaLens.Clients.Entities;
using KafkaLens.Shared.Models;
using Serilog;

namespace AvaloniaApp.Views;

public partial class AddEditClientDialog : DialogBase
{
    public ClientInfo? Result { get; private set; }
    private readonly string? originalName;
    private readonly string? originalId;
    private readonly HashSet<string> existingNames;
    private readonly Func<string, Task<ConnectionValidationResult>>? connectionValidator;

    public AddEditClientDialog()
    {
        InitializeComponent();
        existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public AddEditClientDialog(
        IEnumerable<string> existingNames,
        Func<string, Task<ConnectionValidationResult>>? connectionValidator = null) : this()
    {
        this.existingNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        this.connectionValidator = connectionValidator;
        TestButton.IsVisible = connectionValidator != null;
    }

    public AddEditClientDialog(
        ClientInfo existing,
        IEnumerable<string> existingNames,
        Func<string, Task<ConnectionValidationResult>>? connectionValidator = null) : this(existingNames, connectionValidator)
    {
        originalName = existing.Name;
        originalId = existing.Id;
        NameBox.Text = existing.Name;
        AddressBox.Text = existing.Address;
        Title = "Edit Client";

        // Pre-select protocol
        foreach (var obj in ProtocolBox.Items)
        {
            if (obj is ComboBoxItem item && item.Content?.ToString() == existing.Protocol)
            {
                ProtocolBox.SelectedItem = item;
                break;
            }
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
            Log.Error(ex, "Unexpected error while testing KafkaLens client connection to {Address}", AddressBox.Text.Trim());
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
        ErrorTextBlock.Text = "";

        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(AddressBox.Text))
        {
            ErrorTextBlock.Text = "Name and Address are required.";
            return;
        }

        var newName = NameBox.Text.Trim();
        if (!string.Equals(newName, originalName, StringComparison.OrdinalIgnoreCase) && existingNames.Contains(newName))
        {
            ErrorTextBlock.Text = $"Client with name '{newName}' already exists.";
            return;
        }

        var protocolItem = ProtocolBox.SelectedItem as ComboBoxItem;
        var protocol = protocolItem?.Content?.ToString() ?? "grpc";

        Result = new ClientInfo(originalId ?? Guid.NewGuid().ToString(), newName, AddressBox.Text.Trim(), protocol);
        Close(Result);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}