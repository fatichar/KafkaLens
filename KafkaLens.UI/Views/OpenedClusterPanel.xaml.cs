using System;
using System.Windows;
using KafkaLens.ViewModels;

namespace KafkaLens.Views
{
    public partial class OpenedClusterPanel : UserControl, IMessageLoadListener
    {
         private OpenedClusterViewModel dataContext => (OpenedClusterViewModel)DataContext;

        private readonly DispatcherTimer messageRefreshTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(1000)
        };

        public OpenedClusterPanel()
        {
            InitializeComponent();

            DataContextChanged += OnDataContextChanged;
            messageRefreshTimer.Tick += OnMessageRefreshTimerTick;
        }

        private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs e)
        {
            if (e.NewValue != null)
            {
                if (e.NewValue is MainViewModel)
                {
                    DataContextChanged -= OnDataContextChanged;
                    var mainViewModel = (e.NewValue as MainViewModel);
                    DataContext = mainViewModel.OpenedClusters[mainViewModel.SelectedIndex];
                    DataContextChanged += OnDataContextChanged;
                }
                dataContext.AddMessageLoadListener(this);
            }
        }

        private void OnMessageRefreshTimerTick(object? sender, object e)
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
}