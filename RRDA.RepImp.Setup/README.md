# RRDA.RepImp.Setup

Progetto setup MSI per l'applicazione desktop `RRDA.RepImp`, basato su WiX Toolset SDK-style e gestibile da Visual Studio 2026 tramite FireGiant HeatWave Community Edition.

## Prerequisiti di sviluppo

- Visual Studio 2026 con workload .NET Desktop Development.
- Estensione FireGiant HeatWave Community Edition per Visual Studio 2026.
- Accesso a NuGet per ripristinare `WixToolset.Sdk` e `WixToolset.UI.wixext`.

## Build da Visual Studio

1. Aprire `RRDA.sln`. HeatWave non riconosce correttamente il progetto WiX dalla soluzione `RRDA.slnx`.
2. Ripristinare i pacchetti NuGet.
3. Impostare configurazione `Release` e piattaforma `x64`.
4. Compilare il progetto `RRDA.RepImp.Setup`.

Il progetto setup pubblica automaticamente `RRDA.RepImp` come applicazione self-contained `win-x64` e include tutti i file pubblicati nel pacchetto MSI.

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
- Vengono creati collegamenti nel menu Start e sul desktop.
- Il runtime .NET Desktop è incluso perché la pubblicazione predefinita è self-contained (`SelfContained=true`).
