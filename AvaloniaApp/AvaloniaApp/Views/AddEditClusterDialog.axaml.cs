using Avalonia.Controls;
using Avalonia.Interactivity;
using KafkaLens.Shared.Entities;

namespace AvaloniaApp.Views;

public partial class AddEditClusterDialog : Window
{
    public ClusterInfo? Result { get; private set; }
    private readonly string _originalId = "";

    public AddEditClusterDialog()
    {
        InitializeComponent();
    }

    public AddEditClusterDialog(ClusterInfo existing) : this()
    {
        _originalId = existing.Id;
        NameBox.Text = existing.Name;
        AddressBox.Text = existing.Address;
        Title = "Edit Cluster";
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(AddressBox.Text))
        {
            // Simple validation
            return;
        }

        Result = new ClusterInfo(_originalId, NameBox.Text, AddressBox.Text);
        Close(Result);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}