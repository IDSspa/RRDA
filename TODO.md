# TODO

1. gestione/visualizzazione valori 'ranged'
2. gestione collegamenti/navigazione:
   - importazione carta di identità radar,
   - RADAR/COMPONENTE,
   - RADAR/SOTTOASSIEME (?)
   - SOTTOASSIEME/COMPONENTE.
3. implementare plugin mancanti (TC...)
4. ottimizzazione query statistiche
5. accesso 'veloce' alla vista tabellare per ogni tipologia di report da sidebar/dashboard
6. cancellazione massiva report SOLO tramite applicazione web (per batch, per tipo?):
7. semplificazione struttura database (es. ReportProperties?):
   - spostare Unit e IsSubjectKey in ReportEntities?
   - eliminare ridondanza tra valori memorizzati in ReportProperties e campi di ReportProperties stessa
8. rimozione codice non utilizzato
9. Estrarre ImportResultRepository in un'interfaccia iniettabile e rimuovere la reflection su ImportResult da MainWindow.
10. Suddividere TabularController in servizi dedicati, anche parzialmente, partendo dalla logica plot che è la più isolata.
11. Implementare un IHostedService o un job schedulato per la pulizia delle sessioni tabellari scadute, 
	oppure rimuovere l'intera infrastruttura TabularSession se non è pianificata a breve.
12. Sostituire i ViewBag con dynamic nelle view con ViewModel tipizzati.
13. Estendere la suite di test con almeno un test di integrazione per la pipeline import e uno per TabularController.TypePivot.
14. Inferire l'unità di misura dal contesto i.e. nome del campo/definedName durante l'importazione (definire mapping tra unità di misura e parole chiave fallback nessuna unità di misura).
15. Impostando grafico tipo PDF il select "Riferimento tooltip" diventa a sfondo bianco con testo in grigio chiaro, non molto leggibile.