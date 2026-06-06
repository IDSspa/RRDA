~~1. implementazione grafici statistici~~

~~2. gestione esportazione dati filtrati vista tablellare (csv, xls)~~  
3. gestione/visualizzazione valori 'ranged'
4. gestione collegamenti/navigazione:
   - RADAR/COMPONENTE,
   - RADAR/SOTTOASSIEME ?
   - SOTTOASSIEME/COMPONENTE
   - importazione carta di identità radar

5. importazione singolo report applicazione web
6. cancellazione massiva report tramite applicazione web (per batch, per tipo?):
   - logging applicazione web su registro di sistema (login, operazioni critiche)

7. gestione paginazione tabella:
   - implementazione ordinamento vista tabellare -> default SubjectKey (pulsanti asc/desc)
   - ottimizzazione query statistiche

8. revisione criticità strutturali intera soluzione (implementazione "per services")
9. revisione UI WEB:
   - accesso 'veloce' alla vista tabellare per ogni tipologia di report da sidebar/dashboard

~~10. importazione plugin applicazione web~~

~~11. verificare aggiornamento tabella ReportTypes tramite RepImp (inserimento/aggiornamento plugins)~~  

~~12. creare installer per RepImp~~  

14. footer header vista tabellare fissi righe dati scrollabili

15. semplificazione struttura database (es. ReportProperties?):
    - spostare Unit e IsSubjectKey in ReportEntities?
    - eliminare ridondanza tra valori memorizzati in ReportProperties  e campi di ReportProperties stessa

16. rimozione codice non utilizzato  
~~17. quando un operazione richiede tempi di elaborazione consistenti mostrare un messaggio di attesa~~  
~~sovrapposto alla UI per evidenziare il passaggio del tempo ed evitare/bloccare ulteriori operazioni da parte dell'utente.~~
