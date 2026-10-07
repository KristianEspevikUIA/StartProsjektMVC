# StartPraksisGruppe3Prosjekt

StartCompass, et internt verktøy for IK Start. Spillere, foresatte og trenere svarer på de
samme 25 påstandene om de fem C-ene, og systemet viser hvor bildene deres av samme spiller
skiller seg. Trenerne har i tillegg Identity Benchmarking og succession planning.

**Systemet behandler personopplysninger om mindreårige.** Det er premisset bak alle
valgene under, og det er grunnen til at autorisasjon ikke er noe som skrus på til slutt.

> Bare oppdiktede data i dette repoet, og i de lokale utviklingsdatabasene. De ekte spillerne
> (navn, fødselsdato og bilde) skal bare ligge i hoveddatabasen, se
> [`docs/database.md`](docs/database.md). Prosjektgruppa avgjorde 07.10.2026 at de kan legges
> inn der før prosjektet er meldt til Sikt. Se «Ting som må avklares før ekte data».

---

## Status

**Bygget og i bruk:** 5C-spørreskjemaet (25 påstander i fem kategorier, pluss
refleksjonen som avslutter perioden), skjemalisten med
filtre, treneroversikten med sammenligning og oppfølgingsvarsel, lagoversikten med snitt per
kategori og påstand, utvikling over tid for både spiller og lag, søk i troppen, samtaleflyten
mellom spiller og trener, spiller- og foresattsiden, revisjonsloggen, admin-siden for
perioder, **kontoer** (`/Admin/Users`: opprette, gi midlertidig passord, låse), GDPR-innsyn og
-sletting i `AdminController` (`/Admin/Export/{id}` og `/Admin/Delete/{id}`, begge per
spiller, med lenker fra spillerlista på `/Admin/Players`), Identity Benchmarking, og
**succession planning** — trenernes Excel-ark som sider i appen (se under).

**Fortsatt TODO:** samtykkeskjemaet for foresatte, og siden for lag i `AdminController`.
Samtykkesiden finnes på `/Guardian/Consent/{id}`, men ingenting lenker
dit, og nivået som velges, styrer ingenting — se A1 og A2 i
[`docs/sikt-melding.md`](docs/sikt-melding.md).

**Fjernet:** det eldre ti-påstandsskjemaet (tabellene `Items`, `Responses` og `Answers`,
`ScoringService`, og `CoachController.Team`, `PlayerDetail` og `Search`). Ingenting lenket dit,
og 5C har sin egen skåring.

Sidene som ikke er bygget, er **ikke lenket til** fra menyen eller fra admin-forsiden. De
står på lista der som «Not built yet», og selve siden forteller hvem som eier arbeidet, hva
den skal gjøre, og har en vei tilbake — se `Views/Shared/_NotBuiltYet.cshtml`. En lenke som
fører til en tom side koster et klikk å oppdage, og det er verre enn ingen lenke.

`dotnet build` kjører rent, og `dotnet run` migrerer den lokale databasen og legger inn
oppdiktede data.

---

## Kom i gang

Hver utvikler har sin egen **lokale PostgreSQL 17** med oppdiktede data. Ingen database deles,
og appen er ikke avhengig av Supabase. **Alt om databasen, også hoveddatabasen i drift, står i
[`docs/database.md`](docs/database.md).**

**Steg 1: start databasen.** Kopier `.env.example` til `.env` (git-ignorert), sett et passord
etter `POSTGRES_PASSWORD=`, og start PostgreSQL med Docker:

```bash
docker compose up -d
```

Uten Docker: se «Uten Docker, på Windows» i [`docs/database.md`](docs/database.md).

**Steg 2: gi appen passordet.** Det samme som i `.env`. Resten av tilkoblingsstrengen står i
`appsettings.Development.json`:

```bash
dotnet user-secrets set "Database:Password" "PASSORDET_DITT" --project StartPraksisGruppe3Prosjekt
```

