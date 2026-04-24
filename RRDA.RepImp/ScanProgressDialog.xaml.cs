using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace RRDA.RepImp
{
    public partial class ScanProgressDialog : Window
    {
        private readonly CancellationTokenSource _cts;

        public CancellationToken CancellationToken => _cts.Token;

        public ScanProgressDialog(CancellationTokenSource cts)
        {
            InitializeComponent();
            _cts = cts;
        }

        /// <summary>
        /// Imposta la fase corrente (es. "Scansione cartelle..." / "Catalogazione file...").
        /// Deve essere chiamato sul thread UI.
        /// </summary>
        public void SetPhase(string phase, bool indeterminate = true, int max = 100)
        {
            PhaseLabelText.Text = phase;
            MainProgressBar.IsIndeterminate = indeterminate;
            if (!indeterminate)
                MainProgressBar.Maximum = max;
        }

        /// <summary>
        /// Aggiorna la progress bar e il testo di dettaglio.
        /// Deve essere chiamato sul thread UI.
        /// </summary>
        public void SetProgress(int value, string? detail = null)
        {
            MainProgressBar.Value = value;
            if (detail is not null)
                DetailLabelText.Text = detail;
        }

        /// <summary>
        /// Aggiorna solo il testo di dettaglio (es. nome file/cartella corrente).
        /// Deve essere chiamato sul thread UI.
        /// </summary>
        public void SetDetail(string folder)
        {
            if (string.IsNullOrEmpty(folder))
                return;

            double maxWidth = DetailLabelText.ActualWidth;

            double MeasureText(string text)
            {
                var formatted = new FormattedText(
                    text,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(DetailLabelText.FontFamily, DetailLabelText.FontStyle, DetailLabelText.FontWeight, DetailLabelText.FontStretch),
                    DetailLabelText.FontSize,
                    Brushes.Black,
                    VisualTreeHelper.GetDpi(DetailLabelText).PixelsPerDip);

                return formatted.Width;
            }


            if (MeasureText(folder)<= DetailLabelText.ActualWidth)
            {
                DetailLabelText.Text = folder;
                return;
            }

            string ellipsis = "...";

            // Parti iniziali e finali
            string start = folder;
            string end = "";

            // Separatore corretto
            char[] separators = ['\\', '/'];

            int lastSep = folder.LastIndexOfAny(separators);
            if (lastSep >= 0)
            {
                end = folder[lastSep..]; // include /file.txt
                start = folder[..lastSep];
            }

            // Riduzione progressiva
            for (int i = 1; i < start.Length; i++)
            {
                string shortenedStart = start[..^i];
                string candidate = shortenedStart + ellipsis + end;

                if (MeasureText(candidate) <= maxWidth)
                {
                    DetailLabelText.Text = candidate;
                    return;
                }
            }

            // Se ancora troppo lungo, accorcia anche la parte finale
            for (int i = 1; i < end.Length; i++)
            {
                string shortenedEnd = end[i..];
                string candidate = ellipsis + shortenedEnd;

                if (MeasureText(candidate) <= maxWidth)
                {
                    DetailLabelText.Text = candidate;
                    return;
                }
            }

            DetailLabelText.Text = ellipsis;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelButton.IsEnabled = false;
            CancelButton.Content = "Annullamento...";
            _cts.Cancel();
        }
    }
}