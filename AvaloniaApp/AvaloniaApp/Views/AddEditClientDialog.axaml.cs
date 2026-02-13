using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using KafkaLens.Clients.Entities;

namespace AvaloniaApp.Views;

public partial class AddEditClientDialog : Window
{
    public ClientInfo? Result { get; private set; }
    private readonly string? _originalName;
    private readonly string? _originalId;
    private readonly HashSet<string> _existingNames;

    public AddEditClientDialog()
    {
        InitializeComponent();
        _existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public AddEditClientDialog(IEnumerable<string> existingNames) : this()
    {
        _existingNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
    }

    public AddEditClientDialog(ClientInfo existing, IEnumerable<string> existingNames) : this(existingNames)
    {
        _originalName = existing.Name;
        _originalId = existing.Id;
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
        if (!string.Equals(newName, _originalName, StringComparison.OrdinalIgnoreCase) && _existingNames.Contains(newName))
        {
            ErrorTextBlock.Text = $"Client with name '{newName}' already exists.";
            return;
        }

        var protocolItem = ProtocolBox.SelectedItem as ComboBoxItem;
        var protocol = protocolItem?.Content?.ToString() ?? "grpc";

        Result = new ClientInfo(_originalId ?? Guid.NewGuid().ToString(), newName, AddressBox.Text.Trim(), protocol);
        Close(Result);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}