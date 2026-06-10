using Microsoft.Win32;
using RRDA.Core.Validator;
using System.IO;
using System.Windows;
using System.Windows.Controls;

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
            UnitMappingsTextBox.Text = Properties.Settings.Default.UnitMappings ?? string.Empty;
            ImportBanListTextBox.Text = Properties.Settings.Default.ImportBanList ?? string.Empty;
            ConnectionStringTextBox.Text = Properties.Settings.Default.ConnectionString ?? string.Empty;
            var theme = ThemeManager.Normalize(Properties.Settings.Default.Theme);
            ThemeComboBox.SelectedItem = ThemeComboBox.Items
                .OfType<ComboBoxItem>()
                .First(item => string.Equals(item.Tag?.ToString(), theme, StringComparison.OrdinalIgnoreCase));

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

        private void BrowseUnitMappingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    FileName = UnitMappingsTextBox.Text,
                    Title = "Seleziona mapping unità di misura",
                    Filter = "File XML (*.xml)|*.xml|Tutti i file (*.*)|*.*",
                    CheckFileExists = true
                };

                if (dlg.ShowDialog() == true)
                {
                    UnitMappingsTextBox.Text = dlg.FileName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Impossibile selezionare il file:{Environment.NewLine}{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseImportBanListButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    FileName = ImportBanListTextBox.Text,
                    Title = "Seleziona banlist di importazione",
                    Filter = "File XML (*.xml)|*.xml|Tutti i file (*.*)|*.*",
                    CheckFileExists = true
                };

                if (dlg.ShowDialog() == true)
                {
                    ImportBanListTextBox.Text = dlg.FileName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Impossibile selezionare il file:{Environment.NewLine}{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var connectionString = ConnectionStringTextBox.Text;
                
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    MessageBox.Show(
                        this,
                        "Inserire una connection string prima di testare la connessione.",
                        "Connection string vuota",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Disabilita il pulsante durante il test
                TestConnectionButton.IsEnabled = false;
                TestConnectionButton.Content = "Test in corso...";

                try
                {
                    var (success, message) = await DatabaseConnectionTester.TestConnectionAsync(connectionString);

                    if (success)
                    {
                        MessageBox.Show(
                            this,
                            message,
                            "Test connessione",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show(
                            this,
                            message,
                            "Errore connessione",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
                finally
                {
                    TestConnectionButton.IsEnabled = true;
                    TestConnectionButton.Content = "Testa connessione";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Errore durante il test della connessione:{Environment.NewLine}{ex.Message}",
                    "Errore",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                TestConnectionButton.IsEnabled = true;
                TestConnectionButton.Content = "Testa connessione";
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

            if (!string.IsNullOrWhiteSpace(UnitMappingsTextBox.Text))
            {
                try
                {
                    UnitMappingResolver.Load(ConfiguredPathResolver.ResolveFile(UnitMappingsTextBox.Text));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        $"Il file dei mapping delle unità di misura non è valido:{Environment.NewLine}{ex.Message}",
                        "Mapping unità non valido",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    UnitMappingsTextBox.Focus();
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(ImportBanListTextBox.Text))
            {
                try
                {
                    ImportBanListResolver.Load(ConfiguredPathResolver.ResolveFile(ImportBanListTextBox.Text));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        $"Il file della banlist di importazione non è valido:{Environment.NewLine}{ex.Message}",
                        "Banlist non valida",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    ImportBanListTextBox.Focus();
                    return;
                }
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
            Properties.Settings.Default.UnitMappings = string.IsNullOrWhiteSpace(UnitMappingsTextBox.Text) ? null : UnitMappingsTextBox.Text;
            Properties.Settings.Default.ImportBanList = string.IsNullOrWhiteSpace(ImportBanListTextBox.Text) ? null : ImportBanListTextBox.Text;
            Properties.Settings.Default.ConnectionString = string.IsNullOrWhiteSpace(ConnectionStringTextBox.Text) ? null : ConnectionStringTextBox.Text;
            Properties.Settings.Default.RecurseDepth = depth;
            Properties.Settings.Default.Theme = ThemeManager.Normalize(
                (ThemeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString());
            Properties.Settings.Default.Save();
            ThemeManager.Apply(Properties.Settings.Default.Theme);

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
