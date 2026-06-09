# Installazione server di RRDA.Web

## Scopo e architettura

Questa procedura descrive la predisposizione conservativa di una singola macchina
virtuale Windows denominata **RRDA**, sulla quale sono installati:

- IIS per l'esecuzione di `RRDA.Web`;
- SQL Server Express, istanza `SQLEXPRESS`, per il database `RRDA.Db`.

La VM deve essere utilizzata esclusivamente per RRDA e gestita come server, non
come postazione di sviluppo. Gli utenti accedono al sito con autenticazione
Windows; l'applicazione accede invece al database tramite l'identità dedicata
dell'application pool IIS.

Il sito deve essere accessibile **esclusivamente dalla rete intranet locale**.
Non deve essere pubblicato su Internet, esposto tramite NAT, proxy pubblico,
port-forwarding o regole firewall aperte verso reti non autorizzate.

```text
Utente di dominio
      |
      | HTTPS + Windows Authentication
      v
IIS / RRDA.Web
Application Pool: RRDA.Web
Identità: IIS APPPOOL\RRDA.Web
      |
      | Integrated Security, accesso locale
      v
SQL Server Express .\SQLEXPRESS
Database RRDA.Db
```

Non è necessario configurare delega Kerberos verso SQL Server: IIS e SQL Express
sono sullo stesso server e la connessione al database usa l'identità
dell'application pool, non quella dell'utente browser.

## Parametri da definire prima dell'installazione

Sostituire i valori di esempio con quelli approvati per l'ambiente:

| Parametro | Valore consigliato/esempio |
|---|---|
| Nome VM | `RRDA` |
| FQDN interno del sito | `rrda.example.local` |
| URL intranet | `https://rrda.example.local` |
| Application pool | `RRDA.Web` |
| Sito IIS | `RRDA.Web` |
| Istanza SQL | `.\SQLEXPRESS` |
| Database | `RRDA.Db` |
| Cartella applicazione | `D:\RRDA\Web` |
| Cartella plugin | `D:\RRDA\Plugins` |
| Cartella dati SQL | `D:\RRDA\SqlData` |
| Cartella pacchetti di deploy | `D:\RRDA\Deploy` |
| Cartella backup SQL | `D:\RRDA\Backup` |
| Primo amministratore | `DOMINIO\nomeutente` |

Usare preferibilmente il nome DNS canonico interno della VM, per esempio
`rrda.example.local`, e un certificato TLS emesso dalla CA aziendale per tale
nome. Il record DNS deve esistere soltanto nel DNS interno. Evitare di pubblicare
il sito tramite indirizzo IP o nome temporaneo. Se si usa un alias DNS diverso
dal nome canonico della VM, fare registrare e verificare dagli amministratori di
dominio gli SPN HTTP necessari all'autenticazione Kerberos.

## 1. Predisposizione della VM

### Requisiti minimi consigliati

Per un'installazione iniziale conservativa:

- Windows Server x64 supportato e aggiornato;
- 4 vCPU;
- 12-16 GB RAM;
- disco sistema `C:` separato dai dati;
- volume dati `D:` dimensionato considerando database, crescita, pacchetti e
  backup;
- indirizzo IP statico o prenotazione DHCP;
- join al dominio Active Directory;
- record DNS per il FQDN del sito;
- sincronizzazione oraria funzionante;
- backup della VM coerente con le policy aziendali.

SQL Server Express ha limiti di capacità rispetto alle edizioni superiori.
Monitorare la crescita di `RRDA.Db`; se il database si avvicina ai limiti
dell'edizione Express, pianificare la migrazione a un'edizione SQL Server
adeguata.

### Hardening di base

- Applicare aggiornamenti Windows prima del rilascio.
- Installare soltanto ruoli e funzionalità necessari.
- Limitare RDP agli amministratori e alle reti di gestione.
- Non esporre SQL Server Express alla rete: mantenere TCP/IP disabilitato salvo
  un requisito esplicito.
- Aprire sul firewall soltanto HTTPS `443`, limitando `RemoteAddress` alle subnet
  intranet autorizzate.
- Non creare binding HTTP `80`: gli utenti devono utilizzare direttamente l'URL
  HTTPS.
- Non configurare NAT, reverse proxy pubblico o pubblicazione Internet.
- Configurare antivirus/EDR evitando esclusioni ampie; eventuali esclusioni per i
  file dati SQL devono seguire le policy del prodotto di sicurezza.

Creare le directory:

```powershell
New-Item -ItemType Directory -Force D:\RRDA\Web
New-Item -ItemType Directory -Force D:\RRDA\Plugins
New-Item -ItemType Directory -Force D:\RRDA\SqlData
New-Item -ItemType Directory -Force D:\RRDA\Deploy
New-Item -ItemType Directory -Force D:\RRDA\Backup
```

## 2. Installazione dei componenti

### IIS

Installare IIS includendo almeno:

- Web Server;
- Static Content;
- Default Document;
- HTTP Errors;
- Request Filtering;
- Windows Authentication;
- IIS Management Console;
- Management Service, necessario soltanto se si usa la pubblicazione Web Deploy
  remota integrata in Visual Studio.

