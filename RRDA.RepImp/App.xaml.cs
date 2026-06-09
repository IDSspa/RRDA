using System.Windows;

using RRDA.Data;
using RRDA.Plugins.Common;

namespace RRDA.RepImp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly IReportTypeCompatibilityChecker _reportTypeCompatibilityChecker =
            new ReportTypeCompatibilityChecker();
        private readonly IPluginService _pluginService = new PluginService();
        private readonly IAuditService _auditService = new AuditService();
        private readonly IImportResultRepository _importResultRepository =
            new ImportResultRepository(new SettingsDbContextFactory());

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ThemeManager.Apply(RRDA.RepImp.Properties.Settings.Default.Theme);

            // Impedisce la chiusura automatica dell'applicazione
            // quando la SplashScreenWindow (finestra di avvio) si chiude.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var splash = new SplashScreenWindow();

            // Quando lo splash termina, apri la MainWindow e
            // ripristina la modalità di shutdown normale.
            splash.SplashClosed += (_, _) =>
            {
                var main = new MainWindow(
                    _reportTypeCompatibilityChecker,
                    _pluginService,
                    _auditService,
                    _importResultRepository);
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                MainWindow = main;
                main.Show();
            };

            splash.Show();
        }
    }
}
