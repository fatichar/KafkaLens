using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KafkaLens.ViewModels;

namespace KafkaLens.App.Controls;

public partial class OpenedClusterPanel : UserControl, IMessageLoadListener
{
    private OpenedClusterViewModel dataContext => (OpenedClusterViewModel)DataContext;
        
    private readonly DispatcherTimer messageRefreshTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(100)
    };
    public OpenedClusterPanel()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        messageRefreshTimer.Tick += MessageRefreshTimer_Tick;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue != null)
        {
            dataContext.RemoveMessageLoadListener(this);
        }
        if (e.NewValue != null)
        {
            dataContext.AddMessageLoadListener(this);
        }
    }

    private void MessageRefreshTimer_Tick(object? sender, EventArgs e)
    {
        dataContext.UpdateMessages();
    }

    public void MessageLoadingStarted()
    {
        messageRefreshTimer.Start();
    }

    public void MessageLoadingFinished()
    {
        messageRefreshTimer.Stop();
    }
}