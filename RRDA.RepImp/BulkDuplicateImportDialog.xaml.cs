using RRDA.Data;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace RRDA.RepImp;

public sealed class BulkDuplicateImportItem(
    string filePath,
    string fileName,
    DateTime lastWriteTime,
    string reportTypeKey,
    int existingCount) : INotifyPropertyChanged
{
    public string FilePath { get; } = filePath;
    public string FileName { get; } = fileName;
    public DateTime LastWriteTime { get; } = lastWriteTime;
    public string ReportTypeKey { get; } = reportTypeKey;
    public int ExistingCount { get; } = existingCount;

    private string _selectedStrategy = "Mantieni come nuova versione";
    public string SelectedStrategy
    {
        get => _selectedStrategy;
        set
        {
            if (_selectedStrategy == value)
                return;

            _selectedStrategy = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedStrategy)));
        }
    }

    public DuplicateImportStrategy Strategy => SelectedStrategy switch
    {
        "Blocca / Non importare" => DuplicateImportStrategy.Block,
        "Sostituisci" => DuplicateImportStrategy.Replace,
        _ => DuplicateImportStrategy.NewVersion
    };

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class BulkDuplicateImportDialog : Window
{
    public ObservableCollection<BulkDuplicateImportItem> Items { get; }
    public IReadOnlyList<string> AvailableStrategies { get; } =
    [
        "Blocca / Non importare",
        "Sostituisci",
        "Mantieni come nuova versione"
    ];
    public bool Confirmed { get; private set; }
    public BulkDuplicateImportDialog(IEnumerable<BulkDuplicateImportItem> items)
    {
        InitializeComponent();
        Items = new ObservableCollection<BulkDuplicateImportItem>(items);
        DataContext = this;
    }
    private void ApplyStrategy_Click(object sender, RoutedEventArgs e)
    {
        if (BulkStrategyComboBox.SelectedItem is not string strategy)
            return;

        var selectedItems = DuplicateItemsGrid.SelectedItems.Cast<BulkDuplicateImportItem>().ToList();
        IEnumerable<BulkDuplicateImportItem> targets = selectedItems.Count > 0 ? selectedItems : Items;
        foreach (var item in targets)
            item.SelectedStrategy = strategy;
    }
    private void Confirm_Click(object sender, RoutedEventArgs e) { Confirmed = true; DialogResult = true; }
    private void Cancel_Click(object sender, RoutedEventArgs e) { Confirmed = false; DialogResult = false; }
}