Passordet havner i `%APPDATA%\Microsoft\UserSecrets\`, ikke i git. Uten dette steget stopper
appen med en melding som forklarer akkurat dette — det er ikke en bug.

Har du den gamle Supabase-strengen i user-secrets, må den bort. Den overstyrer ellers fila:

```bash
dotnet user-secrets remove "ConnectionStrings:DefaultConnection" --project StartPraksisGruppe3Prosjekt
```

**Steg 3: kjør.**

```bash
dotnet run --project StartPraksisGruppe3Prosjekt
```

Første kjøring lager tabellene, markerer databasen som en utviklingsbase, og legger inn
roller, lag og oppdiktede demodata.

**Vil du begynne på nytt,** slett den lokale databasen og start igjen. Ingen andre merker det:

```bash
docker compose down -v
```

Appen starter bare mot en database som hører til miljøet den kjører i. Development nekter å
starte mot hoveddatabasen, og appen i drift nekter å starte mot en utviklingsbase — se
«Vernet mellom utvikling og drift» i [`docs/database.md`](docs/database.md).

### Demokontoer

Opprettes bare i `Development`, og først når seedingen har fått kjørt mot databasen.
**Standard passord er `Dev!passord1`** for alle kontoene under. Det kan overstyres:

```bash
dotnet user-secrets set "Seed:DevPassword" "ditt-passord" --project StartPraksisGruppe3Prosjekt
```

Overstyringen virker bare på kontoer som ikke finnes fra før — `SeedData.EnsureUserAsync`
oppretter, den endrer ikke passord. Sett den før første kjøring, eller begynn på nytt.

Demokontoene finnes bare i en database som er markert som utvikling. De kan ikke havne i
hoveddatabasen: der lages den første administratoren med `create-admin`.

**Det er én trenerkonto for lagene.** (Succession planning har to ekstra, se under tabellen.)

| Konto | Rolle |
| --- | --- |
| `admin@ikstart.example` | Admin |
| `trener.senior@ikstart.example` | Trener (alle lag) |
| `trener.akademi@ikstart.example`, `trener.utvikling@ikstart.example` | Trener, for succession planning |
| `spiller.brage.kristoffersen@ikstart.example` m.fl. | Spiller |
| `foresatt1@example.test` … `foresatt7@example.test` | Foresatt |
| `foresatt.isak.ronning@example.test` m.fl. | Foresatt |

De to siste trenerkontoene finnes fordi succession planning sammenligner trenere, og én konto
kan ikke være uenig med seg selv. De har vurderinger i demodataene, og man kan logge inn som en
av dem og se sin egen kolonne. Se `Data/SeedSuccession.cs`.

Spillerne har tilfeldige, oppdiktede navn — bortsett fra prosjektgruppa (Brage Kristoffersen,
Kristian Espevik, Victor Ziad og Taavi-Topias Henell), som spiller på G17 og kan logge
inn som seg selv. Troppene står i `SeedData.Squads`.

**Lagene er G14, G15 og G17**, de tre prosjektet gjelder, med fødselsdatoer etter årsklassene
(G17 født 2009–2010, G15 2011, G14 2012). Alle spillerne er dermed mindreårige og har en
foresatt. G19 finnes også, men har bare spillere når de ekte troppene er hentet (se
`docs/player-welcome.md`).

Spillerkontoen utledes av navnet: `Brage Kristoffersen` blir
`spiller.brage.kristoffersen@ikstart.example`, med æ, ø og å skrevet ae, o og aa. Foresatte
følger samme regel — `foresatt.isak.ronning@example.test` — bortsett fra de sju nummererte over,
som er navngitt i troppen og beholdes som de er.

To spillere har med vilje **ingen** konto (Tobias Moe og Kasper Solberg). Det er en egen
tilstand fra «har ikke svart», og begge skal virke.

Finnes `Data/Squads/squads.json` (git-ignorert), legges troppene derfra inn i stedet for de
oppdiktede: spilleren med fornavn og bilde, og ikke noe mer. Seedingen lager aldri en konto, en
foresatt, et samtykke, et svar eller en vurdering for en ekte spiller. Demokontoene for
spillere og foresatte, og alle demosvarene, hører til de oppdiktede spillerne og forsvinner
med dem. Administrator- og trenerkontoene blir stående. Se
[`docs/player-welcome.md`](docs/player-welcome.md).

---

## Stack

- ASP.NET Core MVC, **.NET 10 (LTS)**
- EF Core 10, code-first, **PostgreSQL 17** (Npgsql). Tabeller og kolonner har små bokstaver
  og understrek (`EFCore.NamingConventions`)
- ASP.NET Core Identity med roller

Om rammeverkversjonen: prosjektet står på `net10.0`, som er LTS-utgaven og støttes til
november 2028. Det ble oppgradert fra .NET 8 den 07.10.2026, en måned før .NET 8 går ut av
støtte. **Alle trenger .NET 10 SDK for å bygge:** `winget install Microsoft.DotNet.SDK.10`, eller
fra [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download). `dotnet-ef` må
følge med: `dotnet tool update --global dotnet-ef --version 10.0.12`.

Neste oppgradering er `<TargetFramework>` i csproj-filen, pakkeversjonene, `dotnet-version` i
`.github/workflows/ci.yml` og stiene i `.vscode/launch.json`. Gjør det som en egen, samlet
endring, ikke midt i en feature.

**Kode og identifikatorer på engelsk. Brukergrensesnittet er også på engelsk**, i tråd med
StartCompass-nettstedet og wireframene. README og dokumentasjonen i `docs/` skrives på norsk.

---

## 5C-spørreskjemaet

25 påstander i fem kategorier på en skala fra 1 til 5, besvart av spiller, foresatt og trener
om samme spiller, og trenersider som viser hvor de tre er uenige. Etter påstandene kommer en
kort refleksjon med egne ord, som avslutter perioden.

- **Spørsmålene** ligger i `Data/Questions/five-c-questions.json` og ingen andre steder. Ingen
  `.cshtml`-fil inneholder et kategorinavn eller en påstand, så treneteamet kan bytte hele
  settet uten at UI-koden røres. Fila valideres ved oppstart.
- **Perioder:** svarene tilhører ett målevindu (`SurveyRound`). Admin oppretter og stenger
  perioder på `Admin/Periods`, og valgt periode huskes i cookien `StartCompass.Period`.
- **Skjemaet** er bygget for mobil. Påstandene stokkes per spiller og periode, i bolker på
  fem, og en kladd holdes i nettleseren til skjemaet er sendt inn.
- **Hvem ser hva:** spiller og foresatt ser egne svar, og trenerens svar og avviket først når
  treneren frigir dem. Trener og admin ser alt, og hvert oppslag på en enkeltspiller havner i
  revisjonsloggen, som spilleren selv kan se.
- **Trenersidene:** per spiller (differanser, de fem C-ene, påstand for påstand, utvikling
  over tid) og per lag (snitt av spillere, spredning, og minst tre svar bak hvert tall).
- **Lagring:** appens egen database (`five_c_submissions`, `five_c_answers` og
  `five_c_reflection_answers`). Avvik og snitt regnes ut ved hver visning og lagres aldri.
- **Deling:** `/Survey/Fill?roundId=2&playerId=14&role=Coach` forhåndsvelger skjemaet. Lenken
  gir ingen tilgang i seg selv; begge sjekkene kjøres på nytt på serveren.

**Alt om dette: [`docs/five-c.md`](docs/five-c.md).**

## Identity Benchmarking

Lagenes kamptall fra StatsBomb-rapportene mot IK Starts Identity Gold Standard, på
`/Identity`, fordelt på tre sider med knapper mellom: Overview, Key Insights og Development
over time. Bare trener og administrator har tilgang.

- **Gold Standard:** `Data/Identity/gold-standard.json`, transkribert ordrett fra klubbens PDF.
- **Kampdata:** hentes ut med `scripts/identity/extract_stats.py` til `Data/Identity/Matches/`,
  som er **git-ignorert** fordi rapportene navngir spillere.
- **Erstatninger:** 4 av klubbens 10 markører kan måles direkte fra rapportene. De andre seks er
  erstattet av det nærmeste rapporten har (for eksempel Pressures i stedet for PPDA), under eget
  navn. Målområdene deres er **foreløpige**, utledet av motstanderne i rapportene med
  `scripts/identity/derive_targets.py`, til klubben setter egne.

**Alt om dette: [`docs/identity-benchmarking.md`](docs/identity-benchmarking.md).**

## Succession planning

Trenernes Excel-ark «IK Start Succession Planning» som sider i appen, på `/Succession`. Trener
og administrator har tilgang, men bare trenere vurderer.

- **Hver trener vurderer hver spiller hver åttende uke:** seks vurderinger fra 1 til 10,
  posisjoner, kategori, prognoser og notater, altså de samme kolonnene som arket. Vurderingene
  lagres hver for seg, og å vurdere på nytt i samme syklus er en retting.
- **Squad board** legger trenerne sammen: én rad per spiller, med arkets fargeskala, og en fane
  med spillerne der trenerne er uenige.
- **Best eleven** plukker de beste i 3-5-2 (eller 4-3-3, 4-2-3-1) fra de vurderte spillerne, og
  viser hvem som er nestemann i hver posisjon.
- **Off og uker til klar:** hvor langt unna 8 spilleren er, og hvor mange uker det tar med
  trenden så langt.
- **Lister og terskler** ligger i `Data/Succession/succession-planning.json`, validert ved oppstart.
- **Ingen navn fra arket.** Arket har ekte navn; appen har sine egne, oppdiktede demospillere.
  Arket ligger ikke i repoet. «Best eleven» viser fornavnet klubben har lagt inn til
  velkomsten, og ellers hele navnet.

**Alt om dette: [`docs/succession-planning.md`](docs/succession-planning.md).**

## Velkomst med navn og bilde

Når en spiller logger inn, står det «Welcome, Brage» på forsiden, med spillerens eget bilde.
Trenerne ba om det, og IK Start har gitt tillatelse til å bruke de offisielle spillerbildene.

- **Admin legger inn** fornavn og bilde på `/Admin/Players`. Lista har også lenker til innsyn og
  sletting for hver spiller.
- **Bare spilleren selv** ser bildet. Andre sider viser spillerens fulle navn, og «Best eleven»
  fornavnet.
  `/Player/Photo` har ingen ID, så den gir bare ditt eget bilde.
- **Bildet kontrolleres og renses:** bare JPEG, PNG og WebP (lest av filens bytes), høyst 2 MB,
  og GPS, bildetekst og annen metadata tas ut før det lagres.
- **Fornavn og bilde** ligger i egen tabell (`player_personal_details`). Ingen bilder ligger i
  repoet, og navnene i demodataene er oppdiktet — bortsett fra prosjektgruppas egne.
- **De ekte spillerne flyttes** mellom to databaser med `export-players` og `import-players`.
- **Må avklares:** Sikt-meldingen beskriver fortsatt spillerkoder i stedet for navn, og må
  oppdateres før den sendes. Navn og bilder legges inn før det (avgjort 07.10.2026).

**Alt om dette: [`docs/player-welcome.md`](docs/player-welcome.md).**

## Struktur

```
StartPraksisGruppe3Prosjekt/
├─ Program.cs                   tjenester, pipeline og sjekkene som kjører ved oppstart
├─ Controllers/
│  ├─ HomeController.cs         forside, personvern og feilsider (åpne uten innlogging)
│  ├─ SurveyController.cs       skjemaliste, utfylling, lagring
│  ├─ CoachController.cs        5C for treneren: alle lag, ett lag, én spiller
│  ├─ PlayerController.cs       spiller ser egne svar
│  ├─ GuardianController.cs     foresatt ser eget barn
│  ├─ AdminController.cs        kontoer, perioder, spillere, innsyn og sletting
│  ├─ IdentityController.cs     Identity Benchmarking
│  ├─ SuccessionController.cs   succession planning
│  └─ HelpController.cs         hjelpesiden: hvordan tallene skal leses
├─ Commands/                    engangskommandoene: create-admin, export- og import-players
├─ Areas/Identity/Pages/        vår egen innloggingsside; resten er Identity UI-pakkens
├─ Authorization/               policyer, krav og handlere
├─ Security/                    sikkerhetshoder og CSP, rate limiting, stengt registrering,
│                               midlertidige passord
├─ Contracts/FiveC/             det 5C-skjemaet sender inn, i C# og TypeScript
├─ Models/                      entiteter, enums og PlayerRules
├─ Data/
│  ├─ AppDbContext.cs
│  ├─ Migrations/               én migrasjon: InitialCreate
│  ├─ DatabaseConnection.cs     hvor tilkoblingsstrengen kommer fra, og kravet om TLS i drift
│  ├─ DatabaseGuard.cs          vernet mellom utvikling og drift
│  ├─ BaseSetup.cs              roller og lag, i alle miljøer
│  ├─ PlayerTransfer.cs         export-players og import-players
│  ├─ Questions/                five-c-questions.json (påstander og refleksjon)
│  ├─ Identity/                 gold-standard.json (+ Matches/, git-ignorert)
│  ├─ Succession/               succession-planning.json (lister, terskler, formasjoner)
│  ├─ Squads/                   de ekte troppene, git-ignorert (bare i utvikling)
│  ├─ SeedData.cs               oppdiktede demodata, bare i Development
│  ├─ SeedSquads.cs             troppene fra Data/Squads, i stedet for de oppdiktede
│  ├─ SeedSuccession.cs         oppdiktede succession-vurderinger
│  └─ SeedWelcome.cs            fornavn til velkomsten i demodataene
├─ Services/
│  ├─ IConsentService.cs + ConsentService.cs
│  ├─ IPeriodService.cs + PeriodService.cs          perioder, én vei inn
│  ├─ IPeriodSelection.cs + PeriodSelection.cs      valgt periode, husket i en cookie
│  ├─ IFeedbackReleaseService.cs + …                trenerens frigivelse
│  ├─ IPlayerAccessLog.cs + PlayerAccessLog.cs      revisjonsloggen
│  ├─ PlayerWelcomeService.cs + PlayerPhotoRules.cs velkomsten: fornavn og bilde
│  ├─ FiveC/                                        spørsmålskatalog, lagring, analyse
│  ├─ Identity/                                     Gold Standard, status, snitt, innsikter
│  └─ Succession/                                   lister, utregninger, lagring
├─ ViewModels/
├─ Views/                       én mappe per controller, pluss Shared/
└─ wwwroot/
   ├─ css/startcompass.css      all stil (CSP-en tillater ingen inline)
   ├─ js/                       survey.js (skjema, faner, søk), lineup.js, site.js
   └─ lib/                      Bootstrap, jQuery og jquery-validation, lokalt

