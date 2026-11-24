using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Models;
using KafkaLens.Shared.Security;
using Serilog;

namespace KafkaLens.UI.Views;

/// <summary>What the user entered: address, security settings and any newly typed secrets.</summary>
public sealed record ClusterConnectionInput(string Address, ClusterSecurity Security, Dictionary<string, string> Secrets);

public partial class AddEditClusterDialog : DialogBase
{
    /// <summary>The edited cluster. <see cref="ClusterInfo.Secrets"/> holds only newly entered secrets.</summary>
    public ClusterInfo? Result { get; private set; }
    private readonly string? originalName;
    private readonly string? originalId;
    private readonly HashSet<string> existingNames;
    private readonly HashSet<string> storedSecretNames = new(StringComparer.Ordinal);
    private readonly Func<ClusterConnectionInput, Task<ConnectionValidationResult>>? connectionValidator;

    public AddEditClusterDialog()
    {
        InitializeComponent();
        existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ProtocolCombo.SelectedIndex = 0;
    }

    /// <param name="secretStore">Where secrets will be kept; used to tell the user whether they persist.</param>
    public AddEditClusterDialog(IEnumerable<string> existingNames,
        Func<ClusterConnectionInput, Task<ConnectionValidationResult>>? connectionValidator = null,
        ISecretStore? secretStore = null) : this()
    {
        this.existingNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        this.connectionValidator = connectionValidator;
        if (secretStore != null)
        {
            SecretStorageHint.Text = secretStore.IsPersistent
                ? $"Passwords are stored in {secretStore.BackendName}, not in the settings file."
                : "No OS credential storage is available: passwords are kept for this session only and are not saved.";
        }
        UpdateTestButton();
    }

    /// <param name="existing">Cluster to edit; <see cref="ClusterInfo.StoredSecretNames"/> marks secrets that are already saved.</param>
    public AddEditClusterDialog(ClusterInfo existing, IEnumerable<string> existingNames,
        Func<ClusterConnectionInput, Task<ConnectionValidationResult>>? connectionValidator = null,
        ISecretStore? secretStore = null) : this(existingNames, connectionValidator, secretStore)
    {
        originalName = existing.Name;
        originalId = existing.Id;
        NameBox.Text = existing.Name;
        AddressBox.Text = existing.Address;
        foreach (var name in existing.StoredSecretNames ?? []) storedSecretNames.Add(name);
        LoadSecurity(existing.Security ?? new ClusterSecurity());
        Title = "Edit Cluster";
    }