Non abilitare ASP.NET 4.x per RRDA.Web: l'applicazione usa ASP.NET Core.

Esempio PowerShell eseguito come amministratore:

```powershell
Install-WindowsFeature Web-Server,Web-Static-Content,Web-Default-Doc,Web-Http-Errors,Web-Filtering,Web-Windows-Auth,Web-Mgmt-Console,Web-Mgmt-Service
```

### .NET Hosting Bundle

Installare sul server il **.NET 8 Hosting Bundle** x64. Il bundle installa il
runtime ASP.NET Core e l'ASP.NET Core Module necessario a IIS.

Dopo l'installazione riavviare IIS o, preferibilmente durante il provisioning
iniziale, riavviare la VM.

Installare IIS prima del Hosting Bundle. Se IIS viene installato successivamente,
eseguire nuovamente il programma di installazione del Hosting Bundle in modalità
Repair, quindi riavviare IIS.

### SQL Server Express e SSMS

Installare:

- SQL Server Express con **Database Engine Services**;
- istanza denominata `SQLEXPRESS`;
- autenticazione Windows;
- SQL Server Management Studio (SSMS) per amministrazione e manutenzione.

Nel programma di installazione SQL Server scegliere l'installazione
**Custom** e verificare:

- feature selezionata: `Database Engine Services`;
- named instance e instance ID: `SQLEXPRESS`;
- account del servizio: identità virtuale predefinita
  `NT SERVICE\MSSQL$SQLEXPRESS`;
- startup type del Database Engine: `Automatic`;
- authentication mode: `Windows authentication`;
- almeno un gruppo amministrativo aziendale aggiunto tra gli amministratori SQL;
- nessuna funzionalità non necessaria installata.

Durante l'installazione, configurare quando possibile `D:\RRDA\SqlData` come
directory predefinita per dati e log SQL. Non salvare file database nella cartella
dell'applicazione IIS.

Verificare che il servizio seguente sia avviato e configurato con avvio
automatico:

```text
SQL Server (SQLEXPRESS)
```

Per questa architettura non sono necessari SQL Server Browser, autenticazione SQL,
TCP/IP o una porta SQL aperta nel firewall.

### Configurazione conservativa dell'istanza SQL Express

Aprire **SQL Server Configuration Manager** e verificare:

- `SQL Server (SQLEXPRESS)`: avvio `Automatic` e servizio in esecuzione;
- `SQL Server Browser`: `Disabled`;
- **SQL Server Network Configuration > Protocols for SQLEXPRESS > TCP/IP**:
  `Disabled`;
- **Named Pipes**: `Disabled`, salvo requisito locale documentato;
- **Shared Memory**: `Enabled`, usato dal collegamento locale IIS-SQL.

Non creare regole firewall in ingresso per porte SQL (`1433` o porte dinamiche).

Aprire SSMS, collegarsi a `.\SQLEXPRESS`, fare clic destro sul server e scegliere
**Properties**:

- **Security > Server authentication**: `Windows Authentication mode`;
- **Connections > Allow remote connections to this server**: disabilitato;
- **Memory > Minimum server memory**: `0 MB`;
- **Memory > Maximum server memory**: impostare un limite esplicito.

Su una VM con 12-16 GB RAM condivisa tra IIS e SQL Express, un valore iniziale
conservativo è `4096 MB`. Monitorare memoria e carico prima di aumentarlo; evitare
il valore predefinito praticamente illimitato.

Verificare la configurazione anche tramite query:

```sql
SELECT
    SERVERPROPERTY('MachineName') AS MachineName,
    SERVERPROPERTY('InstanceName') AS InstanceName,
    SERVERPROPERTY('Edition') AS Edition,
    SERVERPROPERTY('ProductVersion') AS ProductVersion,
    SERVERPROPERTY('IsIntegratedSecurityOnly') AS WindowsAuthenticationOnly;

EXEC sys.sp_configure N'show advanced options', 1;
RECONFIGURE;
EXEC sys.sp_configure N'max server memory (MB)';
```

Per impostare il limite memoria da query:

```sql
EXEC sys.sp_configure N'show advanced options', 1;
RECONFIGURE;
EXEC sys.sp_configure N'max server memory (MB)', 4096;
RECONFIGURE;
```

## 3. Preparazione del database

Questa sezione deve essere eseguita con un account amministrativo SQL. L'identità
dell'application pool non deve essere proprietaria del database e non deve
eseguire migrazioni.

### Creazione del database

1. Aprire SSMS e collegarsi a `.\SQLEXPRESS` con Windows Authentication usando
   un account amministrativo.
2. In **Object Explorer**, fare clic destro su **Databases** e scegliere
   **New Database...**.
3. Impostare il nome `RRDA.Db`.
4. Verificare che file dati e log siano collocati nella directory SQL prevista,
   preferibilmente `D:\RRDA\SqlData`.
5. Nella pagina **Files**, configurare crescita automatica con incrementi fissi
   in MB, non in percentuale. Come valori iniziali da validare con il carico:
   - file dati: dimensione iniziale `512 MB`, crescita `256 MB`;
   - file log: dimensione iniziale `256 MB`, crescita `128 MB`.
