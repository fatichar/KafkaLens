﻿using KafkaLens.App.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace KafkaLens.App.Controls
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class Browser : UserControl
    {
        private OpenedClusterViewModel dataContext => (OpenedClusterViewModel)DataContext;

        public Browser()
        {
            InitializeComponent();
        }


        private void Selector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dataContext != null && dataGrid.SelectedItem != null)
                dataContext.CurrentMessages.CurrentMessage = (MessageViewModel)dataGrid.SelectedItem;
        }
    }
}
