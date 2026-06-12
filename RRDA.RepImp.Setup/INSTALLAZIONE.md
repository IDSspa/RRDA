# Installazione e configurazione di RRDA RepImp

## Scopo

Questo documento descrive l'installazione di **RRDA RepImp** tramite il pacchetto
MSI prodotto dal progetto `RRDA.RepImp.Setup` e la configurazione del collegamento
a un database SQL Server remoto o a SQL Server Express locale.

> L'MSI installa applicazione, plugin, file XML di configurazione e script SQL,
> ma non installa SQL Server Express e non esegue automaticamente lo script sul
> database.

## Contenuto del setup

La procedura di setup è definita in:

- `RRDA.RepImp.Setup/Package.wxs`: contenuto e struttura dell'MSI;
- `RRDA.RepImp.Setup/RRDA.RepImp.Setup.wixproj`: pubblicazione
  self-contained `win-x64`, compilazione plugin e generazione dello script SQL;
- `RRDA.Data/Migrations`: migrazioni Entity Framework Core usate per generare lo
  script SQL idempotente.

L'installazione è per-machine, richiede privilegi amministrativi e usa come
destinazione predefinita:

```text
C:\Program Files\IDS\RRDA RepImp
```

| Contenuto | Destinazione predefinita |
|---|---|
| Applicazione e file XML comuni | `C:\Program Files\IDS\RRDA RepImp` |
| Plugin | `C:\Program Files\IDS\RRDA RepImp\plugins` |
| Validatori creati dall'utente | `%LOCALAPPDATA%\IDS\RRDA RepImp\validators` |
| Script database | `C:\Program Files\IDS\RRDA RepImp\Database\RRDA.Db.Migrations.sql` |

### UnitMappings.xml

Il file viene distribuito dall'MSI nella cartella principale dell'applicazione:

```text
C:\Program Files\IDS\RRDA RepImp\UnitMappings.xml
```

Contiene regole basate sui nomi definiti dei file Excel. Durante la generazione
dei file di validazione, RRDA RepImp usa queste regole per inferire l'unità di
misura dei campi numerici, per esempio `V`, `A`, `mV` o `dB`.

Nelle impostazioni il valore predefinito è `UnitMappings.xml`. Essendo un percorso
relativo, viene risolto rispetto alla cartella di installazione dell'applicazione.

### ImportBanList.xml

Il file viene distribuito dall'MSI nella cartella principale dell'applicazione:

```text
C:\Program Files\IDS\RRDA RepImp\ImportBanList.xml
```

Contiene pattern di nomi definiti e fogli Excel da escludere durante la
generazione dei file di validazione, ad esempio nomi riservati di Excel, aree di
stampa o fogli non destinati all'importazione.

Nelle impostazioni il valore predefinito è `ImportBanList.xml`. Anche questo
percorso relativo viene risolto rispetto alla cartella di installazione.

I due file possono essere sostituiti con versioni personalizzate tramite
**Impostazioni**. È consigliabile conservare le personalizzazioni fuori da
`Program Files`, per evitare che un aggiornamento dell'MSI le sovrascriva.

## Prerequisiti

- Windows a 64 bit;
- privilegi amministrativi per eseguire l'MSI;
- accesso in lettura alle cartelle contenenti i report;
- accesso in lettura e scrittura al database RRDA;
- per il database locale: SQL Server Express e SQL Server Management Studio
  (SSMS).

Non è necessario installare separatamente il runtime .NET Desktop: RRDA RepImp
viene distribuito come applicazione self-contained.

## Installazione tramite MSI

1. Chiudere eventuali versioni di RRDA RepImp già in esecuzione.
2. Eseguire `RRDA.RepImp.Setup.msi`.
3. Accettare la licenza e confermare la cartella di destinazione.
4. Completare l'installazione.
5. Avviare RRDA RepImp dal menu Start o dal collegamento sul desktop.

## Configurazione iniziale

Aprire **Impostazioni** e verificare:

- **Cartella Reports**: cartella radice contenente i file da importare;
- **Profondità ricorsione**: `0` per la sola cartella selezionata oppure un
  intero maggiore per includere sottocartelle;
- **Cartella Plugins**: lasciare vuota per usare la sottocartella `plugins`
  installata dall'MSI;
- **Cartella Validatori**: il valore predefinito è
  `%LOCALAPPDATA%\IDS\RRDA RepImp\validators`; scegliere un'altra cartella per
  modificare la destinazione dei file XML generati;
- **Mapping unità**: lasciare `UnitMappings.xml`, salvo personalizzazioni;
- **Banlist importazione**: lasciare `ImportBanList.xml`, salvo personalizzazioni;
- **ConnectionString**: stringa di connessione al database RRDA.

Le impostazioni vengono salvate separatamente per ciascun utente Windows.

## Collegamento a un database remoto

Richiedere all'amministratore SQL Server il nome del server e dell'istanza, il
nome del database e le autorizzazioni. Un esempio con autenticazione Windows è:

