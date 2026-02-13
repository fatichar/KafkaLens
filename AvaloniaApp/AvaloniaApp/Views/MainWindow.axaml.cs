using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using KafkaLens.ViewModels;

namespace AvaloniaApp.Views;

public partial class MainWindow : Window
{
    private static readonly Dictionary<Key, char> AccessKeyMap = new()
    {
        { Key.C, 'C' },
        { Key.V, 'V' },
        { Key.H, 'H' },
    };

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs args)
    {
        var dataContext = DataContext as MainViewModel;
        Title = dataContext?.Title ?? "KafkaLens";
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Alt && AccessKeyMap.TryGetValue(e.Key, out var accessChar))
        {
            var mainView = this.GetVisualDescendants().OfType<MainView>().FirstOrDefault();
            var menu = mainView?.FindControl<Menu>("MenuBar");
            if (menu != null)
            {
                var menuItems = menu.GetVisualDescendants().OfType<MenuItem>().ToList();
                foreach (var menuItem in menuItems)
                {
                    if (menuItem.Parent == menu && menuItem.Header is string header)
                    {
                        var underscoreIndex = header.IndexOf('_');
                        if (underscoreIndex >= 0 && underscoreIndex + 1 < header.Length &&
                            char.ToUpperInvariant(header[underscoreIndex + 1]) == accessChar)
                        {
                            menuItem.Open();
                            menuItem.Focus();
                            e.Handled = true;
                            break;
                        }
                    }
                }
            }
        }

        base.OnKeyDown(e);
    }
}