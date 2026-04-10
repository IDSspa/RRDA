using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using RRDA.Core;
using RRDA.Core.Validator;
using RRDA.Data;
using System.Data;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace RRDA.RepImp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string _reportsRoot;
        private readonly PluginLoader _pluginLoader = new();
        private List<IReportImporter> _plugins = [];

        public MainWindow()
        {
            InitializeComponent();

            // Usa l'impostazione __Properties.Settings.Default.ReportsFolder__ se valorizzata, altrimenti fallback
            var configured = Properties.Settings.Default.ReportsFolder;
            if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
                _reportsRoot = configured;
            else
                _reportsRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                LoadFolders();
                LoadPluginsFromSettings();
            }
            catch (Exception ex)
            {
                Log($"Errore inizializzazione: {ex.Message}");
            }
        }

        private void LoadFolders()
        {
            List<DirectoryInfo> folders = [];

            if (Directory.Exists(_reportsRoot))
            {
                var dirInfo = new DirectoryInfo(_reportsRoot);
                folders = [.. dirInfo.GetDirectories().OrderBy(d => d.Name)];
                Log($"Caricate {folders.Count} cartelle da '{_reportsRoot}'.");
            }
            else
            {
                // Se non esiste la cartella Reports mostriamo le unità logiche come fallback
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => new DirectoryInfo(d.RootDirectory.FullName))
                    .OrderBy(d => d.Name)
                    .ToList();

                folders = drives;
                Log($"Cartella '{_reportsRoot}' non trovata. Visualizzate {folders.Count} unità log come fallback.");
            }

            FoldersListBox.ItemsSource = folders;
            if (folders.Count > 0)
                FoldersListBox.SelectedIndex = 0;
        }

        private void LoadPluginsFromSettings()
        {
            try
            {
                // Impostazione specificata dall'utente
                var pluginsFolder = Properties.Settings.Default.PluginsFolder;
                if (string.IsNullOrWhiteSpace(pluginsFolder))
                {
                    // fallback: cartella "plugins" accanto all'eseguibile
                    pluginsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
                    Log("Impostazione 'PluginsFolder' vuota; uso cartella di default 'plugins'.");
                }

                if (!Directory.Exists(pluginsFolder))
                {
                    Log($"Cartella plugins non trovata: {pluginsFolder}");
                    PluginsListBox.ItemsSource = null;
                    return;
                }

                var loaded = _pluginLoader.LoadPlugins(pluginsFolder)?.ToList() ?? [];
                _plugins = loaded;
                PluginsListBox.ItemsSource = _plugins;
                Log($"Caricati {loaded.Count} plugin da '{pluginsFolder}'.");
            }
            catch (Exception ex)
            {
                _plugins = [];
                PluginsListBox.ItemsSource = null;
                Log($"Errore caricamento plugin: {ex.Message}");
            }
        }

        private async void FoldersListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FoldersListBox.SelectedItem is DirectoryInfo di)
            {
                await LoadFiles(di.FullName);
                Log($"Selezionata cartella: {di.FullName}");
            }
            else
            {
                FilesListView.ItemsSource = null;
            }
        }

        // DTO per la ListView dei file (include il tipo determinato dal plugin)
        private sealed record FileItem(string Name, long Length, DateTime LastWriteTime, string Tipo, string FullPath);

        private async Task LoadFiles(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    FilesListView.ItemsSource = null;
                    Log($"Cartella non trovata: {folderPath}");
                    return;
                }

                var fileInfos = new DirectoryInfo(folderPath)
                    .GetFiles("*.xlsx") // considera solo .xlsx
                    .OrderBy(f => f.Name)
                    .ToList();

                var fileItems = new List<FileItem>(fileInfos.Count);

                foreach (var fi in fileInfos)
                {
                    string tipo = string.Empty;

                    // Verifica applicabilità con i plugin caricati
                    foreach (var plugin in _plugins)
                    {
                        try
                        {
                            var can = await plugin.CanImportAsync(fi.Name);
                            if (can)
                            {
                                tipo = plugin.Name;
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            // Non fermare l'elaborazione dei file per un plugin malfunzionante
                            Log($"Errore CanImportAsync plugin '{plugin.Name}' per file '{fi.Name}': {ex.Message}");
                        }
                    }

                    fileItems.Add(new FileItem(fi.Name, fi.Length, fi.LastWriteTime, tipo, fi.FullName));
                }

                FilesListView.ItemsSource = fileItems;
                Log($"Caricati {fileItems.Count} file *.xlsx da '{folderPath}'. Plugin applicabili trovati per {fileItems.Count(fi => !string.IsNullOrEmpty(fi.Tipo))} file.");
            }
            catch (Exception ex)
            {
                FilesListView.ItemsSource = null;
                Log($"Errore caricamento file da '{folderPath}': {ex.Message}");
            }
        }

        private void FilesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FilesListView.SelectedItem is FileItem fi)
            {
                Log($"File selezionato: {fi.Name} ({fi.Length} byte) - Tipo: {fi.Tipo}");
            }
            else if (FilesListView.SelectedItem is FileInfo ffi)
            {
                Log($"File selezionato: {ffi.Name} ({ffi.Length} byte)");
            }
        }

        private void PluginsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PluginsListBox.SelectedItem is IReportImporter plugin)
            {
                Log($"Plugin selezionato: {plugin.Name} (v{plugin.Version}) - Estensione supportata: {plugin.SupportedFileExtension}");
            }
        }

        private void Log(string message)
        {
            var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var line = $"[{ts}] {message}{Environment.NewLine}";

            // Garantisce aggiornamento thread-safe dell'UI
            Dispatcher.Invoke(() =>
            {
                LogTextBox.AppendText(line);
                LogTextBox.ScrollToEnd();
            }, DispatcherPriority.Background);
        }

        private void ClearLog_Click(object? sender, RoutedEventArgs e)
        {
            // Cancella i messaggi di log
            LogTextBox.Clear();
        }

        private void SaveLog_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new SaveFileDialog
                {
                    Title = "Salva log",
                    Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    DefaultExt = ".log",
                    FileName = $"RRDA_log_{DateTime.Now:yyyyMMdd_HHmmss}.log",
                    OverwritePrompt = true
                };

                var result = dlg.ShowDialog();
                if (result == true && !string.IsNullOrWhiteSpace(dlg.FileName))
                {
                    // Salviamo lo stato corrente del log (per non includere eventuali messaggi successivi)
                    var content = LogTextBox.Text ?? string.Empty;
                    File.WriteAllText(dlg.FileName, content, Encoding.UTF8);
                    Log($"Log salvato in '{dlg.FileName}'");
                }
            }
            catch (Exception ex)
            {
                Log($"Errore salvataggio log: {ex.Message}");
                MessageBox.Show(this, $"Impossibile salvare il file di log:{Environment.NewLine}{ex.Message}", "Errore salvataggio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SelectRootFolder_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFolderDialog
                {
                    FolderName = _reportsRoot,
                    Title = "Seleziona cartella radice dei report"
                };


                var res = dlg.ShowDialog();
                
                if (res == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
                {
                    // Aggiorna la impostazione __Properties.Settings.Default.ReportsFolder__ e la salva
                    Properties.Settings.Default.ReportsFolder = dlg.FolderName;
                    Properties.Settings.Default.Save();

                    // Aggiorna la variabile locale e ricarica l'elenco cartelle
                    _reportsRoot = dlg.FolderName;
                    LoadFolders();

                    Log($"Cartella radice impostata su '{_reportsRoot}' e salvata nelle impostazioni.");
                }
            }
            catch (Exception ex)
            {
                Log($"Errore selezione cartella radice: {ex.Message}");
                MessageBox.Show(this, $"Impossibile selezionare la cartella radice:{Environment.NewLine}{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void File_Delete_Click(object? sender, RoutedEventArgs e)
        {
            if (FilesListView.SelectedItem is FileItem fi)
            {
                var res = MessageBox.Show(this,
                    $"Confermi cancellazione del file '{fi.Name}'?",
                    "Conferma cancellazione",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res == MessageBoxResult.Yes)
                {
                    try
                    {
                        if (File.Exists(fi.FullPath))
                        {
                            File.Delete(fi.FullPath);
                            Log($"File cancellato: {fi.FullPath}");
                        }
                        else
                        {
                            Log($"File non trovato: {fi.FullPath}");
                        }

                        var folder = Path.GetDirectoryName(fi.FullPath) ?? _reportsRoot;
                        await LoadFiles(folder);
                    }
                    catch (Exception ex)
                    {
                        Log($"Errore cancellazione file '{fi.FullPath}': {ex.Message}");
                        MessageBox.Show(this, $"Impossibile cancellare il file:{Environment.NewLine}{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                MessageBox.Show(this, "Seleziona un file da cancellare.", "Nessun file selezionato", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void File_Import_Click(object? sender, RoutedEventArgs e)
        {
            if (FilesListView.SelectedItem is not FileItem fi)
            {
                MessageBox.Show(this, "Seleziona un file da importare.", "Nessun file selezionato", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(fi.Tipo))
            {
                MessageBox.Show(this, "Nessun plugin associato a questo file.", "Import non disponibile", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var plugin = _plugins.FirstOrDefault(p => string.Equals(p.Name, fi.Tipo, StringComparison.OrdinalIgnoreCase));
            if (plugin == null)
            {
                Log($"Nessun plugin caricato con nome '{fi.Tipo}' per importare il file '{fi.Name}'.");
                MessageBox.Show(this, $"Plugin '{fi.Tipo}' non trovato fra i plugin caricati.", "Plugin mancante", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Log($"Avvio import per '{fi.Name}' usando plugin '{plugin.Name}'...");

            // Prepariamo gli stream: file di input + possibile validation config (se presente)
            FileStream? fileStream = null;
            FileStream? validationStream = null;

            try
            {
                fileStream = File.OpenRead(fi.FullPath);

                // Tentiamo di trovare un file di configurazione XML per il plugin nella cartella dei plugin
                var pluginsFolder = Properties.Settings.Default.PluginsFolder;
                
                if (string.IsNullOrWhiteSpace(pluginsFolder))
                    pluginsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");

                string? possibleConfigPath = null;
                
                if (!string.IsNullOrWhiteSpace(pluginsFolder) && Directory.Exists(pluginsFolder))
                {
                    // nomi possibili: {pluginName}.xml oppure {pluginName}.config.xml
                    var p1 = Path.Combine(pluginsFolder, plugin.Name + ".xml");
                    var p2 = Path.Combine(pluginsFolder, plugin.Name + ".config.xml");

                    if (File.Exists(p1)) possibleConfigPath = p1;
                    else if (File.Exists(p2)) possibleConfigPath = p2;
                }

                if (!string.IsNullOrWhiteSpace(possibleConfigPath))
                {
                    try
                    {
                        validationStream = File.OpenRead(possibleConfigPath);
                        Log($"Usata configurazione di validazione: '{possibleConfigPath}'.");
                    }
                    catch (Exception ex)
                    {
                        Log($"Impossibile aprire configurazione di validazione '{possibleConfigPath}': {ex.Message}. Verrà passato uno stream vuoto.");
                        validationStream?.Dispose();
                        validationStream = Stream.Null as FileStream;
                    }
                }
                else
                {
                    // Nessuna configurazione trovata: passiamo Stream.Null
                    validationStream = Stream.Null as FileStream;
                    Log("Nessuna configurazione di validazione trovata per il plugin; passato Stream.Null.");
                }

                // Chiamata a ImportAsync del plugin
                object? resultObj = null;

                try
                {
                    // ImportAsync restituisce ImportResult; usiamo reflection-safe nel logging dopo l'await
                    var task = plugin.ImportAsync(fileStream, validationStream ?? Stream.Null);
                    resultObj = await task;
                }
                catch (Exception ex)
                {
                    Log($"Eccezione durante ImportAsync per '{fi.Name}' con plugin '{plugin.Name}': {ex.Message}");
                    if (ex.InnerException != null)
                        Log($"InnerException: {ex.InnerException.Message}");
                    return;
                }

                if (resultObj == null)
                {
                    Log($"ImportAsync ha restituito null per file '{fi.Name}'.");
                    return;
                }

                // Logging robusto usando reflection per leggere campi comuni (Success, Errors, Entities, ReportTypeKey)
                var rType = resultObj.GetType();

                var successProp = rType.GetProperty("Success");
                if (successProp != null)
                {
                    var val = successProp.GetValue(resultObj);
                    Log($"Import result - Success: {val}");
                }

                var reportTypeProp = rType.GetProperty("ReportTypeKey") ?? rType.GetProperty("ReportType");
                if (reportTypeProp != null)
                {
                    var val = reportTypeProp.GetValue(resultObj);
                    Log($"Import result - ReportType: {val}");
                }

                var errorsProp = rType.GetProperty("Errors");
                if (errorsProp != null)
                {
                    if (errorsProp.GetValue(resultObj) is System.Collections.IEnumerable errsObj)
                    {
                        var errs = errsObj.Cast<object>().Select(x => x?.ToString() ?? string.Empty).ToList();
                        Log($"Import result - Errors ({errs.Count}):{(errs.Count > 0 ? " " + string.Join(" | ", errs.Take(10)) : " nessuno")}");
                    }
                }

                var entitiesProp = rType.GetProperty("Entities");
                if (entitiesProp != null)
                {
                    if (entitiesProp.GetValue(resultObj) is System.Collections.IEnumerable entsObj)
                    {
                        int count = 0;
                        foreach (var _ in entsObj) count++;
                        Log($"Import result - Entities: {count}");
                    }
                }

                Log($"Importazione completata per '{fi.Name}'.");

                // ==========================
                // Persistenza nel database (EF usando RRDADbContext)
                // ==========================
                try
                {
                    // Cast sicuro: IReportImporter deve restituire ImportResult per contratto
                    if (resultObj is ImportResult importResult && importResult.Entities != null && importResult.Entities.Any())
                    {
                        // Creazione del DbContext: preferisci la connection string dalle impostazioni; altrimenti fallback alla RRDAContextFactory
                        RRDADbContext? db = null;
                        var connStr = Properties.Settings.Default.ConnectionString;

                        try
                        {
                            if (!string.IsNullOrWhiteSpace(connStr))
                            {
                                var optionsBuilder = new DbContextOptionsBuilder<RRDADbContext>();
                                optionsBuilder.UseSqlServer(connStr);
                                db = new RRDADbContext(optionsBuilder.Options);
                            }
                            else
                            {
                                // fallback alla factory design-time che contiene una connection string di default
                                db = new RRDAContextFactory().CreateDbContext(Array.Empty<string>());
                                Log("ConnectionString non impostata; usata la connection string della RRDAContextFactory come fallback.");
                            }

                            await using (db)
                            {
                                var (reportFileId, entitiesSaved, propertiesSaved) =
                                    await ImportResultRepository.SaveAsync(fi.Name, fi.FullPath, importResult, db, Log);

                                Log($"Persistenza completata: ReportFileId={reportFileId}, Entities={entitiesSaved}, Properties={propertiesSaved}.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Errore persistenza DB: {ex.Message}");
                        }
                    }
                    else
                    {
                        Log("Nessuna entity da salvare nel database.");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Errore persistenza DB: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Log($"Errore durante importazione file '{fi.Name}': {ex.Message}");
                MessageBox.Show(this, $"Errore durante importazione:{Environment.NewLine}{ex.Message}", "Errore import", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                try { validationStream?.Dispose(); } catch { }
                try { fileStream?.Dispose(); } catch { }
            }
        }

        private void File_ExportValidator_Click(object? sender, RoutedEventArgs e)
        {
            if (FilesListView.SelectedItem is not FileItem fi)
            {
                MessageBox.Show(this, "Seleziona un file per esportare il validatore.", "Nessun file selezionato", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(fi.Tipo))
            {
                MessageBox.Show(this, "Nessun plugin associato al file selezionato.", "Esporta validatore non disponibile", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var plugin = _plugins.FirstOrDefault(p => string.Equals(p.Name, fi.Tipo, StringComparison.OrdinalIgnoreCase));
            if (plugin == null)
            {
                Log($"Plugin '{fi.Tipo}' non trovato fra i plugin caricati.");
                MessageBox.Show(this, $"Plugin '{fi.Tipo}' non caricato.", "Plugin mancante", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Determina la cartella del plugin (dove salvare l'xml). Se non disponibile, fallback a impostazione PluginsFolder.
            string? pluginFolder = null;
            try
            {
                var asmLocation = plugin.GetType().Assembly.Location;
                if (!string.IsNullOrWhiteSpace(asmLocation))
                {
                    pluginFolder = Path.GetDirectoryName(asmLocation);
                }
            }
            catch
            {
                pluginFolder = null;
            }

            if (string.IsNullOrWhiteSpace(pluginFolder))
            {
                pluginFolder = Properties.Settings.Default.PluginsFolder;
                if (string.IsNullOrWhiteSpace(pluginFolder))
                    pluginFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
            }

            if (!Directory.Exists(pluginFolder))
            {
                Log($"Cartella plugin inesistente: {pluginFolder}");
                MessageBox.Show(this, $"Cartella plugin non trovata: {pluginFolder}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var outputFile = Path.Combine(pluginFolder, $"{plugin.Name}.xml");

            try
            {
                ValidationFileCreator.CreateFromFile(fi.FullPath, outputFile);
                Log($"File di validazione creato in '{outputFile}' per report '{fi.Name}' utilizzando plugin '{plugin.Name}'.");
                MessageBox.Show(this, $"File di validazione creato:{Environment.NewLine}{outputFile}", "Esporta validatore", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"Errore creazione file di validazione per '{fi.Name}': {ex.Message}");
                MessageBox.Show(this, $"Errore durante la creazione del file di validazione:{Environment.NewLine}{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Aggiorna lo stato dei comandi del menu contestuale prima che si apra
        private void FilesListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            try
            {
                if (FilesListView.ContextMenu == null)
                    return;

                var importMenu = FilesListView.ContextMenu.Items
                    .OfType<MenuItem>()
                    .FirstOrDefault(mi => mi.Name == "FileImportMenuItem");

                var exportMenu = FilesListView.ContextMenu.Items
                    .OfType<MenuItem>()
                    .FirstOrDefault(mi => mi.Name == "FileExportValidatorMenuItem");

                if (importMenu != null)
                {
                    if (FilesListView.SelectedItem is FileItem fi && !string.IsNullOrWhiteSpace(fi.Tipo))
                        importMenu.IsEnabled = true;
                    else
                        importMenu.IsEnabled = false;
                }

                if (exportMenu != null)
                {
                    bool canExport = false;
                    if (FilesListView.SelectedItem is FileItem fi2 && !string.IsNullOrWhiteSpace(fi2.Tipo))
                    {
                        var plugin = _plugins.FirstOrDefault(p => string.Equals(p.Name, fi2.Tipo, StringComparison.OrdinalIgnoreCase));
                        if (plugin != null)
                        {
                            // verify plugin folder exists or fallback
                            string? pluginFolder = null;
                            try { pluginFolder = Path.GetDirectoryName(plugin.GetType().Assembly.Location); } catch { pluginFolder = null; }
                            if (string.IsNullOrWhiteSpace(pluginFolder))
                                pluginFolder = Properties.Settings.Default.PluginsFolder;
                            if (string.IsNullOrWhiteSpace(pluginFolder))
                                pluginFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");

                            canExport = Directory.Exists(pluginFolder);
                        }
                    }

                    exportMenu.IsEnabled = canExport;
                }
            }
            catch (Exception ex)
            {
                Log($"Errore valutazione menu contestuale file: {ex.Message}");
            }
        }

    }
}