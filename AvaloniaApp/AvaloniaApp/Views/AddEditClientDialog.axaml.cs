using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using KafkaLens.Clients.Entities;

namespace AvaloniaApp.Views;

public partial class AddEditClientDialog : DialogBase
{
    public ClientInfo? Result { get; private set; }
    private readonly string? originalName;
    private readonly string? originalId;
    private readonly HashSet<string> existingNames;

    public AddEditClientDialog()
    {
        InitializeComponent();
        existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public AddEditClientDialog(IEnumerable<string> existingNames) : this()
    {
        this.existingNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
    }

    public AddEditClientDialog(ClientInfo existing, IEnumerable<string> existingNames) : this(existingNames)
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