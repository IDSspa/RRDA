using System.Windows;

namespace RRDA.RepImp
{
    public partial class ValidatorConflictDialog : Window
    {
        public ValidatorConflictDialog(string validatorPath)
        {
            InitializeComponent();

            ValidatorPath = validatorPath
                ?? throw new ArgumentNullException(nameof(validatorPath));
        }

        public string ValidatorPath { get; set; }

        public ValidatorConflictAction SelectedAction { get; private set; }
            = ValidatorConflictAction.Cancel;

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            SelectedAction = ValidatorConflictAction.Cancel;
            DialogResult = false;
        }

        private void Overwrite_Click(object sender, RoutedEventArgs e)
        {
            SelectedAction = ValidatorConflictAction.Overwrite;
            DialogResult = true;
        }

        private void Merge_Click(object sender, RoutedEventArgs e)
        {
            SelectedAction = ValidatorConflictAction.Merge;
            DialogResult = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            // Anche la chiusura mediante la X equivale ad Annulla.
            if (DialogResult != true)
                SelectedAction = ValidatorConflictAction.Cancel;

            base.OnClosed(e);
        }
    }
}