docs/                           én fil per funksjon, database.md, og utkastet til Sikt-melding
scripts/database/               hoveddatabasen: rollene, rettighetene til app-rollen, backup
scripts/identity/               StatsBomb-PDF → kampdata og foreløpige målområder
scripts/squads/                 troppene fra ikstart.no til Data/Squads
docker-compose.yml              den lokale utviklingsdatabasen (+ .env.example)
.github/workflows/ci.yml        build på push og pull request
```

---

## Hvem eier hva

| Person | Mapper og filer |
| --- | --- |
| **Kristian** | `Models/`, `Data/`, `Authorization/`, `Program.cs`, `AdminController` |
| **Victor** | `SurveyController` |
| **Taavi** | `CoachController`, `Views/Shared/_Layout.cshtml` |
| **Brage** | `GuardianController`, `PlayerController`, `ConsentService`, `SeedData` |

Views-mappene følger controlleren: eier du `CoachController`, eier du `Views/Coach/`.

### Migrasjoner

**Alle kan lage en migrasjon. Si fra i kanalen før du gjør det.** To migrasjoner laget hver
for seg mot samme modell gir en konflikt i `AppDbContextModelSnapshot.cs` som er vond å rydde
opp i — snapshotten er én stor generert fil, og git klarer ikke å flette den fornuftig.

```bash
dotnet ef migrations add <Navn> --project StartPraksisGruppe3Prosjekt --output-dir Data/Migrations
```

I utvikling kjører appen migrasjonen selv ved neste `dotnet run`. I drift gjør den det ikke:
der kjører databaseeieren et migrasjonsskript. Se «Kjøre migrasjonene» i
[`docs/database.md`](docs/database.md).

Skjemaet ble laget på nytt 07.10.2026, som én `InitialCreate`, da databasen ble flyttet bort
fra Supabase. De åtte migrasjonene fra før er slettet, så en lokal database fra før den datoen
må lages på nytt.

---

## To valg som ser rare ut, men er med vilje

### 1. Samtykke er en hendelseslogg, ikke et felt

`ConsentEvent` er **append-only**. Gjeldende samtykke er den nyeste hendelsen for
spilleren. Et samtykke som trekkes tilbake legges inn som en *ny* hendelse med lavere
nivå — den gamle raden blir stående.

Grunnen: klubben må kunne dokumentere hva som var lov når. Et felt som overskrives
sletter nettopp den dokumentasjonen.

`AppDbContext.SaveChanges` kaster hvis noen prøver å endre eller slette en
`ConsentEvent`. Det er ikke en bug. Bruk `IConsentService.RecordAsync`.

### 2. Avviket lagres aldri

Avstanden mellom to respondenters svar regnes ut fra råsvarene hver gang en side vises. Det
finnes ingen kolonne for den, og det skal ikke komme en heller. Det gjelder differanseskårene
i 5C (`Services/FiveC/FiveCDifference.cs`).

Grunnen: et lagret avvik er en påstand om en mindreårig som blir liggende igjen etter at
svarene er rettet, samtykket er trukket eller runden er over.

Negativt formulerte påstander skåres som `6 - verdi`, slik at høyt alltid betyr bra. Regelen
bor ett sted, `FiveCRules.Score`, og `6 -` skal ikke skrives andre steder. Påstanden merkes
`reversed: true` i `five-c-questions.json`. Ingen av de 25 er merket i dag.

---

## Autorisasjon

Rolle alene avgjør ikke alt. En foresatt er ikke foresatt *for alle*, så tilgangen er
ressursbasert: policyene vurderer én konkret spiller eller ett konkret lag.

**`CanViewPlayer`** (`AuthorizationHandler<CanViewPlayerRequirement, Player>`)

| Hvem | Får se spilleren |
| --- | --- |
| Admin | alltid |
| Spilleren selv | `player.UserId` er innlogget bruker |
| Foresatt | bare hvis en `Guardianship` knytter brukeren til *denne* spilleren |
| Trener | alltid — ikke lagavgrenset, og ikke lenger samtykkeavgrenset |
| Alle andre | nei |

### Samtykke stanser ikke lenger en trener

Dette er den største endringen i modellen, og den er verdt å lese to ganger.

Tidligere måtte en trener ha `ConsentEvent = Full` for å se en enkeltspiller i det hele
tatt. Klubben ba om at trenere alltid skal kunne åpne en spillerside, og det er det som nå
gjelder. Samtykket styrer fortsatt hva opplysningene kan brukes til utenfor appen, og det
må fortsatt stemme i Sikt-meldingen — men det er ikke lenger det som hindrer en trener i å
åpne en side.

**Det som erstatter den, er etterprøvbarhet i stedet for hindring.** Hvert oppslag på en
enkeltspillers svar skriver en rad i `PlayerAccessEvent`: hvem, hvilken spiller, hvilken
side, når. Loggen er append-only som samtykkeloggen. Slutter den å skrives, står regelen i
`CanViewPlayerHandler` uten motvekt — så en ny side som viser én spillers svar **skal**
kalle `IPlayerAccessLog.RecordAsync`.

**`CanViewTeam`** (`AuthorizationHandler<CanViewTeamRequirement, Team>`) — admin eller
trener. Et lag er i seg selv bare et navn og en liste med spillere; enkeltsvarene er
vernet av `CanViewPlayer` og loggen over.

**`CanViewTeamAggregate`** — trener eller admin, uten lagavgrensning: trenere er ikke
knyttet til lag. I tillegg:
snittet vises ikke hvis færre enn **3** besvarelser ligger bak det, ellers kan tallet
regnes tilbake til enkeltpersoner. Grensen er `CanViewTeamAggregateRequirement.MinimumResponses`,
og den er en del av ressursen (`TeamAggregateResource`) nettopp for at ingen skal kunne
glemme å sjekke den.

### Mønsteret alle skal følge

Hver action som tar imot en spiller-ID:

```csharp
var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == id);
if (player is null) return NotFound();

