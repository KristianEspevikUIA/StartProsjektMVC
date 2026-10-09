# StartPraksisGruppe3Prosjekt

StartCompass, et internt verktøy for IK Start. Spillere, foresatte og trenere svarer på de
samme 25 påstandene om de fem C-ene, og systemet viser hvor bildene deres av samme spiller
skiller seg. Trenerne har i tillegg Identity Benchmarking og succession planning.

**Systemet behandler personopplysninger om mindreårige.** Det er premisset bak alle
valgene under, og det er grunnen til at autorisasjon ikke er noe som skrus på til slutt.

> Ingen ekte spillerdata i dette repoet: det er offentlig. Appen bruker bare ekte data --
> klubbens tropper fra ikstart.no og trenernes egne succession-ark, med klubbens tillatelse --
> og de ligger i git-ignorerte mapper og i databasen, aldri i git. Ingenting er oppdiktet.

---

## Status

**Bygget og i bruk:** 5C-spørreskjemaet (25 påstander i fem kategorier, pluss
refleksjonen som avslutter perioden), skjemalisten med
filtre, treneroversikten med sammenligning og oppfølgingsvarsel, lagoversikten med snitt per
kategori og påstand, utvikling over tid for både spiller og lag, søk i troppen, samtaleflyten
mellom spiller og trener, spiller- og foresattsiden, revisjonsloggen, admin-siden for
perioder, GDPR-innsyn og -sletting i `AdminController` (`/Admin/Export/{id}` og
`/Admin/Delete/{id}`, begge per spiller, med lenker fra spillerlista på `/Admin/Players`),
Identity Benchmarking, og **succession planning** — trenernes Excel-ark som sider i appen
(se under).

**Fortsatt TODO:** den eldre ti-påstandsvisningen (`CoachController.Team`, `PlayerDetail`,
`Search` og `ScoringService`), samtykkeskjemaet for foresatte, og brukeradministrasjon i
`AdminController`. Samtykkesiden finnes på `/Guardian/Consent/{id}`, men ingenting lenker
dit, og nivået som velges, styrer ingenting — se A1 og A2 i
[`docs/sikt-melding.md`](docs/sikt-melding.md).

Sidene som ikke er bygget, er **ikke lenket til** fra menyen eller fra admin-forsiden. De
står på lista der som «Not built yet», og selve siden forteller hvem som eier arbeidet, hva
den skal gjøre, og har en vei tilbake — se `Views/Shared/_NotBuiltYet.cshtml`. En lenke som
fører til en tom side koster et klikk å oppdage, og det er verre enn ingen lenke.

`dotnet build` kjører rent, og `dotnet run` migrerer databasen og legger inn seed-data.

---

## Kom i gang

Databasen er **Postgres i Supabase** (byttet fra SQLite 26.08.2026). Tilkoblingsstrengen står
i `appsettings.json`, men **uten passord** — passordet er en hemmelighet og skal ikke i repoet.

**Steg 1: hent CA-sertifikatet.** Supabase signerer databasesertifikatet med sin egen CA, og
den ligger ikke i maskinens rotlager. Uten den kommer du ikke gjennom. Last den ned i Supabase
under *Project Settings → Database → SSL Configuration* («Download certificate»; fila heter
typisk `prod-ca-2021.crt`) og legg den et fast sted på egen maskin, f.eks.
`%APPDATA%\Supabase\prod-ca-2021.crt`. Fila er offentlig og inneholder ingen hemmelighet, men
den ligger likevel ikke i repoet: et rotsertifikat er et tillitsanker, og det skal hentes
gjennom en innlogget kanal — ellers vet du ikke at det faktisk er Supabase sitt.