6. Creare il database e impostare il modello di recupero `SIMPLE`.

In alternativa, dopo avere configurato le directory predefinite dell'istanza:

```sql
USE [master];
GO
CREATE DATABASE [RRDA.Db];
GO
ALTER DATABASE [RRDA.Db] SET RECOVERY SIMPLE;
GO
ALTER DATABASE [RRDA.Db] SET AUTO_CLOSE OFF;
GO
ALTER DATABASE [RRDA.Db] SET AUTO_SHRINK OFF;
GO
```

Non abilitare `AUTO_CLOSE` o `AUTO_SHRINK`. Non usare l'identità
`IIS APPPOOL\RRDA.Web` per creare il database.

### Applicazione delle migrazioni dal file SQL

Il file da eseguire è:

```text
RRDA.Db.Migrations.sql
```

Lo script è generato dalle migrazioni Entity Framework Core durante la build di
`RRDA.RepImp.Setup` e si trova nel pacchetto di consegna oppure in:

```text
artifacts\database\Release\RRDA.Db.Migrations.sql
```

Lo script non contiene `CREATE DATABASE` né un comando `USE [RRDA.Db]`: deve
quindi essere eseguito esplicitamente sul database `RRDA.Db`.

Procedura consigliata:

1. Copiare lo script sul server, ad esempio in:

   ```text
   D:\RRDA\Deploy\Database\RRDA.Db.Migrations.sql
   ```

2. Verificare che il file provenga dal pacchetto Release approvato e registrarne
   l'hash:

   ```powershell
   Get-FileHash "D:\RRDA\Deploy\Database\RRDA.Db.Migrations.sql" -Algorithm SHA256
   ```

3. Arrestare temporaneamente l'application pool `RRDA.Web` durante un
   aggiornamento di un database già operativo.
4. Eseguire un backup verificabile di `RRDA.Db`.
5. Aprire lo script in SSMS.
6. Selezionare esplicitamente `RRDA.Db` nel menu database della query.
7. Eseguire lo script e verificare che termini senza errori.

In alternativa, eseguire da PowerShell amministrativa con `sqlcmd`:

```powershell
$script = "D:\RRDA\Deploy\Database\RRDA.Db.Migrations.sql"
$log = "D:\RRDA\Deploy\Database\RRDA.Db.Migrations.log"

sqlcmd -S ".\SQLEXPRESS" -E -d "RRDA.Db" -b -r 1 -i $script -o $log
if ($LASTEXITCODE -ne 0) {
    throw "Migrazione RRDA.Db non riuscita. Consultare $log"
}
```

Il comando `sqlcmd` deve essere installato sul server. Se non disponibile, usare
SSMS oppure installare gli strumenti da riga di comando SQL Server approvati.

Parametri rilevanti:

- `-E`: autenticazione Windows dell'account amministrativo corrente;
- `-d RRDA.Db`: impedisce di eseguire accidentalmente lo script su `master`;
- `-b`: restituisce un codice di errore in caso di errore SQL;
- `-r 1`: scrive gli errori sul flusso di errore;
- `-o`: conserva un log dell'esecuzione.

Lo script è idempotente: può essere rieseguito durante gli aggiornamenti e
applica soltanto le migrazioni non ancora registrate in
`dbo.__EFMigrationsHistory`. Non modificare manualmente tale tabella.

### Verifica delle migrazioni

Al termine, eseguire in SSMS:

```sql
USE [RRDA.Db];
GO

SELECT [MigrationId], [ProductVersion]
FROM [dbo].[__EFMigrationsHistory]
ORDER BY [MigrationId];
GO

SELECT
    DB_NAME() AS DatabaseName,
    DATABASEPROPERTYEX(DB_NAME(), 'Status') AS DatabaseStatus,
    DATABASEPROPERTYEX(DB_NAME(), 'Recovery') AS RecoveryModel;
GO
```

Confrontare l'elenco delle migrazioni applicate con quello previsto nel file SQL
consegnato. Verificare inoltre la presenza almeno delle tabelle applicative
`AppUsers`, `AuditEvents`, `ReportFiles`, `ReportTypes`, `ReportEntities` e
`ReportProperties`.

Eseguire infine un controllo di integrità:

```sql
DBCC CHECKDB ([RRDA.Db]) WITH NO_INFOMSGS;
GO
```

Riavviare l'application pool soltanto dopo il completamento e la verifica delle
migrazioni.

### Autorizzazione dell'application pool

Dopo avere creato l'application pool `RRDA.Web` come descritto nella sezione
successiva, eseguire in SSMS:

```sql
USE [master];
GO
CREATE LOGIN [IIS APPPOOL\RRDA.Web] FROM WINDOWS;
GO

USE [RRDA.Db];
GO
CREATE USER [IIS APPPOOL\RRDA.Web] FOR LOGIN [IIS APPPOOL\RRDA.Web];
ALTER ROLE [db_datareader] ADD MEMBER [IIS APPPOOL\RRDA.Web];
ALTER ROLE [db_datawriter] ADD MEMBER [IIS APPPOOL\RRDA.Web];
GO
```

