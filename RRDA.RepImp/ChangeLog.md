# Changelog — RRDA RepImp

Questo documento raccoglie le modifiche rilevanti per gli utenti di RRDA
RepImp. Le voci storiche sono state ricostruite dai tag e dalla cronologia Git
disponibili nel repository.

## 1.0.5.0 — 2026-10-08

### Modificato

- Rinnovata la navigazione delle cartelle dei report: la cartella configurata
  in `ReportsFolder` è ora la cartella iniziale e l'utente può entrare nelle
  sottocartelle con doppio clic o risalire alla cartella superiore.
- La selezione o navigazione delle cartelle non avvia più automaticamente la
  scansione dei report.
- Aggiunto il comando esplicito **Riscansiona**, disponibile sia nella barra
  della navigazione sia nel menu contestuale.
- La profondità ricorsiva è ora controllata direttamente nel navigatore: il
  valore predefinito è `0` (solo la cartella corrente), viene reimpostato a
  `0` quando cambia la cartella e una sua modifica avvia una nuova scansione.
- L'elenco di navigazione esclude le cartelle contrassegnate come nascoste in
  Windows.

### Corretto

- Corretti i commenti interni che attribuivano erroneamente a `PluginsFolder`
  il comportamento relativo a `ReportsFolder`; i due percorsi restano
  indipendenti.

## 1.0.3 — 2026-10-05

### Aggiunto

- Supporto all'importazione di celle non associate a un `definedName`, tramite
  il tag `Range` e l'attributo `alias` del tag `Map`.

## 1.0.1 — 2026-10-03

### Corretto

- Corrette le impostazioni della stringa di connessione in `secrets.json`.
- Spostata e rinominata la factory del contesto RRDA nell'applicazione RepImp.

## Note sulla tracciabilità delle versioni

Nel repository sono presenti i tag `repImp-1.0.1` e `repImp-1.0.3`. Non sono
stati trovati tag o note di rilascio affidabili per una versione `1.0.2` o
`1.0.4`; tali versioni non sono quindi riportate per non introdurre dati non
verificabili.
