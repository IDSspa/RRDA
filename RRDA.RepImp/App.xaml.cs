using System.Windows;

namespace RRDA.RepImp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Impedisce la chiusura automatica dell'applicazione
            // quando la SplashScreenWindow (finestra di avvio) si chiude.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var splash = new SplashScreenWindow();

            // Quando lo splash termina, apri la MainWindow e
            // ripristina la modalità di shutdown normale.
            splash.SplashClosed += (_, _) =>
            {
                var main = new MainWindow();
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                MainWindow = main;
                main.Show();
            };

            splash.Show();
        }
    }
}
