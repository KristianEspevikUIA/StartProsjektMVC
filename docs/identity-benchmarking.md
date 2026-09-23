# Identity Benchmarking

Lagenes kamptall fra StatsBomb-rapportene satt opp mot IK Starts **Identity Gold Standard**,
i formatet til klubbens «U14 Identity Comparison». Gold Standard-tabellen alene ligger på
`/Identity/GoldStandard`.

Sammenligningen er tre sider, med knapper mellom dem:

| Side | Adresse | Innhold |
| --- | --- | --- |
| Overview | `/Identity` | de to tabellene og forklaringen av statusene |
| Key Insights | `/Identity/Insights` | Key Tactical Insights og Player Highlight |
| Development over time | `/Identity/Development` | utviklingen kamp for kamp, delt i In Possession og Out of Possession |

Alle tre tar `team` og `match` i adressen. Knappene tar med valgt lag og kamp, og lag- og
kampvelgeren blir på siden du står på. Det de deler (toppen, velgerne, knappene og valget) ligger
i `Views/Identity/_IdentityLayout.cshtml`.

**Tilgang:** trener og administrator, via `[Authorize(Roles = Coach,Admin)]` på
`IdentityController`. Menypunktet «Identity» vises bare for de to rollene. Key Insights navngir
spillere (Player Highlight), og derfor er rollesperren på hele controlleren.

---

## Hvor tallene kommer fra

