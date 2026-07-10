using System.Threading.Tasks;
using Avalonia.Controls;

namespace AvaloniaApp.Views;

public partial class SimpleMessageBox : DialogBase
{
    private bool isConfirmation;

    public SimpleMessageBox()
    {
        InitializeComponent();
    }

    public SimpleMessageBox(string title, string message, bool isConfirmation)
        : this()
    {
        this.isConfirmation = isConfirmation;

        Title = title;
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;

        if (isConfirmation)
        {
            PrimaryButton.Content = "Yes";
            SecondaryButton.Content = "No";
            SecondaryButton.IsVisible = true;
        }
        else
        {
            PrimaryButton.Content = "OK";
        }
    }

    public Task ShowMessageAsync(Window owner)
    {
        return ShowDialog(owner);
    }

    public Task<bool> ShowConfirmationAsync(Window owner)
    {
        return ShowDialog<bool>(owner);
    }

    private void PrimaryButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (isConfirmation)
        {
            Close(true);
            return;
        }

        Close();
    }

    protected override void OnCancel()
    {
        if (isConfirmation)
            Close(false);
        else
            Close();
    }

    private void SecondaryButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }
}