    private void LoadSecurity(ClusterSecurity security)
    {
        SelectProtocol(security.Protocol);
        CaLocationBox.Text = security.SslCaLocation;
        CertificateLocationBox.Text = security.SslCertificateLocation;
        KeyLocationBox.Text = security.SslKeyLocation;
        if (storedSecretNames.Contains(ClusterSecretNames.SslKeyPassword))
            KeyPasswordBox.PlaceholderText = "saved — leave empty to keep";
        SaslUsernameBox.Text = security.SaslUsername;
        var mechanism = MechanismCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string?)i.Tag == security.EffectiveSaslMechanism.ToString());
        if (mechanism != null) MechanismCombo.SelectedItem = mechanism;
        if (storedSecretNames.Contains(ClusterSecretNames.SaslPassword))
            SaslPasswordBox.PlaceholderText = "saved — leave empty to keep";
    }

    private KafkaSaslMechanism SelectedMechanism =>
        MechanismCombo.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse<KafkaSaslMechanism>(tag, out var m)
            ? m
            : KafkaSaslMechanism.Plain;

    private void SelectProtocol(KafkaSecurityProtocol protocol)
    {
        var item = ProtocolCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == protocol.ToString());
        ProtocolCombo.SelectedItem = item ?? ProtocolCombo.Items[0];
    }

    private KafkaSecurityProtocol SelectedProtocol =>
        ProtocolCombo.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse<KafkaSecurityProtocol>(tag, out var p)
            ? p
            : KafkaSecurityProtocol.Plaintext;

    private void ProtocolCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateSecurityPanels();

    private void UpdateSecurityPanels()
    {
        if (TlsPanel == null) return;
        var security = new ClusterSecurity { Protocol = SelectedProtocol };
        TlsPanel.IsVisible = security.UsesTls;
        SaslPanel.IsVisible = security.UsesSasl;
        UnencryptedWarning.IsVisible = security.SendsCredentialsUnencrypted;
        SecretStorageHint.IsVisible = !string.IsNullOrEmpty(SecretStorageHint.Text) && !security.IsDefault;
    }

    private ClusterSecurity ReadSecurity() => new ClusterSecurity
    {
        Protocol = SelectedProtocol,
        SslCaLocation = CaLocationBox.Text,
        SslCertificateLocation = CertificateLocationBox.Text,
        SslKeyLocation = KeyLocationBox.Text,
        SaslMechanism = SelectedMechanism,
        SaslUsername = SaslUsernameBox.Text
    }.Normalized();

    /// <summary>Only secrets the user typed; empty fields mean "keep the saved value".</summary>
    private Dictionary<string, string> ReadSecrets()
    {
        var secrets = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(KeyPasswordBox.Text)) secrets[ClusterSecretNames.SslKeyPassword] = KeyPasswordBox.Text;
        if (!string.IsNullOrEmpty(SaslPasswordBox.Text)) secrets[ClusterSecretNames.SaslPassword] = SaslPasswordBox.Text;
        return secrets;
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target } || this.FindControl<TextBox>(target) is not { } box) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select a PEM file",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("PEM files") { Patterns = ["*.pem", "*.crt", "*.cer", "*.key"] },
                    FilePickerFileTypes.All
                ]
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) box.Text = path;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not open file picker");
        }
    }

    private void UpdateTestButton()
    {
        if (TestButton != null)
        {
            TestButton.IsVisible = connectionValidator != null;
        }
    }

    private void ClearMessages()
    {
        StatusTextBlock.Text = "";
        ErrorTextBlock.Text = "";
        DetailsExpander.IsVisible = false;
        DetailsExpander.IsExpanded = false;
        DetailsTextBox.Text = "";
    }

    private async void TestButton_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AddressBox.Text) || connectionValidator == null) return;

        ClearMessages();
        var security = ReadSecurity();
        if (security.Validate() is { } problem)
        {
            ErrorTextBlock.Text = problem;
            return;
        }

        TestButton.IsEnabled = false;
        StatusTextBlock.Text = "Testing connection...";
        StatusTextBlock.Foreground = Brushes.Blue;

        try
        {
            var result = await connectionValidator(new ClusterConnectionInput(AddressBox.Text.Trim(), security, ReadSecrets()));
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
            var message = SecretRedactor.Redact(ex.Message, ReadSecrets().Values);
            Log.Error("Unexpected error while testing Kafka connection to {Address}: {Error}", AddressBox.Text.Trim(), message);
            StatusTextBlock.Text = "";
            ErrorTextBlock.Text = $"Error: {message}";
            DetailsTextBox.Text = SecretRedactor.Redact(ex.ToString(), ReadSecrets().Values);
            DetailsExpander.IsVisible = true;
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        ClearMessages();

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

        var security = ReadSecurity();
        if (security.Validate() is { } problem)
        {
            ErrorTextBlock.Text = problem;
            return;
        }
        if (security.UsesSasl && string.IsNullOrEmpty(SaslPasswordBox.Text) && !storedSecretNames.Contains(ClusterSecretNames.SaslPassword))
        {
            ErrorTextBlock.Text = "A SASL password is required.";
            return;
        }

        Result = new ClusterInfo(originalId ?? Guid.NewGuid().ToString(), newName, AddressBox.Text.Trim())
        {
            Security = security,
            Secrets = ReadSecrets()
        };
        Close(Result);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
