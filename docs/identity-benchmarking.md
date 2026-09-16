# Identity Benchmarking

Lagenes kamptall fra StatsBomb-rapportene satt opp mot IK Starts **Identity Gold Standard**,
i formatet til klubbens «U14 Identity Comparison». Siden ligger på `/Identity`, og Gold
Standard-tabellen alene ligger på `/Identity/GoldStandard`.

**Tilgang:** trener og administrator, via `[Authorize(Roles = Coach,Admin)]` på
`IdentityController`. Menypunktet «Identity» vises bare for de to rollene. Siden navngir
spillere (Player Highlight), og derfor er rollesperren på hele controlleren.

---

## Hvor tallene kommer fra

```
StatsBomb-PDF-er  ──scripts/identity/extract_stats.py──▶  Data/Identity/Matches/u14.json   (git-ignorert)
IK Start – Identity Gold Standard.pdf  ──(transkribert)──▶  Data/Identity/gold-standard.json (i git)
                                                           │
                                          IdentityCatalog (lastes og valideres ved oppstart)
                                                           │
                                     IdentityBenchmarkBuilder ──▶ Views/Identity
```

Ingen visning inneholder et tall, et målområde eller en markørtekst. Alt kommer fra de to
JSON-filene.

### Hente ut kampdata

Krever Python 3.9+ og poppler (`brew install poppler`).

```bash
python3 scripts/identity/extract_stats.py --team U14 --source "<mappe med rapportene>" --table
```

Kjør én gang per lag (U14, U15, U17). Scriptet skriver
`StartPraksisGruppe3Prosjekt/Data/Identity/Matches/<lag>.json`, og `--table` skriver en
kontrolltabell. Start appen på nytt etterpå, fordi dataene leses ved oppstart.

Scriptet finner sidene ut fra overskriften, ikke ut fra sidenummer. Match Statistics ligger på
side 3 eller 4, og forsvarstabellen på side 22–24, avhengig av rapporten. Det stopper med
filnavnet hvis noe ikke henger sammen:

| Kontroll | Hva den fanger |
| --- | --- |
| Hjemme/borte i sidetoppen = rekkefølgen i filnavnet | rapport lest feil vei |
| Lagnavn over kolonnene på Match Statistics = sidetoppen | feil kolonne for Start |
| Per spiller: `T+I = T + I` og `T/DP% = T / (T + DP)` | forskjøvet kolonne |
| Sum `T` = «Tackles Won», sum `T + DP` = «Attempts» | ufullstendig spillertabell, eller feil lag |
| Possession hjemme + borte = 100, Pass % = fullførte / totalt | feillest statistikkrad |

«Disp»-kolonnen er klippet bort i selve PDF-en i flere rapporter. Den leses ikke.

---

## Markørene

| Markør | Kilde i rapporten | Formel |
| --- | --- | --- |
| Possession % | Match Statistics | Starts «Possession %» |
| Pass Accuracy | Match Statistics | Starts «Pass Completion %» |
| Successful Dribbles | Appendix, Player Stats: Defensive Actions / Other | sum «Drib» (dribbles past an opponent) over Starts spillere |
| Interceptions | samme tabell | sum «I» over Starts spillere |
| Progressive Passes | – | **Not measured**: rapporten har ikke tallet |
| Match Tempo | – | **Not measured**: ingen tempo og ingen effektiv spilletid |
| PPDA (Intensity) | – | **Not measured**: rapporten teller pressures, ikke PPDA |
| Opp. Half Recoveries | – | **Not measured**: ingen ballvinninger per banehalvdel |
| Total Duels Won | – | **Not measured**: se under |
| Total Regains | – | **Not measured**: se under |

**Total Duels Won:** rapporten gir bare taklinger vunnet av forsøkt, som er defensive
bakkedueller. Luftdueller kan ikke telles, fordi `Aer%` er 0 både når spilleren ikke hadde
noen og når alle ble tapt. Mislykkede driblinger registreres ikke.

**Total Regains:** rapporten har bare «Pressure Regains», altså ballvinning innen fem sekunder
etter press. Det er en delmengde, ikke totalen.

