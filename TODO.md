# TODO

1. gestione collegamenti/navigazione:
   - importazione o inserimento manuale carta di identità radar,
   - RADAR/COMPONENTE,
   - RADAR/SOTTOASSIEME (?)
   - SOTTOASSIEME/COMPONENTE.
2. rimuovere plugin "Dummy"
3. ottimizzazione query statistiche (su TabularController.cs)
4. cancellazione massiva report (per tipo?)
5. semplificazione struttura database (es. ReportProperties?):
   - ImportResultRepository.SaveAsync non filtra le chiavi interne (verifica prompt con Codex)
   - spostare Unit e IsSubjectKey in ReportEntities?
   - eliminare ridondanza tra valori memorizzati in ReportProperties e campi di ReportProperties stessa
6. Estendere la suite di test con almeno un test di integrazione per la pipeline import e uno per TabularController.TypePivot.