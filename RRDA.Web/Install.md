# Istruzioni di installazione per RRDA Web

1. Requisiti di sistema:
   - Sistema operativo: Windows Server 2016 o superiore
   - .NET Framework: Versione 4.7.2 o superiore
   - IIS (Internet Information Services) installato e configurato
   - SQL Server Express: Versione 2016 o superiore

2. Installazione di RRDA Web:
   - Scaricare il pacchetto di installazione di RRDA Web dal sito ufficiale.
   - Rimuovere se presente il file: appsettings.Development.json

3. Configurazione del database:
   - Creare un nuovo database in SQL Server Express per RRDA Web.
   - Eseguire lo script SQL fornito con il pacchetto di installazione per creare le tabelle necessarie.

4. Configurazione di IIS:
   - Aprire IIS Manager e creare un nuovo sito web per RRDA Web.
   - Configurare il percorso fisico del sito web per puntare alla cartella di installazione di RRDA Web.
   - Impostare le autorizzazioni appropriate per il sito web.

5. Configurazione dell'applicazione:
   - Aprire il file di configurazione (web.config) nella cartella di installazione di RRDA Web.
   - Modificare le stringhe di connessione al database per puntare al database creato in precedenza.

6. Avvio dell'applicazione:
   - Eseguire da powershell: $env:RRDA_BootstrapAdmin__WindowsUsername="dominio\nomeutente" (per impostare l'utente amministratore)
   - Avviare il sito web in IIS e accedere all'applicazione RRDA Web tramite il browser utilizzando l'URL configurato.

7. Deploy plugins:
   - Copiare i file dei plugin nella cartella "Plugins" all'interno della directory di installazione di RRDA Web.
   - Copiare i file di validazione .xml nella cartella "Plugins" all'interno della directory di installazione di RRDA Web.
   - Riavviare il sito web in IIS per caricare i nuovi plugin.
   - Verificare la presenza dei plugin installati accedendo alla sezione "Plugins" dell'applicazione RRDA Web.
   - Verificare la corrispondenza dei plugin con la sezione "Tipi di report" dell'applicazione RRDA Web.