En markør som ikke måles, får ingen verdi, ingen status og ingen stedfortreder. Begrunnelsen
står i `gold-standard.json` og vises som fotnote.

Hver verdi på siden har en «Source»-lenke med formel, filnavn og side.

---

## Status

Kategoriene og fargene fra sammenligningsdokumentet. Grensene ligger i
`gold-standard.json` (`statusRules`, og på PPDA-markøren):

| Høyere er bedre | PPDA (lavere er bedre) | Status |
| --- | --- | --- |
| over maks | ≤ 5.04 | Exceptional |
| min–maks | < 10.0 | Elite Alignment |
| ≥ 90 % av min | ≤ 11.0 | Strong Alignment |
| ≥ 75 % av min | ≤ 12.5 | Developing |
| lavere | høyere | Below Target |

Fargene er justert der dokumentets egen kombinasjon ikke holder WCAG AA. Oransje og gul har
svart tekst, og grønn er mørkere. Kontrasten er dokumentert ved `--sc-id-*` i
`startcompass.css`. Status står alltid i tekst, aldri bare som farge.

### «All matches»

Snittet er det vanlige gjennomsnittet av kampverdiene, avrundet til én desimal. Statusen
regnes av det avrundede tallet, altså det som står på siden. Kildelisten viser alle kampene.

### Key Tactical Insights og Player Highlight

- **Insights:** maks fire setninger, bygget av maler med verdi, målområde og hvem som bidro
  mest. Styrker kommer først, så utviklingsområder. Umålte markører nevnes ikke. Er det
  uavgjort over grensen på tre navn, utelates hele gruppen.
- **Player Highlight:** lederen på Successful Dribbles og Interceptions i valgt kamp, eller
  sum over sesongen for «All matches». Én spiller trekkes fram bare når samme person leder
  begge. Ved uavgjort nevnes alle, eller «N players» når det er flere enn tre.

---

## Personvern

Rapportene og JSON-filene navngir spillere, de fleste mindreårige, og repoet er offentlig.

- `Data/Identity/Matches/*` er git-ignorert. Bare README-en der følger med.
- Per spiller lagres bare navn, Drib og I, altså det highlighten viser.
- Testene bruker et oppdiktet lag (`StartPraksisGruppe3Prosjekt.Tests/Identity/Fixtures`), og
  `StartCompassFactory` peker Identity-siden dit. Ekte navn havner derfor aldri i testoutput.
- **Må avklares:** `docs/sikt-melding.md` sier at navn ikke lagres om spillere. Denne siden
  leser navn fra filer, ikke fra databasen, men meldingen bør oppdateres før siden brukes med
  ekte data i drift.

---

## Avvik i «IK Start – U14 Identity Comparison 2.pdf»

Dokumentet gjelder Start U14 – Viking U14 (13.06.2026). Siden brukte bare formatet derfra.
Flere tall stemmer ikke med kamprapporten. Spillernavn er utelatt her, fordi repoet er offentlig:

| Dokumentet | Rapporten |
| --- | --- |
| Successful Dribbles 51 | 36 |
| Interceptions 74 (Exceptional) | 14 (Below Target) |
| Highlight: én Start-spiller med 13 interceptions | spilleren har 0 interceptions. 13 er Disp-kolonnen |
| to av tre navngitte dribble-ledere | begge spiller for Viking |
| Total Duels Won 51 % | 25 av 49 taklinger, ikke alle dueller |
| Total Regains 65 | Pressure Regains, ikke total |
| PPDA «191 press.» | antall pressures, ikke PPDA |

---

## Filer

| Hva | Hvor |
| --- | --- |
| Uttrekk | `scripts/identity/extract_stats.py` |
| Gold Standard | `Data/Identity/gold-standard.json` |
| Modeller | `Models/Identity/` |
| Katalog, statusregler, bygger, innsikter | `Services/Identity/` |
| Controller | `Controllers/IdentityController.cs` |
| Visninger | `Views/Identity/` |
| Stil | `wwwroot/css/startcompass.css`, seksjonen «identity benchmarking» |
| Tester | `IdentityCatalogTests`, `IdentityStatusRulesTests`, `IdentityBenchmarkBuilderTests`, `IdentityPageTests` |
