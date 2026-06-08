# TODO

1. gestione/visualizzazione valori 'ranged'
2. gestione collegamenti/navigazione:
   - importazione carta di identità radar,
   - RADAR/COMPONENTE,
   - RADAR/SOTTOASSIEME (?)
   - SOTTOASSIEME/COMPONENTE.
3. implementare plugin mancanti (TC...)
   - rimuovere plugin "Dummy"
4. ottimizzazione query statistiche
   - possibile ricorso alla parallelizzazione in TabularController (verifica prompt con Codex)
5. cancellazione massiva report SOLO tramite applicazione web (per batch, per tipo?):
6. semplificazione struttura database (es. ReportProperties?):
   - ImportResultRepository.SaveAsync non filtra le chiavi interne (verifica prompt con Codex)
   - spostare Unit e IsSubjectKey in ReportEntities?
   - eliminare ridondanza tra valori memorizzati in ReportProperties e campi di ReportProperties stessa
7. Estendere la suite di test con almeno un test di integrazione per la pipeline import e uno per TabularController.TypePivot.