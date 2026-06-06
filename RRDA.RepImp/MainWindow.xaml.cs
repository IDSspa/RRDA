using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using RRDA.Core;
using RRDA.Core.Validator;
using RRDA.Data;
using RRDA.Plugins.Common;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using static RRDA.Data.ImportResultRepository;

namespace RRDA.RepImp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string? _reportsRoot;
        private List<IReportImporter> _plugins = [];
        private SplashScreenWindow? _aboutWindow;
        private GridViewColumnHeader? _lastHeaderClicked;
        private ListSortDirection _lastDirection;
        private bool _applyForAll = false;
        private DuplicateImportStrategy _duplicateStrategy = DuplicateImportStrategy.NewVersion;
        private readonly ObservableCollection<FileItem> _fileItems = [];
        private readonly IReportTypeCompatibilityChecker _reportTypeCompatibilityChecker;
        private readonly IPluginService _pluginService;
        private readonly IAuditService _auditService;

        public MainWindow(
            IReportTypeCompatibilityChecker reportTypeCompatibilityChecker,
            IPluginService pluginService,
            IAuditService auditService)
        {
            _reportTypeCompatibilityChecker = reportTypeCompatibilityChecker
                ?? throw new ArgumentNullException(nameof(reportTypeCompatibilityChecker));
            _pluginService = pluginService
                ?? throw new ArgumentNullException(nameof(pluginService));
            _auditService = auditService
                ?? throw new ArgumentNullException(nameof(auditService));
            InitializeComponent();

            FilesListView.ItemsSource = _fileItems;

            ApplySettings();

            Loaded += MainWindow_Loaded;
        }

        private void ApplySettings()
        {
            // Usa l'impostazione __Properties.Settings.Default.ReportsFolder__ se valorizzata, altrimenti fallback
            var configured = Properties.Settings.Default.ReportsFolder;
            if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
                _reportsRoot = configured;
            else
                _reportsRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
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
                Log($"Cartella '{_reportsRoot}' non trovata. Visualizzate {folders.Count} unità logiche come fallback.");
            }

            FoldersListBox.ItemsSource = folders;
            if (folders.Count > 0)
                FoldersListBox.SelectedIndex = 0;
        }

        private readonly IFileScanService _fileScanService = new FileScanService();

        private async Task LoadFiles(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                FilesListView.ItemsSource = null;
                Log($"Cartella non trovata: {folderPath}");
                return;
            }

            var maxDepth = Properties.Settings.Default.RecurseDepth;
            if (maxDepth < 0)
                maxDepth = 0;

            using var cts = new CancellationTokenSource();
            var scanDlg = new ScanProgressDialog(cts) { Owner = this };
            scanDlg.Show();

            try
            {
                var progress = new Progress<FileScanProgress>(p =>
                {
                    switch (p.Phase)
                    {
                        case FileScanPhase.ScanningDirectories:
                            scanDlg.SetPhase("Scansione cartelle in corso...", indeterminate: true);
                            if (!string.IsNullOrWhiteSpace(p.CurrentPath))
                                scanDlg.SetDetail(p.CurrentPath);
                            break;

                        case FileScanPhase.ClassifyingFiles:
                            scanDlg.SetPhase(
                                "Catalogazione file in corso...",
                                indeterminate: false,
                                max: p.TotalItems.GetValueOrDefault(1));

                            if (p.ProcessedItems.HasValue)
                            {
                                scanDlg.SetProgress(
                                    p.ProcessedItems.Value,
                                    p.Message ?? string.Empty);
                            }

                            break;
                    }
                });

                var scannedFiles = await _fileScanService.ScanAsync(
                    new FileScanRequest(folderPath, "*.xlsx", maxDepth),
                    _plugins,
                    progress,
                    Log,
                    cts.Token);

                var fileItems = scannedFiles
                    .Select(f => new FileItem(
                        f.Name,
                        f.Length,
                        f.LastWriteTime,
                        f.ReportType ?? string.Empty,
                        f.FullPath))
                    .ToList();

                FilesListView.ItemsSource = fileItems;

                Log($"Caricati {fileItems.Count} file *.xlsx da '{folderPath}' " +
                    $"(profondità ricorsione={maxDepth}). " +
                    $"Plugin applicabili trovati per {fileItems.Count(f => !string.IsNullOrEmpty(f.Tipo))} file.");
            }
            catch (OperationCanceledException)
            {
                Log("Scansione annullata dall'utente.");
                FilesListView.ItemsSource = null;
            }
            catch (Exception ex)
            {
                FilesListView.ItemsSource = null;
                Log($"Errore caricamento file da '{folderPath}': {ex.Message}");
            }
            finally
            {
                scanDlg.Close();
            }
        }

        private void LoadPlugins()
        {
            try
            {
                var pluginsFolder = ResolvePluginsFolder();
                var result = _pluginService.LoadPlugins(pluginsFolder);
                _plugins = [.. result.Plugins];
                PluginsListBox.ItemsSource = _plugins;

                foreach (var error in result.Errors)
                    Log($"Plugin non caricato da '{error.Source}': {error.Message}");

                Log($"Caricati {_plugins.Count} plugin da '{pluginsFolder}'.");

                // Verifica asincrona non bloccante: RRDA.Web e l'unica autorita
                // autorizzata a sincronizzare la tabella ReportTypes.
                _ = CheckReportTypesCompatibilityAsync();
            }
            catch (Exception ex)
            {
                _plugins = [];
                PluginsListBox.ItemsSource = null;
                Log($"Errore caricamento plugin: {ex.Message}");
            }
        }

        private string ResolvePluginsFolder()
        {
            return _pluginService.ResolvePluginsFolder(
                Properties.Settings.Default.PluginsFolder,
                AppDomain.CurrentDomain.BaseDirectory);
        }

        private async Task CheckReportTypesCompatibilityAsync()
        {
            if (_plugins.Count == 0)
                return;

            try
            {
                var connStr = Properties.Settings.Default.ConnectionString;
                RRDADbContext db;

                if (!string.IsNullOrWhiteSpace(connStr))
                {
                    var opt = new DbContextOptionsBuilder<RRDADbContext>();
                    opt.UseSqlServer(connStr);
                    db = new RRDADbContext(opt.Options);
                }
                else
                {
                    db = new RRDAContextFactory().CreateDbContext([]);
                }

                await using (db)
                {
                    var result = await _reportTypeCompatibilityChecker.CheckAsync(db, _plugins);

                    if (result.IsCompatible)
                    {
                        Log("ReportTypes: plugin locali compatibili con il catalogo gestito da RRDA.Web.");
                    }
                    else
                    {
                        if (result.MissingReportTypes.Count > 0)
                        {
                            Log(
                                $"Avviso ReportTypes: {result.MissingReportTypes.Count} plugin locali non sono registrati nel catalogo gestito da RRDA.Web: " +
                                $"{string.Join(", ", result.MissingReportTypes)}.");
                        }

                        foreach (var mismatch in result.SubjectKindMismatches)
                        {
                            Log(
                                $"Avviso ReportTypes: SubjectKind non coerente per '{mismatch.ReportTypeKey}'. " +
                                $"Database={mismatch.DatabaseSubjectKind}, plugin locale={mismatch.PluginSubjectKind}.");
                        }

                        await WriteAuditAsync(
                            "Plugins.CompatibilityMismatch",
                            "Warning",
                            entityType: "ReportType",
                            description: "I plugin locali non sono compatibili con il catalogo ReportTypes gestito da RRDA.Web.",
                            details: new
                            {
                                result.MissingReportTypes,
                                result.SubjectKindMismatches
                            });
                    }
                }
            }
            catch (Exception ex)
            {
                // Non bloccante: se il DB non è raggiungibile, l'import può comunque proseguire
                Log($"Avviso: impossibile verificare la compatibilita dei plugin con ReportTypes: {ex.Message}");
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

        private async Task WriteAuditAsync(
            string operation,
            string result,
            string? entityType = null,
            string? entityId = null,
            string? description = null,
            object? details = null)
        {
            try
            {
                var connStr = Properties.Settings.Default.ConnectionString;
                RRDADbContext db;

                if (!string.IsNullOrWhiteSpace(connStr))
                {
                    var optionsBuilder = new DbContextOptionsBuilder<RRDADbContext>();
                    optionsBuilder.UseSqlServer(connStr);
                    db = new RRDADbContext(optionsBuilder.Options);
                }
                else
                {
                    db = new RRDAContextFactory().CreateDbContext([]);
                }

                await using (db)
                {
                    var userName = System.Security.Principal.WindowsIdentity.GetCurrent()?.Name
                        ?? Environment.UserName;

                    await _auditService.WriteAsync(
                        db,
                        new AuditEventRequest(
                            "RRDA.RepImp",
                            operation,
                            result,
                            UserName: userName,
                            EntityType: entityType,
                            EntityId: entityId,
                            Description: description,
                            Details: details));
                }
            }
            catch (Exception ex)
            {
                Log($"Avviso: impossibile scrivere l'audit '{operation}': {ex.Message}");
            }
        }

        private async Task<bool> ImportReport(FileItem fi,
                                              string? user = null,
                                              ImportProgressDialog? progressDlg = null,
                                              int? batchId = null,
                                              CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(fi.Tipo))
            {
                MessageBox.Show(this, "Nessun plugin associato a questo file.", "Import non disponibile", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var plugin = _plugins.FirstOrDefault(p => string.Equals(p.Name, fi.Tipo, StringComparison.OrdinalIgnoreCase));
            if (plugin == null)
            {
                Log($"Nessun plugin caricato con nome '{fi.Tipo}' per importare il file '{fi.Name}'.");
                MessageBox.Show(this, $"Plugin '{fi.Tipo}' non trovato fra i plugin caricati.", "Plugin mancante", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            Log($"Avvio import per '{fi.Name}' usando plugin '{plugin.Name}'...");

            // Prepariamo gli stream: file di input + possibile validation config (se presente)
            FileStream? fileStream = null;
            FileStream? validationStream = null;

            try
            {
                fileStream = File.OpenRead(fi.FullPath);

                // Tentiamo di trovare un file di configurazione XML per il plugin nella cartella dei plugin
                var pluginsFolder = ResolvePluginsFolder();

                string? possibleConfigPath = null;

                if (!string.IsNullOrWhiteSpace(pluginsFolder) && Directory.Exists(pluginsFolder))
                {
                    var validatorFilePath = Path.Combine(pluginsFolder, plugin.Name + ".xml");

                    if (File.Exists(validatorFilePath)) possibleConfigPath = validatorFilePath;
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
                    IProgress<ImportProgress>? innerProgress = progressDlg is not null
                                                                ? new Progress<ImportProgress>(p =>
                                                                    // Progress<T> fa già il marshal sul thread UI del costruttore
                                                                    progressDlg.Dispatcher.Invoke(() =>
                                                                        progressDlg.SetRowProgress(p.ProcessedItems, p.TotalItems, p.Message)))
                                                                : null;


                    // Carica e valida la configurazione XML
                    var config = ValidationConfig.Load(validationStream ?? Stream.Null);

                    /*
                     * MainWindow.ImportReport() gestisce già il caso in cui il file XML non esista 
                     * passando Stream.Null. Con la nuova firma occorre decidere il comportamento: 
                     * se SubjectKeyField è obbligatorio nello XSD, un file XML mancante diventa un 
                     * errore bloccante. 
                     * Loggare un warning e restituire un ImportResult con Success = false prima 
                     * ancora di chiamare ImportAsync, rendendo esplicito che un import senza 
                     * configurazione valida non è ammesso?
                     */

                    // ImportAsync restituisce ImportResult; usiamo reflection-safe nel logging dopo l'await
                    var task = plugin.ImportAsync(fileStream, config, innerProgress, ct);
                    resultObj = await task;
                }
                catch (Exception ex)
                {
                    Log($"Eccezione durante ImportAsync per '{fi.Name}' con plugin '{plugin.Name}': {ex.Message}");
                    if (ex.InnerException != null)
                        Log($"InnerException: {ex.InnerException.Message}");
                    await WriteAuditAsync(
                        "Report.ImportFailed",
                        "Failure",
                        "ReportFile",
                        fi.Name,
                        ex.Message,
                        new { FilePath = fi.FullPath, Plugin = plugin.Name });
                    return false;
                }

                if (resultObj == null)
                {
                    Log($"ImportAsync ha restituito null per file '{fi.Name}'.");
                    await WriteAuditAsync(
                        "Report.ImportFailed",
                        "Failure",
                        "ReportFile",
                        fi.Name,
                        "Il plugin ha restituito un risultato nullo.",
                        new { FilePath = fi.FullPath, Plugin = plugin.Name });
                    return false;
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

                if (resultObj is ImportResult checkedImportResult && !checkedImportResult.Success)
                {
                    var firstError = checkedImportResult.Errors.FirstOrDefault();
                    Log($"Import fallito per '{fi.Name}'{(string.IsNullOrWhiteSpace(firstError) ? "." : $": {firstError}")}");
                    await WriteAuditAsync(
                        "Report.ImportFailed",
                        "Failure",
                        "ReportFile",
                        fi.Name,
                        firstError ?? "Il plugin ha restituito un esito negativo.",
                        new
                        {
                            FilePath = fi.FullPath,
                            Plugin = plugin.Name,
                            checkedImportResult.ReportTypeKey,
                            checkedImportResult.Errors
                        });
                    return false;
                }
                // ====================================================
                // Persistenza nel database (EF usando RRDADbContext)
                // ====================================================
                try
                {
                    if (resultObj is ImportResult importResult
                        && importResult.Entities != null
                        && importResult.Entities.Any())
                    {
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
                                db = new RRDAContextFactory().CreateDbContext([]);
                                Log("ConnectionString non impostata; usata la connection string della RRDAContextFactory come fallback.");
                            }

                            await using (db)
                            {
                                // -------------------------------------------------------
                                // Controllo duplicato: verifica se il file è già in DB
                                // prima di aprire il dialog, per non disturbarlo se il
                                // file è nuovo.
                                // -------------------------------------------------------

                                int existing = await ImportResultRepository.CountExistingAsync(
                                    fi.Name, importResult.ReportTypeKey, db);

                                if (existing > 0 && !_applyForAll)
                                {
                                    // Apriamo il dialog sul thread UI (siamo già su di esso
                                    // perché ImportReport è chiamato da un async void handler).
                                    var dupDlg = new DuplicateImportDialog(fi.Name, existing)
                                    {
                                        Owner = this
                                    };

                                    var dlgResult = dupDlg.ShowDialog();

                                    if (dlgResult != true || !dupDlg.Confirmed)
                                    {
                                        // L'utente ha chiuso o annullato il dialog:
                                        // saltiamo la persistenza per questo file.
                                        Log($"Persistenza annullata dall'utente per '{fi.Name}'.");
                                        await WriteAuditAsync(
                                            "Report.ImportCancelled",
                                            "Cancelled",
                                            "ReportFile",
                                            fi.Name,
                                            "Persistenza annullata dall'utente dopo il rilevamento di un duplicato.",
                                            new { FilePath = fi.FullPath, Plugin = plugin.Name, ExistingReports = existing });
                                        return true; // l'import è riuscito, solo la save è stata saltata
                                    }

                                    _applyForAll = dupDlg.ApplyForAll;
                                    _duplicateStrategy = dupDlg.SelectedStrategy;

                                    Log($"Strategia duplicato scelta per '{fi.Name}': {_duplicateStrategy}.");

                                    if (_applyForAll)
                                        Log($"La strategia scelta sarà applicata a tutti i file duplicati in questo ciclo di import.");
                                }

                                // -------------------------------------------------------
                                // Persistenza con la strategia selezionata
                                // -------------------------------------------------------
                                try
                                {
                                    var (reportFileId, entitiesSaved, propertiesSaved) =
                                        await ImportResultRepository.SaveAsync(fi,
                                                                               importResult,
                                                                               db,
                                                                               Log,
                                                                               user, batchId,
                                                                               _duplicateStrategy);

                                    Log($"Persistenza completata: ReportFileId={reportFileId}, " +
                                        $"Entities={entitiesSaved}, Properties={propertiesSaved}" +
                                        (batchId.HasValue ? $", BatchId={batchId.Value}." : "."));

                                    await WriteAuditAsync(
                                        "Report.ImportSucceeded",
                                        "Success",
                                        "ReportFile",
                                        reportFileId.ToString(),
                                        $"Importato '{fi.Name}' usando il plugin '{plugin.Name}'.",
                                        new
                                        {
                                            FileName = fi.Name,
                                            FilePath = fi.FullPath,
                                            Plugin = plugin.Name,
                                            plugin.Version,
                                            importResult.ReportTypeKey,
                                            BatchId = batchId,
                                            DuplicateStrategy = _duplicateStrategy.ToString(),
                                            EntitiesSaved = entitiesSaved,
                                            PropertiesSaved = propertiesSaved
                                        });
                                }
                                catch (DuplicateImportException die)
                                {
                                    // Caso Block: non è un errore tecnico, è una scelta dell'utente.
                                    Log($"Import bloccato per '{fi.Name}': {die.Message}");
                                    await WriteAuditAsync(
                                        "Report.ImportBlocked",
                                        "Blocked",
                                        "ReportFile",
                                        fi.Name,
                                        die.Message,
                                        new { FilePath = fi.FullPath, Plugin = plugin.Name });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Errore persistenza DB: {ex.Message}");
                            if (ex.InnerException != null)
                                Log($"Inner exception: {ex.InnerException.Message}");
                            await WriteAuditAsync(
                                "Report.ImportFailed",
                                "Failure",
                                "ReportFile",
                                fi.Name,
                                ex.Message,
                                new { FilePath = fi.FullPath, Plugin = plugin.Name, Phase = "Persistence" });
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
                    await WriteAuditAsync(
                        "Report.ImportFailed",
                        "Failure",
                        "ReportFile",
                        fi.Name,
                        ex.Message,
                        new { FilePath = fi.FullPath, Plugin = plugin.Name, Phase = "Persistence" });
                }
            }
            catch (Exception ex)
            {
                Log($"Errore durante importazione file '{fi.Name}': {ex.Message}");
                await WriteAuditAsync(
                    "Report.ImportFailed",
                    "Failure",
                    "ReportFile",
                    fi.Name,
                    ex.Message,
                    new { FilePath = fi.FullPath, Plugin = plugin.Name });
                MessageBox.Show(this, $"Errore durante importazione:{Environment.NewLine}{ex.Message}", "Errore import", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                try { validationStream?.Dispose(); } catch { }
                try { fileStream?.Dispose(); } catch { }
            }

            return true;
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                LoadFolders();
                LoadPlugins();
            }
            catch (Exception ex)
            {
                Log($"Errore inizializzazione: {ex.Message}");
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

        private void FoldersListBox_DragOver(object? sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[]?)e.Data.GetData(DataFormats.FileDrop) ?? [];
                    if (files.Length > 0 && Directory.Exists(files[0]))
                        e.Effects = DragDropEffects.Copy;
                    else
                        e.Effects = DragDropEffects.None;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            catch (Exception ex)
            {
                Log($"Errore in PluginsListBox_DragEnter: {ex.Message}");
                e.Effects = DragDropEffects.None;
            }
            finally
            {
                e.Handled = true;
            }
        }

        private void FoldersListBox_Drop(object? sender, DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effects = DragDropEffects.None;
                    return;
                }

                var files = (string[]?)e.Data.GetData(DataFormats.FileDrop) ?? [];
                if (files.Length == 0)
                {
                    e.Effects = DragDropEffects.None;
                    return;
                }

                var first = files[0];
                if (!Directory.Exists(first))
                {
                    MessageBox.Show(this, "Per favore trascina una cartella valida contenente i plugin.", "Drop non valido", MessageBoxButton.OK, MessageBoxImage.Information);
                    e.Effects = DragDropEffects.None;
                    return;
                }

                // Salva la cartella come PluginsFolder nelle impostazioni e ricarica i plugin
                _reportsRoot = Properties.Settings.Default.ReportsFolder = first;
                //Properties.Settings.Default.Save();

                Log($"Cartella reports impostata su '{first}' tramite drag & drop.");

                LoadFolders();

                e.Effects = DragDropEffects.Copy;
            }
            catch (Exception ex)
            {
                Log($"Errore in PluginsListBox_Drop: {ex.Message}");
                MessageBox.Show(this, $"Errore durante l'operazione di drop:{Environment.NewLine}{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Effects = DragDropEffects.None;
            }
            finally
            {
                e.Handled = true;
            }
        }

        private void FilesListView_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not GridViewColumnHeader headerClicked || headerClicked.Column == null)
                return;

            if (headerClicked.Column != TypeColumn)
            {
                return;
            }

            ListSortDirection direction;

            // Gestione della direzione dell'ordinamento[cite: 2]
            if (_lastHeaderClicked != headerClicked)
            {
                direction = ListSortDirection.Ascending;
            }
            else
            {
                direction = _lastDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
            }

            ICollectionView dataView = CollectionViewSource.GetDefaultView(FilesListView.ItemsSource);
            if (dataView != null)
            {
                dataView.SortDescriptions.Clear();
                // Usiamo "Tipo" come proprietà di ordinamento (definita nel Binding della colonna)[cite: 3]
                dataView.SortDescriptions.Add(new SortDescription("Tipo", direction));
                dataView.Refresh();
            }

            // Imposta il Tag per attivare i DataTrigger del SortableHeaderTemplate
            headerClicked.Tag = direction.ToString();

            // Salva lo stato per il prossimo clic[cite: 2]
            _lastHeaderClicked = headerClicked;
            _lastDirection = direction;
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

        private void FilesListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (FilesListView.ContextMenu == null)
                return;

            try
            {
                FileSelectNone.IsEnabled = (FilesListView.SelectedItems.Count > 0);
                // FileSelectAll è abilitato se c'è almeno un file nella lista (non dipende dalla selezione attuale)
                FileSelectAll.IsEnabled = (FilesListView.Items.Count != 0);
                // Se è stato selezionato solo un file, anche se non valido, abilitiamo i comandi di apertura e apertura percorso
                FileOpenFolderMenuItem.IsEnabled = FileOpenMenuItem.IsEnabled = (FilesListView.SelectedItems.Count == 1);

                bool isFValidFile = false;

                FileItem? firstValidFile = null;

                // Viene verificato se il file è valido (ha un plugin associato) per abilitare il comando di importazione ed esportazione validatore.
                // Se sono selezionati più file, il comando di importazione è abilitato se almeno uno è valido,
                // mentre il comando di esportazione validatore è abilitato solo se esattamente uno è valido.
                foreach (var item in FilesListView.SelectedItems)
                    if (item is FileItem fi && !string.IsNullOrWhiteSpace(fi.Tipo))
                    {
                        isFValidFile = true;
                        firstValidFile = fi;
                        break;
                    }

                FileImportMenuItem.IsEnabled = (isFValidFile && firstValidFile != null);

                if (isFValidFile && firstValidFile != null && FilesListView.SelectedItems.Count == 1)
                    FileExportValidatorMenuItem.IsEnabled = (_plugins.FirstOrDefault(p => string.Equals(p.Name, firstValidFile.Tipo, StringComparison.OrdinalIgnoreCase)) != null);
                else
                    FileExportValidatorMenuItem.IsEnabled = false;
            }
            catch (Exception ex)
            {
                Log($"Errore : {ex.Message}");
            }
        }

        private void FileSelectAll_Click(object sender, RoutedEventArgs e)
        {
            FilesListView.SelectAll();
        }

        private void FileSelectNone_Click(object sender, RoutedEventArgs e)
        {
            FilesListView.UnselectAll();
        }

        private async void File_OpenPath_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Determina il file selezionato (può essere FileItem o FileInfo)
                string? fullPath = null;

                if (FilesListView.SelectedItem is FileItem fi)
                    fullPath = fi.FullPath;
                else if (FilesListView.SelectedItem is FileInfo ffi)
                    fullPath = ffi.FullName;

                if (string.IsNullOrWhiteSpace(fullPath))
                {
                    MessageBox.Show(this, "Seleziona un file per aprirne la cartella.", "Nessun file selezionato", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var directory = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    MessageBox.Show(this, $"Cartella non trovata: {directory ?? "<path non valido>"}", "Cartella non trovata", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Log($"Impossibile aprire la cartella: '{directory ?? "<path non valido>"}' non esiste.");
                    return;
                }

                // Se il file esiste, apriamo Esplora selezionandolo; altrimenti apriamo la cartella
                bool fileExists = File.Exists(fullPath);

                var psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true,
                    Arguments = fileExists ? $"/select,\"{fullPath}\"" : $"\"{directory}\""
                };

                Process.Start(psi);
                Log(fileExists
                    ? $"Aperta cartella e selezionato file in Esplora: {fullPath}"
                    : $"Aperta cartella in Esplora: {directory}");
            }
            catch (Exception ex)
            {
                Log($"Errore apertura cartella file: {ex.Message}");
                MessageBox.Show(this, $"Impossibile aprire la cartella del file:{Environment.NewLine}{ex.Message}", "Errore apertura", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void File_Import_Click(object? sender, RoutedEventArgs e)
        {
            var selectedItems = FilesListView.SelectedItems?.OfType<FileItem>().ToList();

            if (selectedItems == null || selectedItems.Count == 0)
            {
                MessageBox.Show(this, "Seleziona almeno un file da importare.", "Nessun file selezionato", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // ----------------------------------------------------------
            // Selezione batch: apriamo il dialog PRIMA di avviare l'import.
            // Il DB viene aperto qui solo per caricare i batch; viene subito
            // chiuso alla fine del blocco using per non tenere la connessione
            // aperta durante l'intero ciclo di import.
            // ----------------------------------------------------------
            int? selectedBatchId = null;

            try
            {
                var connStr = Properties.Settings.Default.ConnectionString;
                RRDADbContext dbForBatches;

                if (!string.IsNullOrWhiteSpace(connStr))
                {
                    var opt = new DbContextOptionsBuilder<RRDADbContext>();
                    opt.UseSqlServer(connStr);
                    dbForBatches = new RRDADbContext(opt.Options);
                }
                else
                {
                    dbForBatches = new RRDAContextFactory().CreateDbContext([]);
                }

                await using (dbForBatches)
                {
                    var batchDlg = new BatchSelectionDialog { Owner = this };
                    await batchDlg.LoadBatchesAsync(dbForBatches);

                    var dlgResult = batchDlg.ShowDialog();

                    // L'utente ha premuto Annulla (o chiuso la finestra): interrompiamo
                    if (dlgResult != true || !batchDlg.Confirmed)
                    {
                        Log("Importazione annullata dall'utente (selezione batch).");
                        return;
                    }

                    selectedBatchId = batchDlg.SelectedBatchId;
                }
            }
            catch (Exception ex)
            {
                Log($"Errore caricamento batch: {ex.Message}");
                MessageBox.Show(this,
                    $"Impossibile caricare i batch dal database:{Environment.NewLine}{ex.Message}",
                    "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Log(selectedBatchId.HasValue
                ? $"Batch selezionato: Id={selectedBatchId.Value}."
                : "Nessun batch selezionato: i file saranno importati senza associazione a un batch.");

            using var cts = new CancellationTokenSource();
            var progressDlg = new ImportProgressDialog(cts) { Owner = this };
            progressDlg.Show();

            int successCount = 0;
            int failCount = 0;
            string user = Environment.UserName;

            try
            {
                int i = 0;

                // Importiamo i file selezionati in sequenza (evita concorrenza su risorse condivise)
                foreach (var fi in selectedItems)
                {
                    if (cts.IsCancellationRequested)
                    {
                        Log("Importazione annullata dall'utente.");
                        break;
                    }

                    // Aggiorna la progress bar esterna (file i+1 di N)
                    progressDlg.SetFileProgress(++i, selectedItems.Count, fi.Name);

                    Log($"Avvio import per file selezionato: {fi.Name}");

                    try
                    {
                        bool ok = await ImportReport(fi, user, progressDlg, selectedBatchId, cts.Token);

                        if (ok)
                        {
                            successCount++;
                        }
                        else
                        {
                            failCount++;

                            Log($"Import non completato per '{fi.Name}'.");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Log("Importazione annullata dall'utente.");
                        failCount++;
                        break;
                    }
                    catch (Exception ex)
                    {
                        failCount++;

                        Log($"Eccezione durante import di '{fi.Name}': {ex.Message}");
                    }
                }

                _applyForAll = false; // reset della scelta "applica per tutti" per i duplicati, per i successivi cicli di import
                _duplicateStrategy = DuplicateImportStrategy.NewVersion; // reset della strategia di importazione per i duplicati

                FilesListView.SelectedItems?.Clear(); // deseleziona tutti i file al termine del ciclo di import
            }
            finally
            {
                progressDlg.Close();
            }

            Log($"Import multiplo completato. Successi: {successCount}, Falliti: {failCount}.");
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
                pluginFolder = ResolvePluginsFolder();
            }

            if (!Directory.Exists(pluginFolder))
            {
                Log($"Cartella plugin inesistente: {pluginFolder}");
                MessageBox.Show(this, $"Cartella plugin non trovata: {pluginFolder}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var outputFile = Path.Combine(pluginFolder, $"{plugin.Name}.xml");

            if (File.Exists(outputFile))
            {
                var res = MessageBox.Show(this,
                    $"Il file di validazione '{outputFile}' esiste già. Sovrascrivere?",
                    "Conferma sovrascrittura",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (res != MessageBoxResult.Yes)
                {
                    Log("Esportazione validatore annullata dall'utente (sovrascrittura).");
                    return;
                }
            }

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

        private void File_Open_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Seleziona il file dalla lista (può essere FileItem o FileInfo)
                if (FilesListView.SelectedItem is FileItem fi)
                {
                    if (!File.Exists(fi.FullPath))
                    {
                        MessageBox.Show(this, $"File non trovato: {fi.FullPath}", "File non trovato", MessageBoxButton.OK, MessageBoxImage.Warning);
                        Log($"Impossibile aprire file: non trovato '{fi.FullPath}'.");
                        return;
                    }

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = fi.FullPath,
                        UseShellExecute = true
                    };

                    Process.Start(psi);
                    Log($"Aperto file con applicazione predefinita: {fi.FullPath}");
                    return;
                }

                if (FilesListView.SelectedItem is FileInfo ffi)
                {
                    if (!File.Exists(ffi.FullName))
                    {
                        MessageBox.Show(this, $"File non trovato: {ffi.FullName}", "File non trovato", MessageBoxButton.OK, MessageBoxImage.Warning);
                        Log($"Impossibile aprire file: non trovato '{ffi.FullName}'.");
                        return;
                    }

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = ffi.FullName,
                        UseShellExecute = true
                    };

                    Process.Start(psi);
                    Log($"Aperto file con applicazione predefinita: {ffi.FullName}");
                    return;
                }

                MessageBox.Show(this, "Seleziona un file da aprire.", "Nessun file selezionato", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"Errore apertura file: {ex.Message}");
                MessageBox.Show(this, $"Impossibile aprire il file:{Environment.NewLine}{ex.Message}", "Errore apertura", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PluginsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PluginsListBox.SelectedItem is IReportImporter plugin)
            {
                Log($"Plugin selezionato: {plugin.Name} (v{plugin.Version}) - Estensione supportata: {plugin.SupportedFileExtension}");
            }
        }

        private void PluginsListBox_DragOver(object? sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[]?)e.Data.GetData(DataFormats.FileDrop) ?? [];
                    if (files.Length > 0 && Directory.Exists(files[0]))
                        e.Effects = DragDropEffects.Copy;
                    else
                        e.Effects = DragDropEffects.None;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            catch (Exception ex)
            {
                Log($"Errore in PluginsListBox_DragOver: {ex.Message}");
                e.Effects = DragDropEffects.None;
            }
            finally
            {
                e.Handled = true;
            }
        }

        private void PluginsListBox_Drop(object? sender, DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effects = DragDropEffects.None;
                    return;
                }

                var files = (string[]?)e.Data.GetData(DataFormats.FileDrop) ?? [];
                if (files.Length == 0)
                {
                    e.Effects = DragDropEffects.None;
                    return;
                }

                var first = files[0];
                if (!Directory.Exists(first))
                {
                    MessageBox.Show(this, "Per favore trascina una cartella valida contenente i plugin.", "Drop non valido", MessageBoxButton.OK, MessageBoxImage.Information);
                    e.Effects = DragDropEffects.None;
                    return;
                }

                // Salva la cartella come PluginsFolder nelle impostazioni e ricarica i plugin
                Properties.Settings.Default.PluginsFolder = first;
                //Properties.Settings.Default.Save();

                Log($"Cartella plugin impostata su '{first}' tramite drag & drop.");
                LoadPlugins();
                LoadFolders();

                e.Effects = DragDropEffects.Copy;
            }
            catch (Exception ex)
            {
                Log($"Errore in PluginsListBox_Drop: {ex.Message}");
                MessageBox.Show(this, $"Errore durante l'operazione di drop:{Environment.NewLine}{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Effects = DragDropEffects.None;
            }
            finally
            {
                e.Handled = true;
            }
        }

        private void OpenSettings_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new SettingsDialog
                {
                    Owner = this
                };

                var res = dlg.ShowDialog();
                if (res == true)
                {
                    // Ricarica le impostazioni applicate dall'utente
                    try
                    {
                        // Ricarica cartelle e plugin secondo le nuove impostazioni
                        LoadFolders();
                        LoadPlugins();
                        Log("Impostazioni aggiornate dall'utente e ricaricate.");
                    }
                    catch (Exception ex)
                    {
                        Log($"Errore ricaricamento impostazioni dopo salvataggio: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Errore apertura dialog impostazioni: {ex.Message}");
                MessageBox.Show(this, $"Impossibile aprire le impostazioni:{Environment.NewLine}{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void About_Click(object? sender, RoutedEventArgs e)
        {
            // Evita aperture multiple simultane
            if (_aboutWindow is { IsVisible: true })
            {
                _aboutWindow.Activate();
                return;
            }

            _aboutWindow = new SplashScreenWindow();

            // Pulizia riferimento quando la finestra si chiude
            _aboutWindow.Closed += (_, _) => _aboutWindow = null;

            // Apre in modalità "About": centrata sulla MainWindow,
            // si chiude automaticamente dopo 2 s
            _aboutWindow.ShowAsAbout(owner: this);
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
    }
}