Se login o utente esistono già, non ricrearli. Non assegnare `sysadmin`,
`db_owner`, `db_ddladmin` o permessi di creazione database all'application pool.

I ruoli `db_datareader` e `db_datawriter` consentono all'applicazione di leggere e
aggiornare i dati, sincronizzare i tipi di report, gestire gli utenti applicativi
e scrivere gli eventi di audit.

### Backup SQL Express

SQL Server Express non include SQL Server Agent. Configurare un'attività in
Utilità di pianificazione che esegua un backup SQL con un account di servizio
autorizzato. Conservare i backup su una destinazione protetta e sottoporli alla
normale retention aziendale.

Concedere modifica sulla cartella di backup all'identità del servizio SQL Express:

```powershell
icacls D:\RRDA\Backup /grant "NT SERVICE\MSSQL`$SQLEXPRESS:(OI)(CI)(M)"
```

Esempio di comando da adattare:

```powershell
sqlcmd -S ".\SQLEXPRESS" -E -Q "BACKUP DATABASE [RRDA.Db] TO DISK = N'D:\RRDA\Backup\RRDA.Db.bak' WITH INIT, CHECKSUM"
```

Verificare periodicamente il ripristino del backup su un ambiente separato.

## 4. Pubblicazione dell'applicazione

La macchina di build, non il server RRDA, deve possedere repository, Visual
Studio con workload **ASP.NET and web development** e .NET SDK.

Sono supportate due modalità integrate in Visual Studio:

- **Web Deploy**: pubblicazione diretta sul sito IIS; richiede configurazione
  remota del Management Service e Web Deploy;
- **Web Deploy Package** o **Folder**: genera un pacchetto da trasferire e
  applicare separatamente; è preferibile quando la macchina di sviluppo non deve
  collegarsi direttamente al server.

Per un ambiente intranet controllato è ammessa la pubblicazione diretta Web
Deploy, purché l'endpoint di gestione sia accessibile soltanto dalla rete
amministrativa e l'account di deploy sia limitato al sito `RRDA.Web`.

La pubblicazione equivalente da riga di comando produce un pacchetto Release
framework-dependent:

```powershell
dotnet publish .\RRDA.Web\RRDA.Web.csproj -c Release -o .\artifacts\publish\RRDA.Web\Release
```

Il pacchetto pubblicato deve contenere almeno `RRDA.Web.dll`, `web.config`,
`appsettings.json`, dipendenze, viste e `wwwroot`.

Distribuire il contenuto pubblicato in `D:\RRDA\Web`. Il progetto esclude dal
publish `appsettings.Development.json` e la directory locale `RRDA.Web\artifacts`.
Prima della consegna verificare comunque che il pacchetto non contenga sorgenti,
directory `bin`, `obj`, profili di sviluppo o altri artefatti non previsti.

Distribuire separatamente in `D:\RRDA\Plugins`:

- file `RRDA.Plugins.*.dll` approvati;
- relativi file XML di validazione, quando previsti.

La separazione della cartella plugin dalla cartella applicazione evita la perdita
dei plugin durante un aggiornamento del sito.

### Configurazione server per Web Deploy da Visual Studio

Questa configurazione è necessaria soltanto per la pubblicazione diretta
**Web Deploy**. Non è necessaria usando un profilo **Folder** o generando un
**Web Deploy Package**.

1. Installare IIS e Management Service prima di installare Web Deploy.
2. Installare sul server una versione supportata di **Microsoft Web Deploy**
   includendo il **Web Deploy Handler**.
3. Aprire **IIS Manager**, selezionare il nodo server e aprire
   **Management Service**.
4. Abilitare **Enable remote connections**.
5. Usare credenziali Windows e avviare il servizio **Web Management Service
   (WMSVC)**.
6. Impostare l'avvio del servizio WMSVC su `Automatic`.
7. In IIS Manager, selezionare il sito `RRDA.Web` e usare
   **Deploy > Configure Web Deploy Publishing...** per autorizzare un account o
   gruppo di dominio dedicato al deploy. Per la connessione da Visual Studio
   usare comunque le credenziali di un account nominativo autorizzato.
8. Verificare in **Management Service Delegation** che la delega consenta la
   pubblicazione del solo sito `RRDA.Web`, senza privilegi amministrativi globali
   e senza provider di distribuzione database.
9. Conservare il file `.PublishSettings` generato in modo protetto e distribuirlo
   soltanto agli operatori di rilascio autorizzati.

Usare **Configure Web Deploy Publishing...** per creare delega e autorizzazioni
del sito. Non concedere manualmente all'account di deploy controllo completo su
IIS, sul server o su directory estranee a `D:\RRDA\Web`.

Non usare come account di pubblicazione l'identità dell'application pool e non
concedere all'account di deploy privilegi SQL. Le migrazioni database restano
un'operazione amministrativa separata.

Web Deploy Handler usa normalmente HTTPS sulla porta TCP `8172`. Creare una
regola firewall dedicata limitata alle sole subnet o postazioni amministrative:

```powershell
New-NetFirewallRule `
    -DisplayName "RRDA Web Deploy - rete amministrativa" `
    -Direction Inbound `
    -Action Allow `
    -Protocol TCP `
    -LocalPort 8172 `
    -RemoteAddress "10.20.10.0/24" `
    -Profile Domain
```

