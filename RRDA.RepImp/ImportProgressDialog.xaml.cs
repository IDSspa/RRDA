using System.Windows;

namespace RRDA.RepImp
{
    public partial class ImportProgressDialog : Window
    {
        private readonly CancellationTokenSource _cts;

        /// <summary>Token passato a ImportAsync per supportare la cancellazione.</summary>
        public CancellationToken CancellationToken => _cts.Token;

        public ImportProgressDialog(CancellationTokenSource cts)
        {
            InitializeComponent();
            _cts = cts;
        }

        // ------------------------------------------------------------------ //
        //  API chiamata da MainWindow (sempre sul thread UI via Dispatcher)
        // ------------------------------------------------------------------ //

        /// <summary>Aggiorna la progress bar dei file (outer).</summary>
        public void SetFileProgress(int current, int total, string fileName)
        {
            FilesProgressBar.Maximum = total;
            FilesProgressBar.Value = current;
            FileLabelText.Text = $"File {current} di {total}: {fileName}";

            // Reimposta la bar interna all'inizio di ogni nuovo file
            RowsProgressBar.IsIndeterminate = true;
            RowLabelText.Text = "Elaborazione campi...";
        }

        /// <summary>Aggiorna la progress bar delle righe (inner), notificata dal plugin.</summary>
        public void SetRowProgress(int processed, int total, string? message)
        {
            if (total > 0)
            {
                RowsProgressBar.IsIndeterminate = false;
                RowsProgressBar.Maximum = total;
                RowsProgressBar.Value = processed;
            }

            if (!string.IsNullOrWhiteSpace(message))
                RowLabelText.Text = message;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelButton.IsEnabled = false;
            CancelButton.Content = "Annullamento...";
            _cts.Cancel();
        }
    }
}