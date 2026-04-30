using RRDA.Data;
using System.Windows;

namespace RRDA.RepImp
{
    public partial class DuplicateImportDialog : Window
    {
        /// <summary>
        /// Strategia scelta dall'utente. Valorizzata solo se <see cref="Confirmed"/> è true.
        /// </summary>
        public DuplicateImportStrategy SelectedStrategy { get; private set; }
        public bool ApplyForAll { get; private set; } = false;

        /// <summary>
        /// true  → l'utente ha confermato una scelta.
        /// false → l'utente ha annullato: il chiamante deve interrompere l'import del file.
        /// </summary>
        public bool Confirmed { get; private set; }

        /// <param name="fileName">Nome del file da mostrare nel messaggio.</param>
        /// <param name="existingImports">
        /// Numero di import precedenti dello stesso file già presenti in DB.
        /// Usato per personalizzare il testo del messaggio.
        /// </param>
        public DuplicateImportDialog(string fileName, int existingImports)
        {
            InitializeComponent();

            MessageText.Text = existingImports == 1
                ? $"Il file \"{fileName}\" risulta già importato ({existingImports} volta). Scegli come procedere:"
                : $"Il file \"{fileName}\" risulta già importato ({existingImports} volte). Scegli come procedere:";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedStrategy = BlockRadio.IsChecked == true   ? DuplicateImportStrategy.Block
                             : ReplaceRadio.IsChecked == true ? DuplicateImportStrategy.Replace
                                                              : DuplicateImportStrategy.NewVersion;
            Confirmed = true;
            DialogResult = true;
            ApplyForAll = (ApplyToAllCheckBox.IsChecked == true);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
        }
    }
}
