# Succession planning: notat til neste økt

Et notat til neste chat som skal jobbe videre med succession planning i StartCompass. Lim inn
hele fila, eller delen du trenger, som første melding. Den står på egne ben og forutsetter ingen
tidligere samtale.

Den fulle beskrivelsen av funksjonen er [`docs/succession-planning.md`](succession-planning.md).
Dette notatet sier hva som er bygget, hvorfor det ble slik, hva som gjenstår og hva som er lett å
tråkke feil i.

**Status per 23.09.2026:** bygget, testet og flettet inn i `main` (PR #32 og #34). Grenen
`Kristian` er lik `main` for appen og dokumentasjonen.

---

## Hva det er

Trenernes Excel-ark «IK Start Succession Planning», gjort om til sider i appen på `/Succession`.
Trenerne beskrev det slik:

> Succession planning – Formation – Position? Overview of excel data etc. Kept separate, pulls it
> all together. Compare coaches responses – only answers for the players. Every 8 weeks. What
> would the best 11 in a 4-3-3 look like? Who's in the building to be the best fit for the
> formation? Overview – look at where the player is "off/how many weeks" till they are fit to
> the position.

| Side | URL | Hva |
| --- | --- | --- |
| Squad board | `/Succession` | Én rad per spiller, trenerne lagt sammen. Faner: Readiness, Ratings, Contracts and pathway, Where coaches disagree |
| Best eleven | `/Succession/Formation` | Bane med beste 11 i 4-3-3 (også 4-2-3-1, 3-5-2), og «Who is next in line» per posisjon |
| Spillerside | `/Succession/Player/{id}` | Hver trener side om side, utvikling over tid, fritekst, kontrakt |
| Vurderingsskjema | `/Succession/Rate/{id}` | Én rad i arket, for innlogget trener og gjeldende syklus |

**Tilgang:** trener og administrator. Bare trenere vurderer. Admin ser alt og kan endre
kontraktsopplysningene. Spillere og foresatte får 403, også om seg selv.

---

## Endringer etter første versjon (etter ønske fra trenerne)

1. **Posisjoner som tre kolonner, 1st/2nd/3rd.** Hver kolonne er en egen avstemning: det flest
   trenere skrev i den kolonnen. Ved likt antall står begge, merket «Split».
   (`PlayerConsensus.PositionsByRank`, `_SuccessionPositionVote.cshtml`.) Beste ellever bruker
   fortsatt «beste rangering noen trener ga». Det er med vilje, for de to svarer på forskjellige
   spørsmål.
2. **Klikk på antall trenere for å se hvem.** Kolonnen «Rated by» er en `<details>` som åpner seg
   i raden. Den viser hver trener med egen overall og dato, og lenker til sammenligningen side
   om side (`_SuccessionRaters.cshtml`, `BoardPlayer.Raters`). Trenerne vises med delen av
   e-posten før @, og innlogget trener som «You».

---

## Filer

| Hva | Hvor |
| --- | --- |
| Lister, terskler, formasjoner (fra arkets «Text»-fane) | `Data/Succession/succession-planning.json`, validert ved oppstart |
| Modell | `Models/SuccessionAssessment.cs` (også `SuccessionRating`, `PlayerSuccessionProfile`), `Models/Succession/` |
| Alle utregninger, rene funksjoner | `Services/Succession/SuccessionMath.cs` |
| Database | `Services/Succession/SuccessionPlanningService.cs` |
| JSON-fila | `Services/Succession/SuccessionCatalog.cs` |
| Controller | `Controllers/SuccessionController.cs` |
| Views | `Views/Succession/` (Index, Formation, Player, Rate, `_SuccessionNav`, `_SuccessionPositionVote`, `_SuccessionRaters`) |
| Formatering og CSS-klasser | `ViewModels/Succession/SuccessionFormat.cs`, seksjonen «succession planning» nederst i `wwwroot/css/startcompass.css` |
| Migrasjon | `Data/Migrations/20260923075345_AddSuccessionPlanning.cs` |
| Demodata | `Data/SeedSuccession.cs` (bare Development) |
| Tester | `SuccessionMathTests` (33), `SuccessionCatalogTests` (21), `SuccessionPageTests` (29), pluss GDPR i `AdminGdprTests` |
| Hjelp for trenere | `Views/Help/Index.cshtml`, `#succession` og `#succession-eleven`, vises bare for trener og admin |

---

## Reglene, kort

- **Syklus:** åtte uker fra mandag 5.1.2026 (`cycle.firstCycleStartsOn`), regnet ut og aldri
  lagret. En vurdering lagrer syklusens første dag. Man vurderer bare i gjeldende syklus. Å
  vurdere igjen i samme syklus erstatter vurderingen, med unik indeks på (spiller, syklus, trener).
- **Én vurdering per trener**, aldri en felles rad. «Together» er snittet av trenerne, og hver
  trener teller én gang.
- **Overall** = snittet av de seks vurderingene (arkets `SUM(N:S)/6`). En tom vurdering utelates
  i stedet for å telle som 0, og skjemaet krever alle seks.
- **Lys:** under 6 = Not yet, 6–8 = Developing, 8 og over = Ready (arkets ikonsett).
  Fargeskala 1–10 som i arket (`.sc-rate--1` … `--10`).
- **Uenighet:** to trenere 3 poeng eller mer fra hverandre på én vurdering, eller uenige om
  kategorien.
- **Uker til klar:** minste kvadraters linje gjennom alle syklusene spilleren er vurdert i, per
  uke, forlenget til 8. Trenger minst to sykluser. Flat eller fallende linje gir «not closing».
  Over 156 uker gir ikke noe tall.
- **Beste ellever:** fit = overall − 0 / 0,5 / 1,0 for 1./2./3. posisjon. Det sterkeste paret
  plasseres først, og ingen spiller brukes to ganger. En posisjon uten kandidater står tom.
  Ved likhet avgjør overall, så rangering og til slutt spillerkoden.
- **Grå rad:** ingen har vurdert spilleren i denne syklusen. Tavla viser da forrige syklus og
  sier hvilken.
- **Kontrakt og treningsgruppe** er fakta og ikke meninger. De legges inn én gang per spiller
  (`PlayerSuccessionProfile`), ikke av hver trener.

---

## Personvern og husregler som gjelder her

- **Ingen ekte navn i repoet.** Arket trenerne leverte har fullt navn på spillerne, de fleste
  mindreårige. Det ble aldri lagt inn. Appen bruker spillerkoder (`TS-08-16`). Repoet er offentlig.
- **Revisjonslogg:** spillersiden og skjemaet logger én rad hver. Tavla og banen logger én rad
  per spiller som vises med tall, i én lagring (`IPlayerAccessLog.RecordManyAsync`).
- **GDPR:** `/Admin/Export/{id}` tar med vurderingene med fritekst og kontraktsopplysningene, med
  trenerne som «Coach 1». Sletting av en spiller tar alt med seg (cascade).
- **Sikt:** `docs/sikt-melding.md` er oppdatert med de nye opplysningene. Den må sendes før ekte
  data legges inn.
- **CSP uten `unsafe-inline`:** ingen `style=""`. Farger er klasser fra faste sett (`SuccessionTones`).
- **Bare Kristian lager migrasjoner** (`dotnet ef migrations add`).
- **UI på engelsk, dokumentasjon på norsk.** Kommentarer forklarer hvorfor, ikke hva.

**Demokontoer** (Development, passord `Dev!passord1`): `trener.senior@ikstart.example`,
`trener.akademi@ikstart.example`, `trener.utvikling@ikstart.example`. Det er tre trenere, slik at
det finnes noe å sammenligne. Demodataene dekker tre sykluser, og den gjeldende er omtrent
halvveis vurdert.

---

## Åpne spørsmål til trenerne

1. **«Rated as»:** er det nivået spilleren vurderes mot, f.eks. 1st team? Vi har tolket det slik,
   og beste ellever kan filtreres på det.
2. **«External needed?»:** gjelder det spilleren eller posisjonen? Banen viser uansett posisjoner
   uten etterfølger.
3. **Prognosene** (0–6, 6–18, 18–36 mnd) er fritekst. Skriver de alltid et nivå? Da kan feltene
   bli lister, og appen kan tegne en tidslinje.
4. **Import av eksisterende ark:** arket har navn, ikke koder. En import krever en kobling fra
   navn til kode, og den finnes ikke i appen.

## Mulige neste steg

- Import fra Excel, når koblingen fra navn til kode er avklart.
- Tersklene (8, 3 poeng, trekket for 2. og 3. posisjon) ligger i JSON-fila. Endre dem der hvis
  trenerne vil, uten kodeendring.
- Spillerbildene fra velkomsten (se under) vises i dag bare for spilleren selv. Hvis trenerne vil
  se dem på tavla eller banen, er det en egen beslutning om tilgang.

---

## Beslektet: velkomst med navn og bilde

Bygget i samme økt, og i `main`. Når en spiller logger inn, står det «Welcome, Alex» med
spillerens eget bilde. Admin legger inn fornavn og bilde på `/Admin/Players`. Tabellen
`PlayerPersonalDetails` er det eneste stedet et spillernavn lagres, og bare spilleren selv og
admin ser det. En test passer på at navnet ikke dukker opp på trenersidene eller
succession-tavla. Se [`docs/player-welcome.md`](player-welcome.md).

---

## Praktisk: kjøre og teste i en ny økt

- **Tester:** `dotnet test` kjører på SQLite i minnet og trenger ingen database eller hemmelighet.
  395 tester, alle grønne da velkomsten ble pushet (`5fad377`). Commitene som kom etter, gjelder
  Identity Benchmarking.
- **.NET i skycontaineren:** SDK-en er ikke installert på forhånd, og `dot.net`-skriptet blokkeres
  av proxyen. `apt-get install -y dotnet-sdk-8.0` virker. `dotnet-ef` installeres med
  `dotnet tool install --global dotnet-ef --version 8.0.11`.
- **Kjøre appen uten Supabase:** `apt-get install -y postgresql`, start klyngen, sett et passord,
  og kjør med
  `ConnectionStrings__DefaultConnection="Host=localhost;Database=startcompass;Username=postgres;Password=…"`
  og `ASPNETCORE_ENVIRONMENT=Development`. Migrasjoner og demodata kjører ved oppstart.
- **Skjermbilder:** Playwright og Chromium ligger i containeren (`npm root -g`/playwright).

## Feller vi gikk i

- `pkill -f StartPraksisGruppe3Prosjekt` treffer også ditt eget skall, fordi stien står i
  kommandolinja. Finn PID-en med `ps` og et mønster som `[n]et8.0/…`.
- Inne i en Razor-kodeblokk skal det stå `foreach`, ikke `@foreach`. Det siste gir RZ1008.
- En `a.x:visited`-regel som setter border, vinner over en enkel `.x--ready`-klasse. Sett bare
  farge på `:visited`.
- Tavla ble bredere enn siden da listene åpnet seg. Split-celler og lange kategoribadger får nå
  bryte linjen (`.sc-table--squad .sc-tone`, `.sc-position--split`).
- Grønt readiness-lys på en grønn bane forsvinner. Lyset står derfor også på tallet i kortet.