```
StatsBomb-PDF-er  ──scripts/identity/extract_stats.py──▶  Data/Identity/Matches/u14.json   (git-ignorert)
StatsBomb-PDF-er  ──scripts/identity/derive_targets.py──▶  foreløpige målområder, limes inn i gold-standard.json
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

Filene har `schemaVersion` 2 fra og med erstatningsmarkørene. Appen nekter å starte med en
fil i versjon 1 og ber deg kjøre scriptet på nytt, i stedet for å vise seks tomme rader.

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
| Pasningstabellen har akkurat de samme spillerne som forsvarstabellen | ufullstendig pasningstabell, eller feil lag |
| Per spiller: `OP F3 Pass ≤ OP Pass` og `OP PintoB ≤ PintoB`; sum `OP Pass` ≤ «Total Passes» | forskjøvet kolonne |
| Per spiller i presstabellene: «Total Duration» / «Pressures» = «Duration Per Pressure» (innenfor avrundingen) og «Pressured Action Fails» ≤ «Pressures» | forskjøvet kolonne |
| Sum pressures per spiller = «Pressures» på Match Statistics | ufullstendig presstabell, eller feil lag |
| Hver spiller i gegenpressing-tabellen finnes i presstabellen, med minst like mange pressures der | feil tabell eller feil lag |
| «Pressure Regains» ≤ «Pressures» | feillest statistikkrad |

Høyre del av venstre lags tabeller er klippet bort i selve PDF-en i flere rapporter: «Disp»
mangler, «Pass%» står som «Pass» eller mangler, og i én rapport står «Drib» som «Dr».
Scriptet sjekker kolonnene det leser, og ingen av de klippede leses.

---

## Markørene

Fire av klubbens ti markører kan måles direkte fra rapporten. De seks andre har ikke noe tall
i en StatsBomb-rapport, og er **erstattet** av det nærmeste rapporten faktisk har:

| Klubbens markør | Vises som | Kilde i rapporten | Formel |
| --- | --- | --- | --- |
| Possession % | Possession % | Match Statistics | Starts «Possession %» |
| Progressive Passes | **Final Third Passes** | Appendix, Player Stats: Passing and Touches | sum «OP F3 Pass» over Starts spillere |
| Match Tempo | **Total Passes** | Match Statistics | Starts «Total Passes» (forsøkte) |
| Pass Accuracy | Pass Accuracy | Match Statistics | Starts «Pass Completion %» |
| Successful Dribbles | Successful Dribbles | Appendix, Player Stats: Defensive Actions / Other | sum «Drib» over Starts spillere |
| PPDA (Intensity) | **Pressures** | Match Statistics | Starts «Pressures» |
| Opp. Half Recoveries | **Counterpresses** | Pressure, Gegenpressing | sum «Pressures» over Starts spillere: press innen fem sekunder etter balltap |
| Total Duels Won | **Tackle Success %** | Match Statistics | «Tackles Won» / «Attempts», hele prosent |
| Total Regains | **Pressure Regains** | Match Statistics | Starts «Pressure Regains» |
| Interceptions | Interceptions | Appendix, Player Stats: Defensive Actions / Other | sum «I» over Starts spillere |

Hvorfor klubbens seks ikke kan måles, og hvorfor akkurat disse erstatter dem:

- **Progressive Passes:** rapporten deler pasninger i siste tredjedel, inn i boksen og lange
  baller. Ingen av dem er en progressiv pasning. «OP F3 Pass» er den nærmeste av dem.
- **Match Tempo** er pasninger per minutt. Rapporten har pasningene, men ingen effektiv
  spilletid: «Possession %» er lagets andel av pasningene, ikke av tiden. Det har vi sjekket
  mot alle 20 rapportene.
- **PPDA:** rapporten har verken motstanderens pasninger eller forsvarsaksjoner per sone. Den
  teller pressures, som er selve presset.
- **Opp. Half Recoveries:** rapporten har ingen ballvinninger per banehalvdel. Den teller press
  innen fem sekunder etter balltap, altså refleksen bak en høy ballvinning.
- **Total Duels Won:** rapporten gir bare taklinger vunnet av forsøkt, som er defensive
  bakkedueller. Luftdueller kan ikke telles, fordi `Aer%` er 0 både når spilleren ikke hadde
  noen og når alle ble tapt. Mislykkede driblinger registreres ikke. Bakkeduellen er den ene
  duellen rapporten har fullt ut.
- **Total Regains:** rapporten har bare «Pressure Regains», altså ballvinning innen fem sekunder
  etter press. Det er en delmengde, ikke totalen, og måles derfor mot et eget målområde.

En erstatning er en egen markør med eget navn, eget tall og eget målområde. På siden står det
«Replaces …» under navnet. Linjen kan åpnes, og viser da klubbens målområde, hvorfor
erstatningen er valgt og hvor det foreløpige målområdet kommer fra. Under tabellene står bare
klubbens egne fotnoter. Klubbens tall vises aldri under et annet navn, og et annet tall vises
aldri under klubbens navn.

Klubbens seks ligger urørt i `gold-standard.json`, under `replaces.marker` på erstatningen,
med navn, målområde, «best at it» og begrunnelsen for at de ikke kan måles. Kan en av dem
måles senere, flyttes den tilbake ut. Katalogen godtar ikke en erstatning for en markør som
måles.

Hver verdi på siden har en «Source»-lenke med formel, filnavn og side.

---

## Foreløpige målområder

Klubbens Gold Standard har ingen målområder for erstatningene. Til klubben setter sine egne,
følger de dokumentets egen metode, «consistent team averages (the floor) with peak match data
(the ceiling)», brukt på Starts motstandere i rapportene:

- **gulv:** motstandernes snitt per kamp, avrundet til hele tall (halve opp)
- **tak:** motstandernes beste enkeltkamp

```bash
python3 scripts/identity/derive_targets.py \
  --source "<U14-mappe>" --source "<U15-mappe>" --source "<U17-mappe>"