La porta `8172` non deve essere raggiungibile dalle normali reti client, da reti
guest o da Internet. Il certificato associato a WMSVC deve essere valido e
attendibile dalla macchina di pubblicazione.

Verificare la raggiungibilità dall'host di deploy:

```text
https://RRDA:8172/msdeploy.axd?site=RRDA.Web
```

Una risposta HTTP `401` senza credenziali dimostra che l'endpoint è raggiungibile
e richiede autenticazione; non deve rispondere da reti non autorizzate.

### Creazione del profilo Publish in Visual Studio

In Visual Studio:

1. Fare clic destro sul progetto `RRDA.Web` e scegliere **Publish**.
2. Scegliere **New profile > Web Server (IIS)**.
3. Selezionare **Web Deploy** per il deploy diretto oppure **Web Deploy Package**
   per produrre un pacchetto da applicare separatamente.
4. Per Web Deploy diretto impostare:

   | Campo | Valore |
   |---|---|
   | Server | `https://RRDA:8172/msdeploy.axd` |
   | Site name | `RRDA.Web` |
   | Destination URL | `https://rrda.example.local` |
   | Username | account nominativo di dominio autorizzato al deploy |

5. Usare **Validate Connection** e accettare soltanto certificati attendibili.
6. In **Settings**, impostare:
   - Configuration: `Release`;
   - Target Framework: `net8.0`;
   - Deployment mode: `Framework-dependent`;
   - Target Runtime: `Portable` oppure non specificato;
   - **Take App Offline**: abilitato, per evitare file bloccati o una versione
     applicativa parzialmente aggiornata durante il deploy;
   - **Remove additional files at destination**: disabilitato;
   - migrazioni o aggiornamenti database automatici: disabilitati.
7. Salvare il profilo con un nome esplicito, ad esempio `RRDA-Intranet-WebDeploy`.
8. Eseguire **Preview** prima di ogni pubblicazione e verificare i file che
   saranno aggiunti, aggiornati o eliminati.

Non salvare password nel profilo condiviso. Il repository ignora i file
`*.pubxml`; se in futuro si decide di versionare un profilo privo di segreti,
verificare comunque che non contenga credenziali, connection string sensibili o
impostazioni specifiche personali. Non distribuire file `*.pubxml.user` o
`.PublishSettings`.

La macchina dalla quale si pubblica deve essere una postazione amministrativa
gestita, con Visual Studio aggiornato e accesso alla sola rete di gestione. Non
abilitare Web Deploy direttamente dalle normali postazioni utente.

### Protezione della configurazione di produzione

La pubblicazione Visual Studio distribuisce `appsettings.json`, ma non deve
eliminare o sostituire `appsettings.Production.json`, che contiene la
configurazione specifica del server.

Per preservarlo:

- mantenere disabilitata l'opzione **Remove additional files at destination**;
- non aggiungere `appsettings.Production.json` al progetto o al profilo Publish;
- conservare una copia protetta prima di ogni rilascio;
- verificare il file dopo la pubblicazione e prima di riavviare il sito.

Il progetto esclude già `appsettings.Development.json` e la directory locale
`RRDA.Web\artifacts` dalla pubblicazione.

La pubblicazione Web Deploy non deve:

- creare o migrare `RRDA.Db`;
- distribuire `RRDA.Db.Migrations.sql` dentro la directory del sito;
- modificare i plugin in `D:\RRDA\Plugins`;
- modificare binding, certificati, autenticazione o ACL IIS senza una modifica
  infrastrutturale approvata.

### Sequenza di pubblicazione diretta

Prima di pubblicare da Visual Studio:

1. verificare backup database e disponibilità del pacchetto precedente;
2. applicare separatamente le eventuali migrazioni SQL;
3. verificare con **Preview** che `appsettings.Production.json` e file estranei
   non siano eliminati;
4. pubblicare con il profilo Release approvato e verificare che l'applicazione
   venga rimessa online al termine;
5. verificare Event Viewer, disponibilità HTTPS, autenticazione e funzionalità
   principali;
6. verificare che la porta `8172` resti limitata alla rete amministrativa.

### Deploy del pacchetto plugin

I plugin seguono un ciclo di rilascio separato dall'applicazione web. Il profilo
Visual Studio Web Deploy pubblica `D:\RRDA\Web` e non deve modificare la cartella
esterna `D:\RRDA\Plugins`.

RRDA.Web carica soltanto assembly con nome:

```text
RRDA.Plugins.*.dll
```

Per importare un report, richiede inoltre nella stessa cartella un file XML di
validazione denominato esattamente come il valore `Name` esposto dal plugin:

```text
<PluginName>.xml
```

Per esempio, se il plugin espone `Name = MAN_CIR`, devono essere distribuiti:

