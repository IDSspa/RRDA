using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using System;
using System.Threading.Tasks;

namespace RRDA.RepImp
{
    /// <summary>
    /// Helper class per testare la connessione al database RRDA.
    /// </summary>
    public static class DatabaseConnectionTester
    {
        /// <summary>
        /// Testa la connessione al database usando la connection string fornita.
        /// </summary>
        /// <param name="connectionString">Connection string da testare. Se vuota, usa il factory di default.</param>
        /// <returns>Tupla (successo, messaggio)</returns>
        public static async Task<(bool Success, string Message)> TestConnectionAsync(string? connectionString)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    return (false, "Connection string vuota. Usare la configurazione di default.");
                }

                // Crea un DbContext temporaneo con la connection string fornita
                var optionsBuilder = new DbContextOptionsBuilder<RRDADbContext>();
                optionsBuilder.UseSqlServer(connectionString);

                using var db = new RRDADbContext(optionsBuilder.Options);
                // Tenta di connettersi al database e di eseguire una semplice query
                var canConnect = await db.Database.CanConnectAsync();

                if (!canConnect)
                {
                    return (false, "Impossibile connettersi al database. Verifica la connection string e le credenziali.");
                }

                // Tenta di accedere alla tabella ReportTypes come verifica ulteriore
                var reportTypesCount = await db.ReportTypes.CountAsync();

                return (true, $"✓ Connessione riuscita. Tabella ReportTypes contiene {reportTypesCount} record.");
            }
            catch (Exception ex)
            {
                return (false, $"Errore di connessione: {ex.Message}");
            }
        }

        /// <summary>
        /// Versione sincrona (per context non asincroni). Sconsigliata in UI thread.
        /// </summary>
        public static (bool Success, string Message) TestConnection(string? connectionString)
        {
            return TestConnectionAsync(connectionString).Result;
        }
    }
}
