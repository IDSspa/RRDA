namespace RRDA.Data
{
    /// <summary>
    /// Mappa un account Windows (dominio\utente) su un ruolo applicativo interno.
    /// Poiché non è disponibile la gestione di gruppi AD, il ruolo viene determinato
    /// direttamente da questa tabella a runtime tramite <see cref="AppUserRole"/>.
    /// </summary>
    public class AppUser
    {
        public int Id { get; set; }

        /// <summary>
        /// Nome utente Windows nel formato DOMAIN\username oppure solo username
        /// per ambienti workgroup. Confronto case-insensitive a livello applicativo.
        /// Es: "IDS\m.santucci"
        /// </summary>
        public required string WindowsUsername { get; set; }

        /// <summary>Ruolo applicativo assegnato all'utente.</summary>
        public AppUserRole Role { get; set; } = AppUserRole.Operator;

        /// <summary>Nota libera (es: nome completo, reparto).</summary>
        public string? DisplayName { get; set; }

        /// <summary>Indica se l'account è abilitato. Se false, l'accesso è negato.</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>Data/ora dell'ultima autenticazione registrata (UTC).</summary>
        public DateTime? LastLoginAt { get; set; }

        /// <summary>Data/ora di creazione del record (UTC).</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Ruoli applicativi disponibili.
    /// I valori interi vengono persistiti nel DB — non modificare l'ordine.
    /// </summary>
    public enum AppUserRole
    {
        /// <summary>Accesso in sola lettura: visualizzazione ed esportazione dati.</summary>
        Operator = 0,

        /// <summary>
        /// Accesso a Plugins e Dati (inclusa cancellazione).
        /// Visualizzazione sezione Amministrazione.
        /// </summary>
        Supervisor = 1,

        /// <summary>Accesso completo a tutte le sezioni, inclusa gestione utenti.</summary>
        Admin = 2
    }
}