**Steg 2: legg inn databasepassordet og stien til CA-en.** Hent «Database password» i Supabase
under *Project Settings → Database* (finner du det ikke, kan det resettes samme sted — men si
fra til de andre først, en reset gjelder alle). Deretter, med hele strengen fra
`appsettings.json` pluss `;Root Certificate=…` og `;Password=…` på slutten:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=aws-1-eu-west-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.fwurrryuqktamabroagx;SSL Mode=VerifyFull;Root Certificate=%APPDATA%\Supabase\prod-ca-2021.crt;Password=DITT_PASSORD" --project StartPraksisGruppe3Prosjekt
```

**`DITT_PASSORD` er en plassholder.** Bytt den ut med databasepassordet før du kjører kommandoen.
Står plassholderen igjen, stopper appen med beskjed om nettopp det.

Passordet havner i `%APPDATA%\Microsoft\UserSecrets\`, ikke i git. Uten dette steget stopper
appen med en melding som forklarer akkurat dette — det er ikke en bug.

User-secrets erstatter hele strengen fra `appsettings.json`, den legges ikke oppå. Derfor skal
hele strengen med, også `SSL Mode=VerifyFull`: en secret uten den kobler til uten å verifisere
sertifikatet, uansett hva som står i repoet.

**Stien til sertifikatet.** Appen utvider `%APPDATA%` og `~` selv, så strengen over virker som
den står. Ligger fila på standardplassen -- `%APPDATA%\Supabase\prod-ca-2021.crt` på Windows,
`~/.config/Supabase/prod-ca-2021.crt` på Mac og Linux -- bruker appen den også når stien i
strengen er feil, og sier fra i loggen. Se `Data/DatabaseConnection.cs`.

Går det galt, sier feilen hvilken av de to tingene som er feil:

| Feilmelding | Hva som er galt |
| --- | --- |
| «Fant ikke CA-sertifikatet til Supabase …» | fila finnes verken der `Root Certificate` peker eller på standardplassen. Last den ned (steg 1) |
| «Passordet … er plassholderen DITT_PASSORD» | bytt ut plassholderen med det ekte passordet (steg 2) |
| «The remote certificate was rejected…» | fila finnes, men er ikke Supabase-CA-en |

Merk: **anon-/publishable-nøkkelen (`sb_publishable_…`) er ikke databasepassordet.** Den
gjelder REST-API-et. En direkte Postgres-tilkobling krever passordet til `postgres`-rollen.

Bruk port **5432** (session-pooleren). Port 6543 er transaction-pooleren, og den fungerer
ikke med EF-migrasjoner. Session-pooleren tar bare **15 klienter for hele prosjektet**, delt
mellom alle som kjører appen. Appen bruker derfor høyst 5 tilkoblinger mot den (sett
`Maximum Pool Size` i strengen for noe annet). Får du «EMAXCONNSESSION max clients reached», er
de andres apper eller gamle prosesser hos deg det som holder dem: stopp dem og prøv igjen.

**Steg 3: kjør.**

```bash
dotnet run --project StartPraksisGruppe3Prosjekt
```

Første kjøring kjører migrasjonene og legger inn klubbens egne data, når filene finnes: troppene
(`python3 scripts/squads/fetch_squads.py`) og trenernes succession-ark (se
`docs/succession-planning.md`). Uten dem starter appen uten spillere. Ingenting diktes opp.

### Demokontoer

Opprettes bare i `Development`, og først når seedingen har fått kjørt mot databasen.
**Standard passord er `Dev!passord1`** for alle kontoene under. Det kan overstyres:

```bash
dotnet user-secrets set "Seed:DevPassword" "ditt-passord" --project StartPraksisGruppe3Prosjekt
```

Overstyringen virker bare på kontoer som ikke finnes fra før — `SeedData.EnsureUserAsync`
oppretter, den endrer ikke passord. Nå som databasen er delt, betyr det at den som seedet
først bestemmer passordet for alle.

**Det er én trenerkonto for lagene.**
Den andre (`trener.ungdom@ikstart.example`) er slått sammen inn i
den gjenværende: lagene ble flyttet over, og kontoen fjernet. Sammenslåingen ligger i
`SeedData.ConsolidateCoachAsync` og kjører ved hver oppstart, ikke bare på en tom base —
den delte basen hadde begge kontoene lenge før steget fantes. Dukker kontoen opp i en
append-only logg, blir den låst i stedet for slettet, slik at loggen fortsatt kan si hvem
som gjorde hva.

| Konto | Rolle |
| --- | --- |
| `admin@ikstart.example` | Admin |
| `trener.senior@ikstart.example` | Trener (alle lag) |
| `spiller.<fornavn>.<etternavn>@ikstart.example` | Spiller, én per spiller i troppene |

Spillerkontoen utledes av navnet slik klubben skriver det: `Ola Nordmann` blir
`spiller.ola.nordmann@ikstart.example`, med æ, ø og å skrevet ae, o og aa.

**Ingen foresatte og ingen samtykker.** Hver mindreårig hadde før en oppdiktet foresatt på
example.test, og et samtykke den foresatte aldri ga. De er fjernet, og nye legges ikke inn:
foresatte og samtykker registreres av klubben. Oppstarten sier i loggen hvor mange mindreårige
som mangler foresatt.

**Succession planning** har ingen demodata: vurderingene er trenernes egne ark, importert med én
låst konto per trener (`trener.<initialer>@ikstart.example`), som ingen kan logge inn med. Logg inn
som `trener.senior@ikstart.example` for å se dem. Se `docs/succession-planning.md`.

**Lagene er G14, G15, G17 og G19**, med spillerne fra klubbens sider, pluss noen trenerne vurderer
som ikke står der (se `docs/player-welcome.md`). Lagene het U14, U15, U17 og U19 til 07.10.2026,
og døpes om på stedet ved neste oppstart (`SeedData.SeedTeamsAsync`).

**Oppdiktede data fra før** -- spillere, foresatte, samtykker, 5C-svar, demoperioder og
succession-vurderinger -- slettes ved hver oppstart i Development, så den delte basen blir ryddet
neste gang noen starter appen (`SeedData.RemoveMadeUpDataAsync`,
`SeedSuccessionImport.RemoveMadeUpAsync`).

Vil du begynne på nytt: tøm `public`-skjemaet i Supabase (inkludert `__EFMigrationsHistory`)
og kjør appen igjen. Det rammer alle på prosjektet, så si fra i kanalen først.

---

## Stack

- ASP.NET Core MVC, **.NET 10 (LTS)**
- EF Core 10, code-first, **Postgres i Supabase** (Npgsql)
- ASP.NET Core Identity med roller

Om rammeverkversjonen: prosjektet står på `net10.0` fordi .NET 10 er en LTS-utgave (støttet til
november 2028). Rammeverket står tre steder: `<TargetFramework>` i csproj-filen, pakkeversjonene
og `dotnet-version` i `.github/workflows/ci.yml`. Bytt alle tre i én samlet endring, ikke midt i
en feature. Verktøyet `dotnet-ef` bør ha samme hovedversjon som EF-pakkene.

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
- **Lagring:** appens egen database (`FiveCSubmissions`, `FiveCAnswers` og
  `FiveCReflectionAnswers`). Avvik og snitt regnes ut ved hver visning og lagres aldri.
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

- **Hver trener vurderer hver spiller hver åttende uke:** seks vurderinger fra 0 til 10,
  posisjoner, kategori, prognoser og notater, altså de samme kolonnene som arket. Vurderingene
  lagres hver for seg, og å vurdere på nytt i samme syklus er en retting.
- **Squad board** legger trenerne sammen: én rad per spiller, med arkets fargeskala, og en fane
  med spillerne der trenerne er uenige.
- **Best eleven** plukker de beste i 3-5-2 (eller 4-3-3, 4-2-3-1) fra de vurderte spillerne, og
  viser hvem som er nestemann i hver posisjon.
- **Off og uker til klar:** hvor langt unna 8 spilleren er, og hvor mange uker det tar med
  trenden så langt.
- **Lister og terskler** ligger i `Data/Succession/succession-planning.json`, validert ved oppstart.
- **Import av trenernes ark:** `scripts/succession/import_workbooks.py` matcher radene mot de
  ekte troppene og tar med alt trenerne skrev, også friteksten. Appen leser resultatet inn i
  Development. Arkene og resultatet ligger i den git-ignorerte `Data/Succession/Import/`, aldri i
  repoet. «Best eleven» viser fornavnet klubben har lagt inn til velkomsten, og ellers hele navnet.

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
- **Fornavn og bilde** ligger i egen tabell (`PlayerPersonalDetails`). Ingen bilder eller navn
  ligger i repoet; de hentes fra klubbens sider til den git-ignorerte `Data/Squads/`.
- **Må avklares:** Sikt-meldingen beskriver fortsatt spillerkoder i stedet for navn. Den må
  oppdateres og sendes før ekte navn og bilder legges inn.

**Alt om dette: [`docs/player-welcome.md`](docs/player-welcome.md).**

## Struktur

```
StartPraksisGruppe3Prosjekt/
├─ Program.cs                   tjenester, pipeline og sjekkene som kjører ved oppstart
├─ Controllers/
│  ├─ HomeController.cs         forside, personvern og feilsider (åpne uten innlogging)
│  ├─ SurveyController.cs       skjemaliste, utfylling, lagring
│  ├─ CoachController.cs        lagoversikt, søk, spillerdetalj
│  ├─ PlayerController.cs       spiller ser egne svar
│  ├─ GuardianController.cs     foresatt ser eget barn
│  ├─ AdminController.cs        perioder, spillere, innsyn og sletting
│  ├─ IdentityController.cs     Identity Benchmarking
│  ├─ SuccessionController.cs   succession planning
│  └─ HelpController.cs         hjelpesiden: hvordan tallene skal leses
├─ Areas/Identity/Pages/        vår egen innloggingsside; resten er Identity UI-pakkens
├─ Authorization/               policyer, krav og handlere
├─ Security/                    sikkerhetshoder og CSP, rate limiting, stengt registrering
├─ Contracts/FiveC/             det 5C-skjemaet sender inn, i C# og TypeScript
├─ Models/                      entiteter, enums og PlayerRules
├─ Data/
│  ├─ AppDbContext.cs
│  ├─ Migrations/
│  ├─ Questions/                five-c-questions.json (påstander og refleksjon)
│  ├─ Identity/                 gold-standard.json (+ Matches/, git-ignorert)
│  ├─ Succession/               succession-planning.json (lister, terskler, formasjoner)
│  ├─ DatabaseConnection.cs     tilkoblingsstrengen: sertifikatstien og plassholderne
│  ├─ SeedData.cs               roller, lag, perioder, innlogging, og opprydding i det oppdiktede
│  ├─ SeedSquads.cs             klubbens tropper, fra den git-ignorerte Squads/
│  └─ SeedSuccessionImport.cs   trenernes succession-ark, fra den git-ignorerte Succession/Import/
├─ Services/
│  ├─ IScoringService.cs + ScoringService.cs
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

