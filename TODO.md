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
5. accesso 'veloce' alla vista tabellare per ogni tipologia di report da sidebar/dashboard
6. cancellazione massiva report SOLO tramite applicazione web (per batch, per tipo?):
7. semplificazione struttura database (es. ReportProperties?):
   - spostare Unit e IsSubjectKey in ReportEntities?
   - eliminare ridondanza tra valori memorizzati in ReportProperties e campi di ReportProperties stessa
8. rimozione codice non utilizzato
9. Implementare un IHostedService o un job schedulato per la pulizia delle sessioni tabellari scadute, 
   oppure rimuovere l'intera infrastruttura TabularSession se non è pianificata a breve.
10. Estendere la suite di test con almeno un test di integrazione per la pipeline import e uno per TabularController.TypePivot.
11. Impostando grafico tipo PDF il select "Riferimento tooltip" diventa a sfondo bianco con testo in grigio chiaro, non molto leggibile.