# RRDA.RepImp.Setup

Progetto setup MSI per l'applicazione desktop `RRDA.RepImp`, basato su WiX Toolset SDK-style e gestibile da Visual Studio 2026 tramite FireGiant HeatWave Community Edition.

## Prerequisiti di sviluppo

- Visual Studio 2026 con workload .NET Desktop Development.
- Estensione FireGiant HeatWave Community Edition per Visual Studio 2026.
- Accesso a NuGet per ripristinare `WixToolset.Sdk` e `WixToolset.UI.wixext`.
- Accesso a NuGet per ripristinare il tool locale `dotnet-ef` definito in
  `.config/dotnet-tools.json`.

## Build da Visual Studio

1. Aprire `RRDA.sln`. HeatWave non riconosce correttamente il progetto WiX dalla soluzione `RRDA.slnx`.
2. Ripristinare i pacchetti NuGet.
3. Impostare configurazione `Release` e piattaforma `x64`.
4. Compilare il progetto `RRDA.RepImp.Setup`.

Il progetto setup pubblica automaticamente `RRDA.RepImp` come applicazione self-contained `win-x64`, compila i plugin distribuibili, genera lo script SQL idempotente dalle migrazioni Entity Framework Core e include tutti questi elementi nel pacchetto MSI.

Il file distribuibile viene generato in:

```text
RRDA.RepImp.Setup\bin\x64\Release\RRDA.RepImp.Setup.msi
```

Il progetto `RRDA.RepImp` dichiara `win-x64` tra i runtime supportati, in modo che
il ripristino automatico eseguito da Visual Studio includa le risorse necessarie
alla pubblicazione self-contained. Dopo modifiche ai file di progetto, attendere
il completamento del ripristino NuGet prima di eseguire **Ricompila**.

## Build da riga di comando

```powershell
msbuild RRDA.RepImp.Setup\RRDA.RepImp.Setup.wixproj /p:Configuration=Release /p:Platform=x64
```

È possibile sovrascrivere la versione dell'MSI senza modificare i sorgenti:

```powershell
msbuild RRDA.RepImp.Setup\RRDA.RepImp.Setup.wixproj /p:Configuration=Release /p:Platform=x64 /p:InstallerVersion=1.2.3
```

## Note

- L'installer è per-machine e installa l'applicazione in `Program Files\IDS\RRDA RepImp`.
- I plugin vengono installati nella sottocartella `Program Files\IDS\RRDA RepImp\plugins`.
- I validatori creati dall'applicazione vengono salvati in
  `%LOCALAPPDATA%\IDS\RRDA RepImp\validators`, scrivibile dall'utente standard.
  La destinazione può essere modificata nelle impostazioni di RepImp.
- L'MSI supporta l'aggiornamento e la reinstallazione anche quando il numero di
  versione del nuovo pacchetto coincide con quello già installato.
- Lo script SQL viene installato nella sottocartella `Program Files\IDS\RRDA RepImp\Database`.
- `UnitMappings.xml` e `ImportBanList.xml` vengono installati nella cartella principale dell'applicazione.
- Vengono creati collegamenti nel menu Start e sul desktop.
- Il runtime .NET Desktop è incluso perché la pubblicazione predefinita è self-contained (`SelfContained=true`).
