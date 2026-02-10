using System;
using System.Collections.Generic;
using System.Linq;
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

    public AddEditClusterDialog()
    {
        InitializeComponent();
        _existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public AddEditClusterDialog(IEnumerable<string> existingNames) : this()
    {
        _existingNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
    }

    public AddEditClusterDialog(ClusterInfo existing, IEnumerable<string> existingNames) : this(existingNames)
    {
        _originalName = existing.Name;
        _originalId = existing.Id;
        NameBox.Text = existing.Name;
        AddressBox.Text = existing.Address;
        Title = "Edit Cluster";
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