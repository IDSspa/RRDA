using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RRDA.Core;

namespace RRDA.RepImp
{
    /// <summary>
    /// Logica di interazione per ForcePluginDialog.xaml
    /// </summary>
    public partial class ForcePluginDialog : Window
    {
        public ForcePluginDialog(IEnumerable<IReportImporter> plugins)
        {
            InitializeComponent();
            PluginsListBox.ItemsSource = plugins ?? throw new ArgumentNullException(nameof(plugins));
        }

        public IReportImporter? SelectedPlugin => PluginsListBox.SelectedItem as IReportImporter;
        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPlugin is null)
                return;

            DialogResult = true;
        }

        private void PluginsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            OkButton.IsEnabled = SelectedPlugin is not null;
        }

        private void PluginsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SelectedPlugin is not null)
                DialogResult = true;
        }
    }
}
