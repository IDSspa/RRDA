# TODO
1. Semplificazione struttura database (es. ReportProperties?):
   - ImportResultRepository.SaveAsync non filtra le chiavi interne (verifica prompt con Codex)
   - spostare Unit e IsSubjectKey in ReportEntities?
   - eliminare ridondanza tra valori memorizzati in ReportProperties e campi di ReportProperties stessa
2. Estendere la suite di test con almeno un test di integrazione per la pipeline import e uno per TabularController.TypePivot.
3. Gli errori di importazione dovrebbero riferire il definedName/Alias oggetto dell'errore.
4. Meno messaggi in console.
5. Gestione dei validatori.
6. RRDA.Web spazio accessibile per download: plugin, validatori, installer RepImp, ecc.
7. Ottimizzazione query statistiche (su TabularController.cs)
8. Cancellazione massiva report (per tipo?)
9. Grafici PDF: se vengono selezionate più grandezze, viene generato un grafico per ogni grandezza.
10. Gestione HEADER cliccabile su colonne di relazione.
