using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace RRDA.RepImp
{
    /// <summary>
    /// Splash screen temporizzato con fade-in / fade-out.
    ///
    /// — Modalità STARTUP (default):
    ///     Aperta da App.OnStartup. Al termine solleva <see cref="SplashClosed"/>
    ///     così App può aprire la MainWindow.
    ///
    /// — Modalità INFO (da menu "?"):
    ///     Chiamare <see cref="ShowAsAbout"/> invece di Show().
    ///     La finestra è owner-ed dalla MainWindow, rimane sopra di essa,
    ///     si chiude da sola dopo 2 s senza interferire con il ciclo di vita
    ///     dell'applicazione.
    /// </summary>
    public partial class SplashScreenWindow : Window
    {
        // Durata visibilità (ms) — escluse le animazioni fade
        private const int DisplayMilliseconds = 2000;

        private readonly DispatcherTimer _timer;
        private bool _isAboutMode;

        /// <summary>
        /// Sollevato (solo in modalità startup) quando lo splash è completamente
        /// chiuso. Usare questo evento per aprire la MainWindow.
        /// </summary>
        public event EventHandler? SplashClosed;

        public SplashScreenWindow()
        {
            InitializeComponent();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DisplayMilliseconds)
            };
            _timer.Tick += OnTimerTick;
        }

        // ------------------------------------------------------------------ //
        //  API pubblica
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Mostra lo splash come pannello "Informazioni su RRDA":
        ///   - Owner impostato alla finestra chiamante (rimane sopra la MainWindow)
        ///   - Si chiude automaticamente dopo 2 s
        ///   - Non interferisce con ShutdownMode dell'applicazione
        /// </summary>
        /// <param name="owner">Finestra proprietaria (di solito MainWindow).</param>
        public void ShowAsAbout(Window owner)
        {
            _isAboutMode = true;
            StatusText.Text = string.Empty;         // nessun testo di stato in modalità About
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Show();
        }

        /// <summary>
        /// Aggiorna il messaggio di stato visibile nella barra inferiore.
        /// Thread-safe: può essere chiamato da thread non-UI.
        /// Ignorato in modalità About.
        /// </summary>
        public void SetStatus(string message)
        {
            if (_isAboutMode) return;
            Dispatcher.Invoke(() => StatusText.Text = message);
        }

        // ------------------------------------------------------------------ //
        //  Ciclo di vita interno
        // ------------------------------------------------------------------ //

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            if(!_isAboutMode)
                _timer.Start();
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            _timer.Stop();

            if (!_isAboutMode)
                StatusText.Text = "Avvio completato.";

            var fadeOut = (Storyboard)FindResource("FadeOut");
            fadeOut.Begin(this);
        }

        /// <summary>
        /// Callback al termine del fade-out.
        /// In modalità startup notifica i subscriber tramite <see cref="SplashClosed"/>.
        /// </summary>
        private void FadeOut_Completed(object? sender, EventArgs e)
        {
            if (!_isAboutMode)
                SplashClosed?.Invoke(this, EventArgs.Empty);

            Close();
        }

        /// <summary>
        /// Gestore per il click singolo del mouse (vuoto).
        /// </summary>
        private void SplashWindow_MouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
        {
            if (_isAboutMode)
                Close();
        }
    }
}