```text
Server=NOME-SERVER\ISTANZA;Database=RRDA.Db;Trusted_Connection=True;MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=True
```

Il database remoto deve essere già creato e aggiornato con lo script
`RRDA.Db.Migrations.sql` distribuito dall'MSI.

## Predisposizione di SQL Server Express locale

### 1. Installare SQL Server Express e SSMS

Installare SQL Server Express includendo **Database Engine Services** e usando
l'istanza denominata `SQLEXPRESS`. Installare inoltre **SQL Server Management
Studio (SSMS)**, necessario per eseguire lo script e configurare gli accessi.

Da **Servizi di Windows**, verificare che sia avviato:

```text
SQL Server (SQLEXPRESS)
```

Per il solo accesso locale non è normalmente necessario abilitare SQL Server
Browser, TCP/IP o regole firewall.

### 2. Creare il database con SSMS

Queste operazioni devono essere eseguite da un amministratore dell'istanza SQL:

1. Avviare **SQL Server Management Studio**.
2. Collegarsi a `.\SQLEXPRESS` con **Windows Authentication**.
3. In **Object Explorer**, fare clic destro su **Databases** e scegliere
   **New Database...**.
4. Impostare **Database name** a `RRDA.Db` e confermare con **OK**.
5. Aprire **File > Open > File...** e selezionare:

   ```text
   C:\Program Files\IDS\RRDA RepImp\Database\RRDA.Db.Migrations.sql
   ```

6. Nella barra degli strumenti selezionare il database `RRDA.Db`.
7. Premere **Execute** e verificare che l'esecuzione termini senza errori.

Lo script è idempotente: può essere eseguito sia su un database vuoto sia su un
database RRDA esistente. Applica soltanto le migrazioni non ancora registrate
nella tabella `__EFMigrationsHistory`.

### 3. Autorizzare gli utenti Windows con SSMS

Per ogni utente Windows che deve usare RRDA RepImp:

1. In SSMS, collegarsi a `.\SQLEXPRESS` con un account amministratore.
2. Espandere **Security > Logins**.
3. Fare clic destro su **Logins** e scegliere **New Login...**.
4. In **Login name**, usare **Search...** per selezionare l'utente o,
   preferibilmente, un gruppo Windows dedicato.
5. Lasciare selezionato **Windows authentication**.
6. Aprire la pagina **User Mapping**.
7. Selezionare il database `RRDA.Db`.
8. Nella sezione **Database role membership for: RRDA.Db**, selezionare:
   - `db_datareader`;
   - `db_datawriter`.
9. Confermare con **OK**.

I ruoli `db_datareader` e `db_datawriter` sono sufficienti per il normale uso di
RRDA RepImp. Non assegnare `db_owner` agli utenti operativi. L'account che crea
il database ed esegue le migrazioni deve invece essere amministratore
dell'istanza o proprietario del database.

### 4. Configurare RRDA RepImp

In **Impostazioni**, impostare la seguente connection string:

```text
Server=.\SQLEXPRESS;Database=RRDA.Db;Trusted_Connection=True;MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=True
```

Salvare e riavviare l'applicazione.

> La configurazione iniziale dell'applicazione punta a
> `(localdb)\MSSQLLocalDB`. LocalDB e `.\SQLEXPRESS` sono istanze differenti:
> per utilizzare SQL Server Express occorre sostituire la connection string.

## Produzione dello script SQL

Lo script distribuito non viene mantenuto manualmente. Durante la compilazione di
`RRDA.RepImp.Setup`, il target `GenerateDatabaseScript` esegue:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations script --idempotent --configuration Release --project .\RRDA.Data\RRDA.Data.csproj --startup-project .\RRDA.RepImp\RRDA.RepImp.csproj --output .\artifacts\database\Release\RRDA.Db.Migrations.sql
```

Il file generato viene incluso automaticamente nell'MSI e installato nella
sottocartella `Database`. La versione `9.0.11` di `dotnet-ef` è definita nel
manifest locale `.config/dotnet-tools.json` e viene ripristinata automaticamente
durante la compilazione del setup.

## Verifica finale

1. Avviare RRDA RepImp con un utente autorizzato.
2. Verificare che i plugin risultino caricati.
3. Selezionare una cartella Reports accessibile.
4. Eseguire un'importazione di prova.
5. Verificare l'assenza di errori di connessione, autorizzazione o schema.

In caso di problemi controllare il servizio `SQL Server (SQLEXPRESS)`, la
connection string, l'esistenza di `RRDA.Db`, l'esecuzione dello script SQL e il
mapping dell'utente Windows.

## Disinstallazione

La disinstallazione rimuove applicazione, plugin, file XML comuni, script SQL e
collegamenti installati dall'MSI. Non rimuove SQL Server Express, il database
`RRDA.Db`, le cartelle Reports o le impostazioni utente.