```text
RRDA.Plugins.MAN_CIR.dll
MAN_CIR.xml
```

La DLL consente il caricamento e il riconoscimento del tipo di report; il file
XML contiene la configurazione di validazione usata durante l'importazione. Un
plugin privo del relativo XML può comparire nel catalogo, ma le importazioni
associate vengono rifiutate.

#### Composizione del pacchetto

Preparare sulla macchina di build un pacchetto versionato, per esempio:

```text
RRDA.Plugins-1.2.3\
  manifest.txt
  sha256.txt
  RRDA.Plugins.ALI.dll
  ALI.xml
  RRDA.Plugins.MAN_CIR.dll
  MAN_CIR.xml
  ...
```

Includere esclusivamente:

- DLL plugin Release approvate `RRDA.Plugins.*.dll`;
- un XML di validazione approvato per ogni plugin;
- un manifest con versione pacchetto, data, elenco plugin e relative versioni;
- hash SHA-256 dei file consegnati.

Non includere:

- file `.pdb`, `.deps.json`, `postbuild.log` o output Debug;
- `RRDA.Core.dll`, `RRDA.Data.dll` o `RRDA.Plugins.Common.dll`, già distribuiti
  con RRDA.Web;
- file di configurazione applicativa o script database;
- DLL non approvate o dipendenze aggiuntive non verificate.

Se una futura versione di un plugin introduce una nuova dipendenza non già
distribuita con RRDA.Web, non copiarla automaticamente nella cartella plugin:
verificare compatibilità e modalità di caricamento in collaudo, quindi includerla
in un rilascio applicativo approvato.

Le DLL Release vengono prodotte nella cartella condivisa:

```text
artifacts\plugins\Release\net8.0
```

Per produrre gli artefatti tramite Visual Studio:

1. selezionare configurazione `Release`;
2. compilare i progetti plugin approvati oppure il progetto
   `RRDA.RepImp.Setup`, che compila l'insieme dei plugin distribuibili;
3. verificare che tutte le build siano terminate senza errori;
4. prelevare dalla cartella condivisa soltanto le DLL previste dal rilascio.

Il contenuto di tale cartella è un output di build e non costituisce direttamente
il pacchetto distribuibile: contiene anche file tecnici che devono essere esclusi.

Esempio per calcolare gli hash del pacchetto preparato:

```powershell
$package = "D:\Release\RRDA.Plugins-1.2.3"
Get-ChildItem $package -File |
    Where-Object Name -NotIn @("sha256.txt") |
    Get-FileHash -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($_.Path))" } |
    Set-Content "$package\sha256.txt"
```

Prima della consegna verificare su un ambiente di collaudo:

- assenza di plugin con `Name` duplicato;
- corrispondenza tra ogni `PluginName` e relativo file XML;
- caricamento senza errori;
- riconoscimento e importazione di un report campione per ogni plugin modificato.

#### Copia del pacchetto sul server

Copiare il pacchetto approvato in una directory di staging non accessibile
dall'application pool, per esempio:

```text
D:\RRDA\Deploy\Plugins\RRDA.Plugins-1.2.3
```

Verificare gli hash ricevuti prima del deploy:

```powershell
Get-ChildItem "D:\RRDA\Deploy\Plugins\RRDA.Plugins-1.2.3" -File |
    Get-FileHash -Algorithm SHA256
```

Confrontare il risultato con `sha256.txt`; interrompere il deploy in caso di
differenze o file inattesi.

Il deploy deve essere eseguito da un amministratore o da un account di rilascio
autorizzato alla modifica di `D:\RRDA\Plugins`.

Procedura:

1. Registrare plugin e versioni attualmente visibili in
   **Plugin > Catalogo plugin**.
2. Creare una copia di rollback della cartella corrente:

   ```powershell
   $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
   Copy-Item "D:\RRDA\Plugins" "D:\RRDA\Deploy\Plugins\Backup-$timestamp" -Recurse
   ```

3. Arrestare l'application pool:

   ```powershell
   Import-Module WebAdministration
   Stop-WebAppPool -Name "RRDA.Web"
   ```

4. Copiare nella cartella `D:\RRDA\Plugins` soltanto le DLL e gli XML approvati:

   ```powershell
   $package = "D:\RRDA\Deploy\Plugins\RRDA.Plugins-1.2.3"
   Copy-Item "$package\RRDA.Plugins.*.dll" "D:\RRDA\Plugins" -Force
   Copy-Item "$package\*.xml" "D:\RRDA\Plugins" -Force
   ```

5. Rimuovere eventuali plugin ritirati soltanto se la rimozione è esplicitamente
   prevista dal piano di rilascio. Non usare sincronizzazioni automatiche con
   eliminazione indiscriminata.
6. Verificare che le ACL della cartella siano rimaste invariate:

   ```powershell
   icacls D:\RRDA\Plugins
   ```

7. Avviare l'application pool:

   ```powershell
   Start-WebAppPool -Name "RRDA.Web"
   ```