docs/                           én fil per funksjon, pluss utkastet til Sikt-melding
scripts/identity/               StatsBomb-PDF → kampdata og foreløpige målområder
.github/workflows/ci.yml        build på push og pull request
```

---

## Hvem eier hva

| Person | Mapper og filer |
| --- | --- |
| **Kristian** | `Models/`, `Data/`, `Authorization/`, `Program.cs`, `AdminController` |
| **Victor** | `SurveyController`, `ScoringService` |
| **Taavi** | `CoachController`, `Views/Shared/_Layout.cshtml` |
| **Brage** | `GuardianController`, `PlayerController`, `ConsentService`, `SeedData` |

Views-mappene følger controlleren: eier du `CoachController`, eier du `Views/Coach/`.

### Migrations: bare én person genererer dem

**Bare Kristian kjører `dotnet ef migrations add`.** To personer som genererer
migrasjoner mot samme modell gir konflikter i `AppDbContextModelSnapshot.cs` som er
vonde å rydde opp i — snapshotten er én stor generert fil, og git klarer ikke å flette
den fornuftig.

Trenger du en modellendring: si ifra, så lages migrasjonen én gang. Resten kjører bare

```bash
dotnet ef database update --project StartPraksisGruppe3Prosjekt
```

Migrasjonene ble generert på nytt for Postgres 26.08.2026. Den gamle
`InitialCreate` var laget for SQLite og ville ikke ha gitt et brukbart skjema på
Postgres — identity-kolonnene mangler, og `DateTimeOffset` ville havnet i `text`.

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
i 5C (`Services/FiveC/FiveCDifference.cs`) og avviket (D) i den eldre ti-påstandsvisningen
(`ScoringService`, der utregningen fortsatt er TODO).

Grunnen: et lagret avvik er en påstand om en mindreårig som blir liggende igjen etter at
svarene er rettet, samtykket er trukket eller runden er over.

Negativt formulerte påstander skåres som `6 - verdi`, slik at høyt alltid betyr bra. Regelen
bor ett sted per skjema, og `6 -` skal ikke skrives andre steder:

- **5C:** `FiveCRules.Score`. Påstanden merkes `reversed: true` i `five-c-questions.json`.
  Ingen av de 25 er merket i dag.
- **De ti eldre påstandene:** `ScoringService.ScoreOf`. Nummer 5 er reversert
  (`IsReversed = true`).

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

**`CanViewTeamAggregate`** — trener eller admin, uten lagavgrensning: `CoachTeam` ligger
fortsatt i modellen, men ingen policy ser på den lenger. I tillegg:
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

Ferdig eksempel: `CoachController.PlayerDetail`.

`[Authorize(Roles = ...)]` slipper deg inn i controlleren og sier ingenting om hvilke
spillere du får se. Ikke skriv rolle- eller lagsjekker for hånd i controlleren — reglene
skal bo ett sted, i `Authorization/`.

To småting som er ferdig implementert med vilje, fordi autorisasjonen faller uten dem:
`ConsentService.GetCurrentLevelAsync` og `ScoringService.ScoreOf`. Ikke gjør dem om til
stubs. `ConsentService.GetCurrentLevelsAsync` (flertall) er også implementert — lagoversikten
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

Strengen i `appsettings.json` bruker `SSL Mode=VerifyFull`, og hver enkelt legger til
`Root Certificate=<sti til Supabase-CA-en>` i sin egen user-secret. Se «Kom i gang», steg 1–2.

Grunnen er at `Require` ikke betyr det navnet antyder. Fra og med Npgsql 8 følger `Require`
libpq: den *krever kryptering* og **verifiserer ikke** sertifikatet. `Trust Server Certificate`
er i samme slengen merket obsolete med «no longer needed and does nothing» — flagget vi hadde
stående gjorde altså ingenting, og å fjerne det endret heller ingenting. Det som faktisk
verifiserer, er `VerifyCA` (signatur) og `VerifyFull` (signatur + vertsnavn).

Målt mot pooleren med Npgsql 8.0.6, før oppgraderingen til .NET 10:

| `SSL Mode` | Resultat |
| --- | --- |
| `Require` | kobler til, TLS 1.3, **ingen** verifisering |
| `VerifyCA` / `VerifyFull` uten CA | avvist — Supabase-kjeden ender i deres egen rot |
| `VerifyFull` + `Root Certificate` | kobler til, TLS 1.3, verifisert |

Kjeden er `*.pooler.supabase.com` → `Supabase Intermediate 2021 CA` → `Supabase Root 2021 CA`,
og rota er selvsignert og ligger ikke i noe rotlager. Derfor CA-fila: uten den er det ingen
forskjell på Supabase og en som står i veien.

---

## Ting som må avklares før ekte data

- [~] Melding til Sikt — utkast i [`docs/sikt-melding.md`](docs/sikt-melding.md). Sju punkter
      gjenstår, og fire av dem er klubbens å svare på
- [ ] Personvernerklæring (`Views/Home/Privacy.cshtml`)
- [x] Selvregistrering stengt — kontoer opprettes av klubben. Admin-siden som faktisk
      oppretter dem er fortsatt TODO i `AdminController.Users`
- [x] `AllowedHosts` er ikke lenger `*`. `appsettings.json` slipper bare gjennom lokale navn,
      `appsettings.Development.json` beholder `*` for utvikling, og produksjon setter det
      faktiske vertsnavnet i miljøet (`AllowedHosts=…`). Står den likevel på `*` utenfor
      utvikling, sier `Program.cs` fra i loggen ved oppstart
- [ ] Identity UI lar en bruker slette sin egen konto på
      `/Identity/Account/Manage/DeletePersonalData`. Det går utenom sletterutinen i
      `AdminController.Delete` og etterlater `Player.UserId` uten bruker. Avklar om siden
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
- [ ] Regelen om foresatt for spillere under 19 håndheves i seed-data, men ikke ennå
      ved registrering i `AdminController`
- [ ] Appen sender ikke e-post: ingen `IEmailSender` er registrert, så Identity bruker en
      avsender som ikke sender noe. «Forgotten your password?» på innloggingssiden og
      e-postbytte under «My account» ser derfor ut til å virke, men gjør det ikke, og
      siden der admin kunne satt nytt passord, er ikke bygget. Koble på e-post, eller fjern
      lenken og bygg admin-siden
- [ ] `TempData` legger en fjerde cookie, `.AspNetCore.Mvc.CookieTempDataProvider`, med
      kvitteringsmeldinger som navngir spilleren. Den er kryptert, men har ikke fått navn
      og herding som de to andre, og mangler i cookie-oversikten i Sikt-meldingen
