using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace RRDA.RepImp
{
    public partial class SettingsDialog : Window
    {
        public SettingsDialog()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            ReportsFolderTextBox.Text = Properties.Settings.Default.ReportsFolder ?? string.Empty;
            PluginsFolderTextBox.Text = Properties.Settings.Default.PluginsFolder ?? string.Empty;
            ConnectionStringTextBox.Text = Properties.Settings.Default.ConnectionString ?? string.Empty;

            // Carica RecurseDepth (valore intero). Se non presente o non impostato, mostra 0.
            try
            {
                RecurseDepthTextBox.Text = Properties.Settings.Default.RecurseDepth.ToString();
            }
            catch
            {
                RecurseDepthTextBox.Text = "0";
            }
        }

        private void BrowseReportsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFolderDialog
                {
                    FolderName = ReportsFolderTextBox.Text,
                    Title = "Seleziona cartella Reports"
                };

                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
                {
                    ReportsFolderTextBox.Text = dlg.FolderName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Impossibile selezionare la cartella:{Environment.NewLine}{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowsePluginsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFolderDialog
                {
                    FolderName = PluginsFolderTextBox.Text,
                    Title = "Seleziona cartella Plugins"
                };

                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
                {
                    PluginsFolderTextBox.Text = dlg.FolderName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Impossibile selezionare la cartella:{Environment.NewLine}{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Validazione minima: accetta cartelle vuote ma verifica esistenza se valorizzate
            if (!string.IsNullOrWhiteSpace(ReportsFolderTextBox.Text) && !Directory.Exists(ReportsFolderTextBox.Text))
            {
                var res = MessageBox.Show(this, "La cartella Reports specificata non esiste. Vuoi comunque salvarla?", "Cartella non trovata", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res != MessageBoxResult.Yes) return;
            }

            if (!string.IsNullOrWhiteSpace(PluginsFolderTextBox.Text) && !Directory.Exists(PluginsFolderTextBox.Text))
            {
                var res = MessageBox.Show(this, "La cartella Plugins specificata non esiste. Vuoi comunque salvarla?", "Cartella non trovata", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res != MessageBoxResult.Yes) return;
            }

            // Validazione RecurseDepth: intero >= 0
            int depth = 0;
            if (!string.IsNullOrWhiteSpace(RecurseDepthTextBox.Text))
            {
                if (!int.TryParse(RecurseDepthTextBox.Text, out depth) || depth < 0)
                {
                    MessageBox.Show(this, "La profondità di ricorsione deve essere un intero maggiore o uguale a 0.", "Valore non valido", MessageBoxButton.OK, MessageBoxImage.Warning);
                    RecurseDepthTextBox.Focus();
                    return;
                }
            }

            Properties.Settings.Default.ReportsFolder = string.IsNullOrWhiteSpace(ReportsFolderTextBox.Text) ? null : ReportsFolderTextBox.Text;
            Properties.Settings.Default.PluginsFolder = string.IsNullOrWhiteSpace(PluginsFolderTextBox.Text) ? null : PluginsFolderTextBox.Text;
            Properties.Settings.Default.ConnectionString = string.IsNullOrWhiteSpace(ConnectionStringTextBox.Text) ? null : ConnectionStringTextBox.Text;
            Properties.Settings.Default.RecurseDepth = depth;
            Properties.Settings.Default.Save();

            MessageBox.Show(this, "Impostazioni salvate.", "Salvataggio", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}