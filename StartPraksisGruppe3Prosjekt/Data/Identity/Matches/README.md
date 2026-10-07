# Kampdata for Identity Benchmarking

Denne mappa er git-ignorert. Bare denne fila er med i repoet.

Filene her (`g14.json`, `g15.json`, `g17.json`) lages av
`scripts/identity/extract_stats.py` fra StatsBomb-rapportene, og de inneholder spillernavn.
De fleste spillerne er mindreårige, og repoet er offentlig. Filene skal derfor aldri
committes, heller ikke med `git add -f`.

```bash
python3 scripts/identity/extract_stats.py --team G14 --source "<mappe med rapportene>" --table
```

Filer fra før lagene het G (`u14.json` med `"team": "U14"`) leses fortsatt, og trenger ikke
lages på nytt. Rapportene kaller lagene «Start U14», og fila skriver navnene slik rapporten gjør;
siden viser «Start G14».

Mangler en fil, viser Identity-siden at det ikke finnes data for laget. En fil fra før
erstatningsmarkørene (`schemaVersion` 1) stopper oppstarten med beskjed om å kjøre scriptet på
nytt. Se `docs/identity-benchmarking.md`.
