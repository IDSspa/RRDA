# Analisi critica e proposta tecnica: proiezione tabellare orizzontale dei report

## Nota terminologica: “Pivot” vs “Proiezione Tabellare”

Il termine **Pivot** è comprensibile tecnicamente, ma può risultare ambiguo perché:
- richiama una specifica operazione SQL (`PIVOT`), mentre l’implementazione può avvenire anche in memoria;
- non esplicita il fine funzionale (ottenere un dataset tabellare stabile per analytics).

Per questo si propone come naming di dominio:
- **Tabular Projection / Proiezione Tabellare** per contratti applicativi;
- mantenendo “pivot” solo come termine descrittivo informale.

## Scelta proposta

- Interfaccia core: `ITabularProjectionProvider`
- Contratti: `TabularRequest`, `TabularSchema`, `TabularResult`
- Cache sessionale: `TabularSessions`, `TabularSessionRows`

Questa nomenclatura è più neutra rispetto al motore di esecuzione e più chiara per i team applicativi.
