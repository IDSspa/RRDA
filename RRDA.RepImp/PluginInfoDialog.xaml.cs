using RRDA.Plugins.Common;
using System.Windows;

namespace RRDA.RepImp;

public partial class PluginInfoDialog: Window
{
    public PluginInfoDialog(BaseImporter importer)
    {
        InitializeComponent();

        DataContext = importer;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}