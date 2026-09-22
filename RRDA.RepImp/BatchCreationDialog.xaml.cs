using System.Windows;

namespace RRDA.RepImp
{
    public partial class BatchCreationDialog : Window
    {
        public string BatchName { get; private set; } = string.Empty;
        public string? Description { get; private set; }
        public bool IsMaintenance { get; private set; }

        public BatchCreationDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => NameTextBox.Focus();
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            var name = NameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(this, "Il nome del batch è obbligatorio.", "Nome richiesto", MessageBoxButton.OK, MessageBoxImage.Information);
                NameTextBox.Focus();
                return;
            }

            BatchName = name;
            Description = string.IsNullOrWhiteSpace(DescriptionTextBox.Text) ? null : DescriptionTextBox.Text.Trim();
            IsMaintenance = MaintenanceCheckBox.IsChecked == true;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