È necessario arrestare o riciclare l'application pool per sostituire DLL già
caricate. Il comando web **Ricarica e sincronizza** rilegge il catalogo e
sincronizza `ReportTypes`, ma non sostituisce in memoria assembly già caricati.

#### Verifica post-deploy

Accedere con ruolo Admin e aprire **Plugin > Catalogo plugin**:

1. verificare che la cartella mostrata sia `D:\RRDA\Plugins`;
2. selezionare **Ricarica e sincronizza**;
3. verificare numero, nomi e versioni dei plugin;
4. verificare che non siano presenti errori di caricamento;
5. verificare che non risultino tipi di report senza plugin;
6. eseguire un'importazione campione per ogni plugin aggiornato;
7. controllare Event Viewer e audit applicativo.

La sincronizzazione può inserire o aggiornare i record `ReportTypes`, ma non
elimina automaticamente i tipi relativi a plugin rimossi. La rimozione di un
plugin e l'eventuale gestione del relativo `ReportType` devono essere valutate
separatamente, considerando i dati storici presenti.

#### Rollback plugin

In caso di errore:

1. arrestare l'application pool `RRDA.Web`;
2. rimuovere esplicitamente i nuovi file introdotti dal pacchetto fallito,
   usando il relativo manifest;
3. ripristinare l'intero contenuto della copia `Backup-<timestamp>`;
4. avviare l'application pool;
5. usare **Ricarica e sincronizza**;
6. ripetere il controllo del catalogo e un'importazione campione;
7. registrare l'esito e conservare log e pacchetto che ha causato il problema.

### ACL NTFS

Applicare permessi conservativi:

- amministratori e account di deploy: modifica su `D:\RRDA\Web` e
  `D:\RRDA\Plugins`;
- `IIS APPPOOL\RRDA.Web`: sola lettura ed esecuzione su `D:\RRDA\Web` e
  `D:\RRDA\Plugins`;
- nessun permesso di scrittura dell'application pool sulle cartelle di deploy.

Esempio:

```powershell
icacls D:\RRDA\Web /inheritance:r
icacls D:\RRDA\Web /grant:r "SYSTEM:(OI)(CI)(F)" "Administrators:(OI)(CI)(F)" "IIS APPPOOL\RRDA.Web:(OI)(CI)(RX)"
icacls D:\RRDA\Plugins /inheritance:r
icacls D:\RRDA\Plugins /grant:r "SYSTEM:(OI)(CI)(F)" "Administrators:(OI)(CI)(F)" "IIS APPPOOL\RRDA.Web:(OI)(CI)(RX)"
```

Adattare i gruppi amministrativi e di deploy alle convenzioni aziendali prima di
rimuovere l'ereditarietà.

## 5. Configurazione IIS

### Application pool

Creare un application pool denominato `RRDA.Web`:

- **.NET CLR version**: `No Managed Code`;
- **Managed pipeline mode**: `Integrated`;
- **Enable 32-Bit Applications**: `False`;
- **Identity**: `ApplicationPoolIdentity`;
- **Start Mode**: `AlwaysRunning`;
- **Idle Time-out**: `0` oppure valore concordato con l'esercizio;
- riciclo pianificato soltanto in una finestra di manutenzione.

### Sito

Creare il sito `RRDA.Web`:

- physical path: `D:\RRDA\Web`;
- application pool: `RRDA.Web`;
- binding HTTPS `443` con hostname/FQDN interno approvato;
- certificato TLS valido;
- nessun binding HTTP `80`.

L'applicazione in ambiente Production abilita HSTS e redirect HTTPS. Un binding
HTTPS funzionante è quindi obbligatorio.

### Limitazione alla rete intranet

Applicare la limitazione innanzitutto sul firewall Windows. Esempio da adattare
alle subnet intranet effettive:

```powershell
New-NetFirewallRule `
    -DisplayName "RRDA.Web HTTPS intranet" `
    -Direction Inbound `
    -Action Allow `
    -Protocol TCP `
    -LocalPort 443 `
    -RemoteAddress "10.20.0.0/16","10.30.40.0/24" `
    -Profile Domain