```

Scriptet leser motstanderens side av hver rapport med de samme funksjonene og kontrollene som
Starts side, og skriver ut verdiene som skal inn i `gold-standard.json`. Det skriver ingenting
selv. Fra de 20 rapportene (1. april – 29. august 2026):

| Markør | Målområde | Beste enkeltkamp | Motstandernes snitt U14 · U15 · U17 |
| --- | --- | --- | --- |
| Final Third Passes | 68 – 140 | Stabæk U14 | 68.2 · 61.7 · 71.1 |
| Total Passes | 471 – 596 | Rosenborg U17 | 425.6 · 459.5 · 503.7 |
| Pressures | 182 – 298 | Haugesund U15 | 179.2 · 213.5 · 161.9 |
| Counterpresses | 45 – 77 | Haugesund U15 | 54.6 · 57.5 · 31.8 |
| Tackle Success % | 64 % – 83 % | Stabæk U14 | 62.2 · 63.2 · 64.4 |
| Pressure Regains | 61 – 99 | Haugesund U15 | 73.6 · 70.8 · 47.1 |

Disse områdene er merket `"provisional": true` og har en `basis` som sier hvor de kommer fra.
Siden merker dem **Provisional**, og innsiktene kaller dem «provisional range», aldri «elite
range». Katalogen godtar ikke et foreløpig målområde uten `basis`.

**Svakhet:** Som klubbens egne gjelder ett målområde for alle årskull. Volummarkørene øker
med alderen, så U14 blir målt hardt på Total Passes, og U17 på Counterpresses og Pressure
Regains. Snittene per årskull i tabellen over er et utgangspunkt hvis klubben vil sette egne.

**Når klubben setter egne:** bytt `min`, `max`, `display`, `eliteRange` og `bestAtIt`, og
fjern `provisional` og `basis`. `IdentityCatalogTests` sjekker at trykt område og tall stemmer
overens.

---

## Status

Kategoriene og fargene fra sammenligningsdokumentet. Grensene ligger i
`gold-standard.json` (`statusRules`). Alle markørene på siden er «høyere er bedre». Klubbens
PPDA er «lavere er bedre» og har egne grenser, som fortsatt ligger på den i `replaces.marker`:

| Høyere er bedre | Klubbens PPDA (lavere er bedre) | Status |
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
  mest. Styrker kommer først, så utviklingsområder. Umålte markører nevnes ikke. Et foreløpig
  målområde kalles «provisional range». Er det uavgjort over grensen på tre navn, utelates hele
  gruppen.
- **Player Highlight:** lederen på Successful Dribbles og Interceptions i valgt kamp, eller
  sum over sesongen for «All matches». Én spiller trekkes fram bare når samme person leder
  begge. Ved uavgjort nevnes alle, eller «N players» når det er flere enn tre.

---

## Personvern

Rapportene og JSON-filene navngir spillere, de fleste mindreårige, og repoet er offentlig.

- `Data/Identity/Matches/*` er git-ignorert. Bare README-en der følger med.
- Per spiller lagres bare navn, Drib og I, altså det highlighten viser. Pasnings- og
  presstabellene leses spiller for spiller for å kontrollere dem, men bare Starts summer lagres.
- `gold-standard.json` nevner motstanderlag (for eksempel «Stabæk U14 (peak 140)»), men ingen
  spillere.
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

De tre siste er de samme byttene som erstatningsmarkørene gjør. Forskjellen er at siden viser
dem under sitt eget navn og mot sitt eget målområde: Tackle Success % 51 %, Pressure Regains 65
og Pressures 191, ikke klubbens Total Duels Won, Total Regains og PPDA.

---

## Filer

| Hva | Hvor |
| --- | --- |
| Uttrekk | `scripts/identity/extract_stats.py` |
| Foreløpige målområder | `scripts/identity/derive_targets.py` |
| Gold Standard | `Data/Identity/gold-standard.json` |
| Modeller | `Models/Identity/` |
| Katalog, statusregler, bygger, innsikter | `Services/Identity/` |
| Controller | `Controllers/IdentityController.cs` |
| Visninger | `Views/Identity/` |
| Stil | `wwwroot/css/startcompass.css`, seksjonen «identity benchmarking» |
| Tester | `IdentityCatalogTests`, `IdentityStatusRulesTests`, `IdentityBenchmarkBuilderTests`, `IdentityPageTests` |