var authorized = await _authz.AuthorizeAsync(User, player, Policies.CanViewPlayer);
if (!authorized.Succeeded) return Forbid();
```

Ferdig eksempel: `CoachController.FiveCPlayer`.

`[Authorize(Roles = ...)]` slipper deg inn i controlleren og sier ingenting om hvilke
spillere du får se. Ikke skriv rolle- eller lagsjekker for hånd i controlleren — reglene
skal bo ett sted, i `Authorization/`.

`ConsentService.GetCurrentLevelAsync` er ferdig implementert med vilje. Ikke gjør den om til
en stub. `ConsentService.GetCurrentLevelsAsync` (flertall) er også implementert — lagoversikten
lister en hel tropp og ville ellers gjort ett oppslag per spiller.

---

## Sikkerhet: rammene koden skrives innenfor

Ligger i `Security/` og settes opp i `Program.cs`. Dette er på plass fra nå, så det
er noe å skrive kode *innenfor* — ikke noe som skrus på til slutt.

### CSP — skript og stil må ligge i egne filer

Svarene har en Content-Security-Policy uten `unsafe-inline`. I praksis:

- `<script>alert(1)</script>` rett i en view kjører **ikke**. Legg JavaScript i en fil
  under `wwwroot/js/` og referer til den.
- `<style>`-blokker og `style="..."`-attributter i markup blokkeres. All stil ligger i
  `wwwroot/css/startcompass.css`.
  (JavaScript som setter `element.style.x` er fortsatt greit — det er Bootstrap avhengig av.)
- Må du absolutt ha et inline-skript, gi det nonce-en for forespørselen:

  ```cshtml
  <script nonce="@Context.GetCspNonce()">…</script>
  ```

- Bilder fra `data:`-URI-er er tillatt, fordi Bootstrap legger ikoner i CSS-en. Alt annet
  må komme fra vårt eget domene: ingen CDN-er, ingen Google Fonts.

Ser du en tom side og en CSP-feil i konsollen, er det denne regelen. Skru den ikke av —
`Security:Headers:ReportOnly: true` i `appsettings.Development.json` lar deg feilsøke
med policyen i rapportmodus, men koden skal fungere med den håndhevet.

Kjent begrensning: 2FA-siden i Identity UI (`EnableAuthenticator`) har et inline-skript
i pakken som CSP-en blokkerer. Tofaktor er ikke i bruk her; skal det tas i bruk, må siden
scaffoldes og skriptet få nonce.

Razor koder fortsatt alt som skrives med `@`. CSP-en er nettet under — den erstatter ikke
regelen om at `Html.Raw` ikke brukes på noe en bruker har skrevet.

### Antiforgery er på overalt

`AutoValidateAntiforgeryTokenAttribute` er registrert globalt. Alle POST/PUT/DELETE mot
en controller krever token, uten at noen må huske attributtet. Bruk `<form asp-action="…">`
— tag helperen legger inn tokenet selv. Trenger du unntak, må det være et bevisst
`[IgnoreAntiforgeryToken]` som synes i en pull request.

### Rate limiting

- Alle forespørsler: 240 per minutt per IP-adresse.
- POST mot `/Identity/Account/*`: 10 per fem minutter per IP. Kontolåsingen i Identity
  beskytter én konto; denne hindrer at noen prøver ett passord mot hundre kontoer.
- Egen policy for dyre eller endrende actions:

  ```csharp
  [EnableRateLimiting(RateLimitPolicies.Sensitive)]  // 30 per minutt per IP
  ```

  Verdt å sette på innsending av skjema, søk og eksport.

Avviste forespørsler får `429` med `Retry-After` og logges med IP, metode og sti.

### Nekt som standard

`FallbackPolicy` i `Program.cs` krever innlogging på alle endepunkter som ikke sier noe
annet. En ny controller uten `[Authorize]` er altså ikke åpen — den krever innlogging.
Det som faktisk skal være åpent, må merkes `[AllowAnonymous]`, og i dag er det bare
`HomeController` (forside, personvern og de to feilsidene).

Fallbacken erstatter ikke `[Authorize(Roles = ...)]` og slett ikke de ressursbaserte
policyene. Den sier bare «innlogget», ikke «innlogget som riktig person».

### 404 og 403 har egen side

Et tomt 4xx- eller 5xx-svar kjøres om igjen som `HomeController.Status`
(`UseStatusCodePagesWithReExecute` i `Program.cs`), som viser `Views/Home/Status.cshtml` og
beholder statuskoden. `Forbid()` svarer 403 på adressen det ble spurt etter, i stedet for å
sende brukeren til Identity-pakkens `AccessDenied`-side.

Fallbacken gjelder også adresser uten noe endepunkt bak. En ukjent adresse sender derfor en
uinnlogget bruker til innlogging, og 404-siden kommer først etterpå. Uinnlogget ser man den
bare på de stengte registreringsadressene. En følge er at svaret ikke røper hvilke adresser
som finnes.

### Innloggingssiden er vår egen

`Areas/Identity/Pages/Account/Login.cshtml` overstyrer siden fra Identity UI-pakken. To
grunner, og ingen av dem lot seg løse utenfra:

- Pakkesiden er et bart Bootstrap-skjema som ikke ligner resten av appen, og markupen lar
  seg ikke restyle langt nok med CSS alene.
- Pakkesiden tilbyr «Register as a new user», som her er en lenke til 404 — selvregistrering
  er stengt i middleware. En død lenke på innloggingssiden er det første nye brukere møter.
  Vår side har ikke lenken.

`Areas/Identity/Pages/_ViewStart.cshtml` peker resten av Identity-sidene på vårt eget
layout, så de arver header, footer og palett selv om de fortsatt er pakkens versjoner.
Noen få Bootstrap-overstyringer i `startcompass.css` tar resten.

Eksterne innloggingsleverandører er utelatt med vilje: ingen er satt opp, og pakkesidens
«det er ingen eksterne tjenester konfigurert»-blokk er ikke noe å vise en trener.

### Selvregistrering er stengt

`/Identity/Account/Register` og de tilhørende sidene svarer `404`
(`Security/ClosedRegistrationExtensions.cs`). Kontoer opprettes av klubben — en åpen
registrering på et system med opplysninger om mindreårige er et hull uansett hvor god
autorisasjonen bak er.

Sidene stenges i middleware og ikke med en policy, fordi Identity UI-sidene har
`[AllowAnonymous]` i selve pakken, og AllowAnonymous slår enhver policy vi legger på
utenfra. Ingenting i appen lenker dit: innloggingssiden er vår egen og har ingen
registreringslenke (se over).

### Cookies og hoder ellers

Sesjons- og antiforgery-cookies er `HttpOnly`, `SameSite=Strict` og https-only utenfor
utvikling. Sesjonen varer to timer med glidende utløp. HTML-svar til innloggede brukere
sendes med `no-store` — sidene skal ikke ligge igjen i nettleseren på en delt PC.

I tillegg: `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`,
`Permissions-Policy` uten kamera/mikrofon/posisjon, COOP/CORP `same-origin`, og HSTS i ett
år utenfor utvikling. Serverhodet er fjernet.

Kjøres appen bak en proxy, må proxyens IP-adresse inn i `ForwardedHeaders:KnownProxies`
(`appsettings.json` eller miljøet). `Program.cs` leser `X-Forwarded-For` og
`X-Forwarded-Proto`, men bare fra adressene som står der, og ellers bare fra en proxy på
samme maskin. Mangler oppføringen, ser rate limiteren bare proxyens IP-adresse, og
HTTPS-omdirigeringen ser en http-forespørsel.

### Databasetilkoblingen verifiseres, ikke bare krypteres

I drift kobler appen til hoveddatabasen med `SSL Mode=VerifyFull`. **Utenfor Development
nekter appen å starte med en tilkobling som ikke verifiserer sertifikatet**, med mindre
databasen står på samme maskin (`localhost`, en loopback-adresse eller en Unix-socket).
Regelen ligger i `Data/DatabaseConnection.cs`.

Grunnen er at `Require` ikke betyr det navnet antyder. Fra og med Npgsql 8 følger `Require`
libpq: den *krever kryptering* og **verifiserer ikke** sertifikatet. `Trust Server Certificate`
er i samme slengen merket obsolete med «no longer needed and does nothing». Det som faktisk
verifiserer, er `VerifyCA` (signatur) og `VerifyFull` (signatur + vertsnavn).

Målt 07.10.2026 mot en lokal PostgreSQL 17 som bare tar imot TLS (TLS 1.3), med et
serversertifikat utstedt til `localhost` av en egen test-CA som ikke ligger i maskinens
rotlager, og Npgsql 10.0.3, som er versjonen prosjektet drar inn. Npgsql 8.0.6 ga de samme
svarene før oppgraderingen:

| Tilkobling | Resultat |
| --- | --- |
| `Require` | kobler til, kryptert, **ingen** verifisering |
| `VerifyCA` / `VerifyFull` uten `Root Certificate` | avvist — kjeden ender i en rot maskinen ikke kjenner |
| `VerifyFull` + `Root Certificate` | kobler til, verifisert |
| `VerifyFull` + `Root Certificate` til en annen CA | avvist |
| `VerifyCA` + `Root Certificate`, feil vertsnavn (`127.0.0.1`) | kobler til — `VerifyCA` ser ikke på vertsnavnet |
| `VerifyFull` + `Root Certificate`, feil vertsnavn (`127.0.0.1`) | avvist |

`Root Certificate=<sti til CA-fila>` trengs bare når serverens CA ikke ligger i maskinens
rotlager, for eksempel en CA klubben har laget selv. Med et sertifikat fra en offentlig CA er
`SSL Mode=VerifyFull` nok. Stien må være full og bokstavelig: Npgsql utvider ikke
miljøvariabler.

I utvikling går tilkoblingen til `localhost`, uten TLS. Det er bare oppdiktede data på den.

### Engangskommandoene

Tre ting gjøres fra kommandolinja og ikke fra en side: `create-admin` (første administrator i
en ny database), `import-players` og `export-players` (de ekte spillerne mellom to
databaser). De kjører før appen migrerer eller seeder, og starter ikke webserveren. Se
[`docs/database.md`](docs/database.md).

`create-admin` leser passordet fra standard input og aldri fra argumentlista, følger de samme
passordreglene som resten av appen, og nekter hvis det finnes en administrator fra før: den
er en vei inn i en tom database, ikke en bakdør forbi appens egen brukeradministrasjon.

### Data Protection-nøklene har et fast sted

Nøklene som krypterer innloggingscookien og antiforgery-tokenene, lagres i mappa
`DataProtection:KeysPath`. Utenfor Development er den påkrevd, og appen starter ikke uten.
Uten et fast sted lages nøklene på nytt når appen flyttes eller starter under en annen
bruker, og da logges alle ut.

---

## Ting som må avklares før ekte data

- [~] Melding til Sikt — utkast i [`docs/sikt-melding.md`](docs/sikt-melding.md). Sju punkter
      gjenstår, og fire av dem er klubbens å svare på. Utkastet beskriver fortsatt
      Supabase og spillerkoder; hva som må rettes, står sist i
      [`docs/database.md`](docs/database.md)
- [x] **Spillerne legges inn før meldingen til Sikt er sendt.** Avgjort i prosjektgruppa
      07.10.2026: prosjektet er fortsatt under utvikling, og gruppa vurderer at melding til
      Sikt hører til større prosjekter, som en bacheloroppgave. Det gjelder navn, fødselsdato
      og bilde, som klubben har gitt tillatelse til å bruke
- [ ] Avklar med veileder om UiA krever melding til Sikt også for dette prosjektet.
      Personvernforordningen gjelder uansett om Sikt er meldt eller ikke: klubben er
      behandlingsansvarlig, og trenger et grunnlag for behandlingen, informasjon til spillere
      og foresatte, og databehandleravtaler (se «Drift: hvem gjør hva» i
      [`docs/database.md`](docs/database.md))
- [ ] Driften er ikke bestemt: hvem som drifter serveren, hvem som er databaseeier og
      administratorer, og avtalene. Forslagene står i «Drift: hvem gjør hva» i
      [`docs/database.md`](docs/database.md). Backup er daglig i 30 dager
      (`scripts/database/backup.sh`)
- [ ] Personvernerklæring (`Views/Home/Privacy.cshtml`)
- [ ] **Hoveddatabasen er ikke opprettet.** Koden og oppskriften er klare
      ([`docs/database.md`](docs/database.md)); serveren, databehandleravtalen og backupen
      er klubbens
- [ ] De ekte spillerne ligger fortsatt i den gamle Supabase-databasen. De flyttes med
      `export-players` og `import-players`, og Supabase slås av etterpå
- [x] Selvregistrering stengt — kontoer opprettes av klubben. Den første administratoren i en
      ny database lages med `create-admin`, og resten på `/Admin/Users`: trenere,
      administratorer, spillere og foresatte, med et midlertidig passord som må byttes ved
      første innlogging. Se [`docs/user-administration.md`](docs/user-administration.md)
- [ ] Kontoene til spillere og foresatte i hoveddatabasen trenger e-postadresser fra klubben.
      Det er nye personopplysninger, og må med i informasjonen til de registrerte
- [x] `AllowedHosts` er ikke lenger `*`. `appsettings.json` slipper bare gjennom lokale navn,
      `appsettings.Development.json` beholder `*` for utvikling, og produksjon setter det
      faktiske vertsnavnet i miljøet (`AllowedHosts=…`). Står den likevel på `*` utenfor
      utvikling, sier `Program.cs` fra i loggen ved oppstart
- [ ] Identity UI lar en bruker slette sin egen konto på
      `/Identity/Account/Manage/DeletePersonalData`. Det går utenom sletterutinen i
      `AdminController.Delete`: kontoen forsvinner, men spilleren og alt som er lagret om
      hen, blir stående (fremmednøkkelen setter `players.user_id` til null). Avklar om siden
      skal stenges eller om sletting skal gå gjennom den
- [x] Revisjonslogg for oppslag på enkeltspillere — `PlayerAccessEvent` og
      `IPlayerAccessLog`. Admin-visningen av loggen er fortsatt TODO
- [x] Skal spilleren se trenerens svar og avviket? Avgjort: ja, men først når treneren
      frigir dem. Se «Samtaleflyten» i [`docs/five-c.md`](docs/five-c.md)
- [ ] **Samtykke stanser ikke lenger en trener.** Dette må inn i Sikt-meldingen og i
      personvernerklæringen: trenere ser alle spillere, og det som dokumenterer bruken er
      revisjonsloggen. Klubben bør bekrefte at det er slik de vil ha det
- [ ] Foresatt ser det samme som spilleren, også for myndige spillere over 19. Vurder om
      det burde følge `PlayerRules.GuardianRequiredBelowAge`
- [x] Regelen om foresatt for spillere under 19 håndheves når kontoer opprettes: en spiller
      under 19 får ikke konto før en foresatt er registrert, og den siste foresatte kan ikke
      fjernes (`AccountAdministration`). Spillere uten konto, som de som kommer inn med
      `import-players`, har ingen foresatt før noen registrerer en
- [~] Appen sender ikke e-post: ingen `IEmailSender` er registrert. Glemt passord er løst uten
      e-post: en administrator gir kontoen et nytt midlertidig passord, lenken på
      innloggingssiden er borte, og sidene for «glemt passord» er stengt. Det som gjenstår, er
      e-postbytte under «My account», som fortsatt ser ut til å virke uten å gjøre det
- [ ] `TempData` legger en fjerde cookie, `.AspNetCore.Mvc.CookieTempDataProvider`, med
      kvitteringsmeldinger som navngir spilleren. Den er kryptert, men har ikke fått navn
      og herding som de to andre, og mangler i cookie-oversikten i Sikt-meldingen
- [x] Oppgradert til .NET 10 (LTS, støttet til november 2028) og pakkene i 10.0-serien.
      .NET 8 går ut av støtte 10. november 2026. Se «Stack»
