# RRDA.RepImp.UserSetup

MSI per-user per `RRDA.RepImp`, pensato per installazione senza privilegi
amministrativi.

Il pacchetto installa l'applicazione nel profilo dell'utente corrente:

```text
%LOCALAPPDATA%\IDS\RRDA RepImp
```

Contenuto installato:

- applicazione desktop self-contained `win-x64`;
- plugin in `%LOCALAPPDATA%\IDS\RRDA RepImp\plugins`;
- script SQL in `%LOCALAPPDATA%\IDS\RRDA RepImp\Database`;
- collegamenti nel menu Start e sul desktop dell'utente.

Il setup per-user e il setup per-machine sono pacchetti distinti e hanno
`UpgradeCode` diversi. Evitare di installare entrambi per lo stesso utente, per
non creare confusione nei collegamenti e nelle impostazioni.

Build:

```powershell
dotnet build .\RRDA.RepImp.UserSetup\RRDA.RepImp.UserSetup.wixproj -c Release
```

Output:

```text
RRDA.RepImp.UserSetup\bin\x64\Release\RRDA.RepImp.UserSetup.msi
```
