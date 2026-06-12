# TODO
1. il file ValidationConfig.xsd deve essere distribuito con RepImp ed inserito localmente ai validatori 
   (o l'xml del validatore impostato per usare l'xsd altrove)
2. gestione collegamenti/navigazione:
   - importazione o inserimento manuale carta di identità radar,
   - RADAR/COMPONENTE,
   - RADAR/SOTTOASSIEME (?)
   - SOTTOASSIEME/COMPONENTE.
3. rimuovere plugin "Dummy"
4. ottimizzazione query statistiche (su TabularController.cs)
5. cancellazione massiva report (per tipo?)
6. semplificazione struttura database (es. ReportProperties?):
   - ImportResultRepository.SaveAsync non filtra le chiavi interne (verifica prompt con Codex)
   - spostare Unit e IsSubjectKey in ReportEntities?
   - eliminare ridondanza tra valori memorizzati in ReportProperties e campi di ReportProperties stessa
7. Estendere la suite di test con almeno un test di integrazione per la pipeline import e uno per TabularController.TypePivot.
8. Gli errori di importazione dovrebbero riferire il campo oggetto dell'errore
9. Meno messaggi in console.