```

Verificare che non esistano altre regole più ampie che consentano l'accesso alla
porta `443` da qualunque rete. Il firewall perimetrale non deve pubblicare la VM.

Come controllo aggiuntivo è possibile installare la funzionalità IIS **IP and
Domain Restrictions** e autorizzare soltanto le subnet intranet, mantenendo il
firewall come controllo principale.

Distribuire tramite Group Policy il FQDN del sito nella zona browser **Local
Intranet**, così l'autenticazione Windows integrata può avvenire senza richieste
di credenziali inattese.

### Autenticazione

Nella configurazione **Authentication** del sito:

- **Anonymous Authentication**: `Disabled`;
- **Windows Authentication**: `Enabled`.

Nei provider di Windows Authentication mantenere `Negotiate` prima di `NTLM`.
Non abilitare Basic Authentication.

### Logging Event Viewer

In Production l'applicazione scrive warning ed errori nel registro
`Application`, sorgente `RRDA.Web`. Creare preventivamente la sorgente da
PowerShell amministrativa:

```powershell
if (-not [System.Diagnostics.EventLog]::SourceExists("RRDA.Web")) {
    New-EventLog -LogName Application -Source "RRDA.Web"
}
```

## 6. Configurazione applicativa

Creare sul server `D:\RRDA\Web\appsettings.Production.json`:

```json
{
  "AllowedHosts": "rrda.example.local;RRDA",
  "ConnectionStrings": {
    "DefaultConnection": "Server=.\\SQLEXPRESS;Database=RRDA.Db;Trusted_Connection=True;MultipleActiveResultSets=true;Encrypt=True;TrustServerCertificate=True"
  },
  "Plugins": {
    "Folder": "D:\\RRDA\\Plugins"
  }
}
```

Non copiare `appsettings.Development.json` e non impostare
`ASPNETCORE_ENVIRONMENT=Development` sul server.

La connection string usa Windows Integrated Security. L'account SQL effettivo è
`IIS APPPOOL\RRDA.Web`; gli utenti browser non devono ricevere login SQL.

## 7. Bootstrap del primo amministratore

RRDA.Web autorizza gli utenti tramite la tabella `AppUsers`. L'autenticazione
Windows, da sola, non concede accesso applicativo.

Per creare il primo amministratore, aggiungere temporaneamente a
`appsettings.Production.json`:

```json
{
  "BootstrapAdmin": {
    "WindowsUsername": "DOMINIO\\nomeutente"
  }
}
```

Integrare questa sezione nel JSON esistente, riciclare l'application pool e
verificare nel registro eventi che il bootstrap sia riuscito. Al primo avvio
senza altri Admin, l'applicazione crea o promuove l'utente indicato e registra
l'operazione nell'audit.

Subito dopo:

1. rimuovere la sezione `BootstrapAdmin`;
2. riciclare nuovamente l'application pool;
3. accedere con il primo Admin;
4. creare gli altri utenti dall'area **Amministrazione > Utenti**.

Non usare un comando PowerShell del tipo:

```powershell
$env:RRDA_BootstrapAdmin__WindowsUsername="DOMINIO\nomeutente"
```

La variabile così impostata vale soltanto per il processo PowerShell corrente e
non viene ereditata dal processo IIS già in esecuzione.

I ruoli applicativi disponibili sono:

- `Operator`: consultazione ed esportazione;
- `Supervisor`: operazioni sui dati;
- `Admin`: amministrazione utenti, plugin, tipi di report e audit.

Gli username devono corrispondere esattamente al formato restituito
dall'autenticazione Windows, normalmente `DOMINIO\utente`.

## 8. Avvio e collaudo

1. Avviare sito e application pool.
2. Verificare che `https://rrda.example.local` risponda con certificato valido.
3. Verificare che il browser autentichi l'utente Windows senza richiesta di
   credenziali inattesa.
4. Accedere con il primo Admin.
5. Controllare **Plugin** e verificare caricamento e sincronizzazione dei tipi di
   report.
6. Creare un utente Operator di prova e verificarne i limiti.
7. Eseguire un'importazione e una consultazione di prova.
8. Controllare Event Viewer e tabella di audit.
9. Eseguire e verificare il primo backup del database.
10. Verificare da una rete non autorizzata che la porta HTTPS del server non sia
    raggiungibile.

Errori tipici:

| Sintomo | Verifica |
|---|---|
| HTTP 500.30 / applicazione non avviata | Event Viewer, Hosting Bundle, configurazione JSON |
| Redirect continuo o sito non raggiungibile | binding HTTPS e certificato |
| HTTP 401 | Windows Authentication abilitata, Anonymous disabilitata, DNS/intranet |
| Accesso negato dopo autenticazione | utente assente o disabilitato in `AppUsers` |
| Errore SQL Login failed | login `IIS APPPOOL\RRDA.Web` e mapping su `RRDA.Db` |
| Plugin non caricati | percorso `Plugins:Folder`, ACL e file distribuiti |

## 9. Aggiornamento applicativo

Prima di ogni aggiornamento:

1. eseguire un backup verificabile di `RRDA.Db`;
2. conservare il pacchetto applicativo precedente;
3. arrestare o mettere offline il sito;
4. eseguire `RRDA.Db.Migrations.sql` sul database con un account amministrativo;
5. sostituire il contenuto di `D:\RRDA\Web`, preservando
   `appsettings.Production.json`;
6. se previsti dal rilascio, distribuire i plugin usando la procedura
   **Deploy del pacchetto plugin** e il relativo pacchetto separato;
7. avviare il sito ed eseguire il collaudo.

Non concedere all'application pool permessi per applicare automaticamente
migrazioni o modificare la propria directory di installazione.

## 10. Monitoraggio operativo

Controllare periodicamente:

- disponibilità HTTPS e scadenza certificato;
- stato application pool e servizio SQL Server Express;
- Event Viewer, sorgente `RRDA.Web`;
- spazio libero del volume dati;
- dimensione e crescita del database;
- esito e ripristinabilità dei backup;
- aggiornamenti Windows, Hosting Bundle e SQL Server Express;
- utenti abilitati e ruoli applicativi;
- plugin caricati e relativi errori.
