using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace AvaloniaApp.Views;

public class DialogBase : Window
{
    public DialogBase()
    {
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Escape),
            Command = new RelayCommand(OnCancel)
        });
    }

    protected virtual void OnCancel()
    {
        Close();
    }
}
