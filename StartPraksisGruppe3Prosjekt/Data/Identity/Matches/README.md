# Kampdata for Identity Benchmarking

Denne mappa er git-ignorert. Bare denne fila er med i repoet.

Filene her (`u14.json`, `u15.json`, `u17.json`) lages av
`scripts/identity/extract_stats.py` fra StatsBomb-rapportene, og de inneholder spillernavn.
De fleste spillerne er mindreårige, og repoet er offentlig. Filene skal derfor aldri
committes, heller ikke med `git add -f`.

```bash
python3 scripts/identity/extract_stats.py --team U14 --source "<mappe med rapportene>" --table
```

Mangler en fil, viser Identity-siden at det ikke finnes data for laget. Se
`docs/identity-benchmarking.md`.
