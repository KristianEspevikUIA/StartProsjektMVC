# Import av trenernes succession planning-ark

Denne mappa er git-ignorert. Bare denne fila er med i repoet.

Her ligger trenernes utfylte «IK Start Succession Planning»-ark og det som importeres fra dem.
Arkene har fullt navn på spillerne, de fleste mindreårige, ved siden av trenernes vurdering av
dem, og repoet er offentlig. Ingenting her skal committes, heller ikke med `git add -f`.

| Fil | Hva |
| --- | --- |
| `workbooks/*.xlsx` | Arkene slik trenerne leverte dem, ett per trener |
| `decisions.json` | Avgjørelser for rader skriptet ikke kunne matche trygt. Skrives for hånd |
| `assessments.json` | Det appen leser inn. Lages av skriptet |
| `report.md` | Hva som ble matchet, hva som ikke ble det, og hvorfor. Lages av skriptet |

Troppene må være hentet først, fordi radene matches mot dem:

```bash
python3 scripts/squads/fetch_squads.py
python3 scripts/succession/import_workbooks.py --table
dotnet run --project StartPraksisGruppe3Prosjekt
```

Skriptet tar med alt trenerne skrev, også friteksten, som blant annet har helseopplysninger om
mindreårige; rapporten gjentar aldri tekst. Rader det ikke kan matche trygt, blir ikke med, men
står i `report.md` med grunn og kandidater. Avgjør dem i `decisions.json` og kjør skriptet på nytt:

```json
{ "matches": { "Etternavn, Fornavn": "Navnet slik klubben skriver det", "Etternavn, Annen": null } }
```

`null` holder raden ute for godt. Appen leser `assessments.json` ved oppstart i Development
(`Data/SeedSuccessionImport.cs`), og en ny kjøring lager ingen duplikater. Se
`docs/succession-planning.md`.
