using Avalonia.Controls;
using Avalonia.Interactivity;
using KafkaLens.Clients.Entities;

namespace AvaloniaApp.Views;

public partial class AddEditClientDialog : Window
{
    public ClientInfo? Result { get; private set; }
    private readonly string _originalId = "";

    public AddEditClientDialog()
    {
        InitializeComponent();
    }

    public AddEditClientDialog(ClientInfo existing) : this()
    {
        _originalId = existing.Id;
        NameBox.Text = existing.Name;
        AddressBox.Text = existing.Address;
        Title = "Edit Client";
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(AddressBox.Text))
        {
            return;
        }

        var protocolItem = ProtocolBox.SelectedItem as ComboBoxItem;
        var protocol = protocolItem?.Content?.ToString() ?? "grpc";

        Result = new ClientInfo(_originalId, NameBox.Text, AddressBox.Text, protocol);
        Close(Result);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}