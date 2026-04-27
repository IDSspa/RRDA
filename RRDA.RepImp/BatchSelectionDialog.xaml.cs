using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using System.Windows;

namespace RRDA.RepImp
{
    public partial class BatchSelectionDialog : Window
    {
        // DTO interno per popolare la ListBox con una stringa leggibile
        private sealed record BatchItem(int Id, string DisplayName);

        /// <summary>
        /// Id del batch selezionato dall'utente.
        /// null  → l'utente ha scelto "Nessun batch".
        /// </summary>
        public int? SelectedBatchId { get; private set; }

        /// <summary>
        /// true  → il dialog è stato confermato (OK o "Nessun batch").
        /// false → l'utente ha annullato: il chiamante deve interrompere l'import.
        /// </summary>
        public bool Confirmed { get; private set; }

        public BatchSelectionDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Carica i batch disponibili dal DB e popola la ListBox.
        /// Deve essere chiamato dal chiamante prima di ShowDialog().
        /// </summary>
        public async Task LoadBatchesAsync(RRDADbContext db)
        {
            var batches = await db.ReportBatches
                                  .AsNoTracking()
                                  .OrderBy(b => b.Name)
                                  .ToListAsync();

            var items = batches
                .Select(b => new BatchItem(
                    b.Id,
                    b.IsMaintenance ? $"{b.Name}  [manutenzione]" : b.Name))
                .ToList();

            BatchListBox.ItemsSource = items;

            if (items.Count > 0)
                BatchListBox.SelectedIndex = 0;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (BatchListBox.SelectedItem is BatchItem selected)
            {
                SelectedBatchId = selected.Id;
                Confirmed = true;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show(this,
                    "Seleziona un batch dall'elenco oppure scegli \"Nessun batch\".",
                    "Selezione richiesta",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void NoBatchButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedBatchId = null;
            Confirmed = true;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
        }
    }
}
