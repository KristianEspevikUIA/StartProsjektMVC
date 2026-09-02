# StartPraksisGruppe3Prosjekt

Verktøy for IK Start. Spillere, trenere og foresatte svarer på de samme ti påstandene
om rolleforståelse, trygghet og mestring. Systemet viser avviket mellom hva treneren
*tror* spilleren svarer og hva spilleren *faktisk* svarer.

**Systemet behandler personopplysninger om mindreårige.** Det er premisset bak alle
valgene under, og det er grunnen til at autorisasjon ikke er noe som skrus på til slutt.

> Bare oppdiktede data i dette repoet. Ekte spillerdata skal ikke inn før prosjektet
> er meldt til Sikt.

---

## Status

**Bygget og i bruk:** 5C-spørreskjemaet (25 påstander, fem kategorier), skjemalisten med
filtre, treneroversikten med sammenligning og oppfølgingsvarsel, lagoversikten med snitt per
kategori og påstand, utvikling over tid for både spiller og lag, søk i troppen, samtaleflyten
mellom spiller og trener, spiller- og foresattsiden, revisjonsloggen, og admin-siden for
perioder.

**Fortsatt TODO:** den eldre ti-påstandsvisningen (`CoachController.Team`, `PlayerDetail`,
`Search` og `ScoringService`), samtykkeskjemaet for foresatte, brukeradministrasjon og
GDPR-innsyn/sletting i `AdminController`.

Sidene som ikke er bygget, er **ikke lenket til** fra menyen eller fra admin-forsiden. De
står på lista der som «Not built yet», og selve siden forteller hvem som eier arbeidet, hva
den skal gjøre, og har en vei tilbake — se `Views/Shared/_NotBuiltYet.cshtml`. En lenke som
fører til en tom side koster et klikk å oppdage, og det er verre enn ingen lenke.

`dotnet build` kjører rent, `dotnet test` er grønn, og `dotnet run` migrerer databasen og
legger inn seed-data.

---

## Kom i gang

Databasen er **Postgres i Supabase** (byttet fra SQLite 26.08.2026). Tilkoblingsstrengen står
i `appsettings.json`, men **uten passord** — passordet er en hemmelighet og skal ikke i repoet.

**Steg 1: legg inn databasepassordet.** Hent «Database password» i Supabase under
*Project Settings → Database* (finner du det ikke, kan det resettes samme sted — men si fra
til de andre først, en reset gjelder alle). Deretter, med hele strengen fra `appsettings.json`
pluss `;Password=…` på slutten:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=aws-1-eu-west-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.fwurrryuqktamabroagx;SSL Mode=Require;Trust Server Certificate=true;Password=DITT_PASSORD" --project StartPraksisGruppe3Prosjekt
```

Passordet havner i `%APPDATA%\Microsoft\UserSecrets\`, ikke i git. Uten dette steget stopper
appen med en melding som forklarer akkurat dette — det er ikke en bug.

Merk: **anon-/publishable-nøkkelen (`sb_publishable_…`) er ikke databasepassordet.** Den
gjelder REST-API-et. En direkte Postgres-tilkobling krever passordet til `postgres`-rollen.

Bruk port **5432** (session-pooleren). Port 6543 er transaction-pooleren, og den fungerer
ikke med EF-migrasjoner.

**Steg 2: kjør.**

```bash
dotnet run --project StartPraksisGruppe3Prosjekt
```

Første kjøring kjører migrasjonene og legger inn oppdiktede demodata.

### Demokontoer

Opprettes bare i `Development`, og først når seedingen har fått kjørt mot databasen.
**Standard passord er `Dev!passord1`** for alle kontoene under. Det kan overstyres:

```bash
dotnet user-secrets set "Seed:DevPassword" "ditt-passord" --project StartPraksisGruppe3Prosjekt
```

Overstyringen virker bare på kontoer som ikke finnes fra før — `SeedData.EnsureUserAsync`
oppretter, den endrer ikke passord. Nå som databasen er delt, betyr det at den som seedet
først bestemmer passordet for alle.

**Det er én trenerkonto.** Den andre (`trener.ungdom@ikstart.example`) er slått sammen inn i
den gjenværende: lagene ble flyttet over, og kontoen fjernet. Sammenslåingen ligger i
`SeedData.ConsolidateCoachAsync` og kjører ved hver oppstart, ikke bare på en tom base —
den delte basen hadde begge kontoene lenge før steget fantes. Dukker kontoen opp i en
append-only logg, blir den låst i stedet for slettet, slik at loggen fortsatt kan si hvem
som gjorde hva.

| Konto | Rolle |
| --- | --- |
| `admin@ikstart.example` | Admin |
| `trener.senior@ikstart.example` | Trener (alle lag) |
| `spiller.ts0816@ikstart.example` m.fl. | Spiller |
| `foresatt1@example.test` … `foresatt7@example.test` | Foresatt |
| `foresatt.ts1019@example.test` m.fl. | Foresatt |

Spillerkontoen utledes av koden: `TS-08-16` blir `spiller.ts0816@ikstart.example`. De fire
kontoene som ble seedet for hånd tidligere følger allerede den regelen, så de gjenkjennes og
ingen må lære seg en ny innlogging. Foresatte følger samme regel — `foresatt.ts1019@example.test`
— bortsett fra de sju nummererte over, som er navngitt i troppen og beholdes som de er.

To spillere har med vilje **ingen** konto (`TS-08-05`, `TS-11-12`). Det er en egen tilstand
fra «har ikke svart», og begge skal virke.

Vil du begynne på nytt: tøm `public`-skjemaet i Supabase (inkludert `__EFMigrationsHistory`)
og kjør appen igjen. Det rammer alle på prosjektet, så si fra i kanalen først.

---

## Tester

```bash
dotnet test
```

Testene ligger i `StartPraksisGruppe3Prosjekt.Tests` (xUnit) og kjører på **SQLite i minnet**,
ikke mot Supabase. Ingen hemmeligheter, ingen nettverk, og ingen fare for å skrive i den delte
basen — kjør dem så ofte du vil.

At de kjører på SQLite og appen på Postgres er en avveining og ikke en forglemmelse: SQLite
gir ekte unike indekser, fremmednøkler og faktisk SQL-oversetting, og en spørring som ikke
lar seg oversette i det hele tatt faller her i stedet for i produksjon. SQLite har ingen
`DateTimeOffset`, så `SqliteAppDbContext` i testprosjektet legger på en konvertering. Den
hører hjemme der og ikke i `AppDbContext` — Postgres har `timestamptz` og trenger den ikke.

Det som testes er reglene som ikke tåler å bli feil:

- **Reverseringen og båndene** (`FiveCRules`, `ScoringService.ScoreOf`) — inkludert at de to
  skjemaene skårer en reversert påstand likt.
- **Append-only-loggene.** Vakten ligger i `AppDbContext.SaveChanges`, så testene skriver
  direkte på konteksten: går de gjennom en tjeneste, tester de tjenesten og ikke vakten.
- **Redigeringen før frigivelse.** At trenerens tall ikke er i *modellen* før treneren har
  frigitt dem — ikke bare at de er skjult i visningen.
- **Perioder**: navnekrav, dubletter, vindu som slutter før det starter, og hvilken periode
  som er «gjeldende» når flere er åpne.
- **Spørsmålsfila.** `QuestionCatalogTests` laster *den* fila appen leverer (lenket inn i
  testprosjektet), så en redigering som ødelegger skjemaet faller i CI i stedet for ved neste
  oppstart.
- **Revisjonsloggen**, inkludert at den ikke lagrer noe annet forespørselen holdt på med.
- **Lagsnittet** (`TeamAggregateTests`): at det er et snitt av spillere og ikke av svar, at
  grensen på tre respondenter holder per rolle, at «holdt tilbake» og «ingen har svart» er to
  ulike tilstander, og at et snitt per påstand er skåret slik at en reversert påstand peker
  samme vei som resten.

En del av testene går gjennom **hele applikasjonen over HTTP**, med `WebApplicationFactory`
og `StartCompassFactory`. De svarer på spørsmål ingen enkelttjeneste kan svare på: hva en
gitt innlogget bruker faktisk får tilbake, gjennom ruting, policyer, controller og ferdig
rendret visning. Innloggingscookien er byttet mot `TestAuthHandler`, så en test kan spørre
«hva ser en foresatt her» uten å håndtere passord. Databasen er den samme SQLite-en som
resten.

Det er der disse ligger, og de kunne ellers bare sjekkes ved å logge inn som fire personer og
klikke: at en anonym forespørsel avvises, at en foresatt ser sitt eget barn og *ikke* et
annet, at en trener slipper inn uten samtykke **og** at oppslaget havner i revisjonsloggen,
at spillerens egne besøk ikke logges, at trenerens svar er skjult til de er frigitt, og at
lagoversikten står **over** spillerlista og sier hvorfor den er tom når for få har svart.

CI ligger i [`.github/workflows/ci.yml`](.github/workflows/ci.yml) og kjører `restore`,
`build` og `test` på hver push og hver pull request. Den trenger ingen hemmeligheter:
databasepassordet trengs for å *kjøre* appen, ikke for å bygge eller teste den.

---

## Stack

- ASP.NET Core MVC, **.NET 8 (LTS)**
- EF Core 8, code-first, **Postgres i Supabase** (Npgsql)
- ASP.NET Core Identity med roller

Om rammeverkversjonen: maskinen som satte opp prosjektet har SDK 9.0.308 installert,
men .NET 9 er STS. Nyeste LTS som faktisk kan bygges og kjøres her er .NET 8 (runtime
8.0.22 er installert), og prosjektet står derfor på `net8.0`. Skal dere opp på .NET 10
LTS senere, er det `<TargetFramework>` i csproj-filen pluss pakkeversjonene — men gjør
det som en egen, samlet endring, ikke midt i en feature.

**Kode og identifikatorer på engelsk. Brukergrensesnittet er også på engelsk**, i tråd med
StartCompass-nettstedet og wireframene. (Dette er en endring: tidligere sto det at all
brukertekst skulle være på norsk. Views som ennå ikke er rørt, kan fortsatt være norske.)

---

## 5C-spørreskjemaet

25 påstander i fem kategorier, 1–5-skala, besvart av spiller, foresatt og trener om samme
spiller — pluss en treneroversikt som viser hvor de tre er uenige.

**Spørsmålene ligger i `Data/Questions/five-c-questions.json` og ingen andre steder.** Ingen
`.cshtml`-fil inneholder et kategorinavn eller en påstand, så treneteamet kan bytte hele
settet uten at UI-koden røres. Fila valideres ved oppstart, og en feil i den stopper appen
med en melding som sier hva som er galt.

Svarene lagres i **appens egen database**, som etter overgangen til Npgsql *er* Supabase:
tabellene `FiveCSubmissions` og `FiveCAnswers`, med unik indeks på
(runde, spiller, respondent) slik at et nytt svar er en retting og ikke en ny mening.
Tidligere lå de i minnet og forsvant ved omstart.

To unntak finnes, og de er unntak — ikke alternativer:

| Konfigurasjon | Lagring |
| --- | --- |
| *(ingenting)* | appens database. **Standard.** |
| `FiveC:Supabase:Url` + `:ApiKey` | et *genuint separat* Supabase-prosjekt, over PostgREST |
| `FiveC:Store = "InMemory"` | ingenting lagres — for en demo uten å skrive |

Kontrakten frontend sender ligger i `Contracts/FiveC/` — i C# og speilet i TypeScript.

Deling skjer med query-param: `/Survey/Fill?roundId=2&playerId=14&role=Coach`. Lenken gir
ingen tilgang i seg selv; den forhåndsvelger spiller og rolle, og begge sjekkene kjøres på
nytt på serveren.

**Alt om dette: [`docs/five-c.md`](docs/five-c.md)** — inkludert hvorfor det ikke ble token i
URL-en, hva Victor trenger å vite om skjemaet, og hva som gjenstår.

### Perioder

En **periode** (`SurveyRound` i modellen) er ett målevindu. Spiller, foresatt og trener
svarer på det samme skjemaet innenfor den, og svarene tilhører den perioden alene.

Flere perioder kan være åpne samtidig — det er normalt når en ny starter før den forrige er
stengt. Skjemaet lander på den som stenger sist.

Perioder opprettes på to måter, og begge går gjennom `IPeriodService`, så reglene for hva
som er en brukbar periode bor ett sted:

- **Admin-siden** `Admin/Periods`: navn, åpner, stenger. Ny periode starter tom. Den ligger
  som **«Periods»** i hovedmenyen for admin, ikke bare under «Administration» — en periode må
  finnes og være åpen før noen kan svare på noe, så det er den admin-siden som åpnes oftest.
- **Seeding** i `SeedData.SeedRoundsAsync`, som er idempotent *per periode* — ellers kunne
  en ny periode aldri legges til i en base som allerede var seedet.

Seedingen ligger på **én** periode i alle miljøer: `Autumn <år>`, åpen. Det er en plassholder
til klubben har bestemt hva de virkelige periodene er. Andre perioder fjernes ved oppstart —
men **bare hvis de er tomme**. En periode med svar blir stående, for sletting tar svarene med
seg, og det er ikke en avveining et seed-steg skal gjøre alene.

**I Development kommer to til:** `Spring <år>` og `Summer <år>`, begge avsluttet, begge med
oppdiktede svar i seg. De ligger i `SeedData.SeedDemoPeriodsAsync` og ikke i `SeedRoundsAsync`
nettopp fordi de er demodata — uten dem er «over time» en tom side, både for spiller og lag.
At de overlever oppryddingen over, er fordi de har svar i seg.

### Valgt periode huskes

Å velge en periode på skjemasiden og så åpne lagoversikten kastet tidligere valget og hoppet
tilbake til gjeldende periode. Nå ligger valget i en cookie (`StartCompass.Period`), og
`IPeriodSelection` er den ene veien inn: URL-en vinner hvis den navngir en periode — en delt
lenke må bety det den sier — ellers det som ble husket, ellers gjeldende. En husket periode
som siden er slettet ignoreres i stedet for å bli en 404 på en side ingen ba om.

En periode kan stenges fra admin-siden. Svar som allerede er gitt beholdes; perioden slutter
bare å ta imot nye.

### Hvem ser hva

| | Egne svar | At de andre har svart | Trenerens svar og avvik | Hele laget |
| --- | --- | --- | --- | --- |
| Spiller | ja | ja | først når treneren frigir | nei |
| Foresatt (eget barn) | ja | ja | først når treneren frigir | nei |
| Trener | ja | ja | alltid, for alle spillere | ja |
| Admin | ja | ja | alltid | ja |

Trener- og admin-oppslag på en enkeltspiller havner i revisjonsloggen. Spillerens egne
besøk på sin egen side gjør det ikke — det ville vært støy som skjuler radene som betyr noe.

**Spilleren ser loggen selv**, nederst på sin egen side: rolle og tidspunkt, ikke bruker-ID
eller e-postadresse. Leseren vet hvem treneren sin er, og en kontoadresse er ikke deres å få.
Det er den andre halvdelen av at trenere ikke lenger trenger samtykke: klubben kan gjøre rede
for hvert oppslag, og det kan den det gjelder også.

### Skjemalisten

`/Survey` er én liste med tre betydninger: for en spiller ett kort om seg selv, for en
foresatt ett per barn, for en trener ett per spiller i klubben. Visningen forgrener seg ikke
på rolle — `ISurveyAssignmentService` har allerede regnet ut hva som hører hjemme i lista.

Trenertilfellet er grunnen til at det er filtre: periode, lag, rolle, status og spillerkode.
Filtrene ligger i query-strengen, så en filtrert liste er en URL som kan deles og som
tilbakeknappen forstår. Totalene telles **før** filtrering — et fremdriftstall som flytter
seg når du filtrerer, forteller om filteret og ikke om arbeidet som gjenstår.

### Mobil

Utfyllingen er den flyten som må fungere på en telefon, og den er bygget for det: skalaen
1–5 tar full bredde, knapper er trykkflater i full bredde, inputfelt er 16px (mindre, og
iOS Safari zoomer inn ved fokus), og etikettene under tallene vikes til fordel for
endepunktene «Strongly disagree» / «Strongly agree». Trenerens tabeller scroller i stedet
sidelengs inne i `.sc-table-wrap` — en trener som sammenligner en tropp sitter uansett på
en laptop.

### Lagoversikt

Øverst på lagsiden leses hele troppen som én, på de samme tre nivåene som en enkeltspiller:
på tvers av alle 25 påstandene, per kategori, og per påstand. Samme stolper, samme partial,
samme 1–5-skala — poenget er at man ikke skal lære seg diagrammet på nytt ett nivå opp.

**Hvert tall er et snitt av SPILLERE, ikke av svar.** På hvert nivå er lagets tall snittet av
spillernes tall på det nivået, slik at én spiller teller én gang enten hen svarte på fem
påstander eller tjuefem. Å slå sammen alle svarene i stedet ville latt den som fylte ut
skjemaet mest fullstendig veie mest, og et lagsnitt skal beskrive den gjennomsnittlige
spilleren.

Seksjonen avgjøres av `CanViewTeamAggregate` — én gang, mot det faktiske antallet spillere bak
tallene — og ikke av `CanViewPlayer` gjentatt for alle. Samme grense gjelder **per rolle**:
har færre enn tre foresatte svart, er «foresattsnittet» de foresattes egne svar med lagets
navn på. `TeamRoleAverage.From` slipper tallet i stedet for å sende det videre, så ingen
visning har det å lekke. At noe er holdt tilbake, og at ingen har svart, er to forskjellige
ting, og siden sier hvilken av dem det er.

Tallene per påstand her er **skårede**, i motsetning til påstandstabellen for én spiller: de
står ved siden av kategorisnittene på en skala der høyt er bra, og et råsnitt på en reversert
påstand ville vært den ene kolonnen i seksjonen som pekte motsatt vei.

### Søk i troppen

Spillerlista på lagsiden filtreres levende, på spillerkode og posisjon, over den troppen som
allerede står på siden. Ingenting hentes og ingen kode forlater nettleseren — hver rad ligger
i dokumentet, og filteret avgjør bare hvilke som vises. Feltet er `hidden` i markupen og
avdekkes av `survey.js`, så uten JavaScript står tabellen komplett og det dukker ikke opp en
søkeboks som ikke gjør noe.

Kode og posisjon, fordi det er det som finnes: systemet har ingen navn.

### Utvikling over tid

Trenerens spillerside viser spillerens egne snitt per C på tvers av periodene de har svart i,
med endringen i tall og ord. Kun **spillerens egne** svar: hva en trener mente om dem i mars
er ikke en del av hvordan spilleren utviklet seg til september, og en linje som blandet inn
det ville flyttet seg når treneren skiftet mening.

**Lagsiden har den samme grafen for hele troppen**, aggregert på samme måte som lagoversikten:
for hver periode og hver C, snittet av spillernes egne snitt. Samme partial og samme tidsakse
— `IFiveCTrend` er det de to deler, og det eneste som skiller dem er hvem linja handler om. En
periode med for få spillere bak seg blir et hull i linja i stedet for et tegnet punkt, og
siden navngir perioden: et uforklart hull leses som «ingen svarte», og noen svarte.

Trenger minst to perioder med svar. Med én står det at det finnes en posisjon, men ingen
retning — nye perioder opprettes under Administration.

### Statement by statement

Under snittene ligger alle 25 påstandene med hva hver enkelt faktisk svarte. Tallene er
**rå** — det respondenten klikket — ikke den reverserte skåren. På en reversert påstand
betyr derfor 5 at man er sterkt enig i en negativt formulert setning, altså en lav skår, og
den er merket «Reversed» av nettopp den grunn.

Avstanden mellom to svar er lik uansett: reversering snur begge sider, så |(6−a) − (6−b)|
er |a − b|. Rå svar og en absoluttdifferanse er derfor konsistent sammen, mens rå svar og en
fortegnsdifferanse ikke ville vært det.

### Tre ordlyder per spørsmål

Samme påstand, tre lesere. Spilleren svarer om seg selv, treneren om en spiller, foresatt om
sitt eget barn — bare grammatikken skifter:

| Felt | Leser | Eksempel |
| --- | --- | --- |
| `text` | spilleren | «I keep working on my development …» |
| `textAboutPlayer` | treneren | «The player keeps working on their development …» |
| `textForGuardian` | foresatt | «My child keeps working on their development …» |

Hver faller tilbake på den over, så et spørsmålssett som bare fyller ut `text` fungerer for
alle. Svaret lagres likt uansett hvilken ordlyd som produserte det.

### Samtaleflyten

5C-runden er en samtale, ikke en dom. Rekkefølgen:

1. Spilleren svarer om seg selv.
2. Treneren svarer om spilleren. Ingen av dem ser den andre ennå.
3. Spilleren får vite at treneren **har** svart — ikke hva.
4. Treneren frigir svarene sine. Først da ser spilleren trenerens score og avviket.

Treneren ser alt hele veien. Foresatt ser nøyaktig det samme som spilleren.

Merk at samtalen følger **spilleren**, ikke den som ser på: en foresatt som ikke har fylt ut
sitt eget skjema følger likevel barnets samtale med treneren. Deres eget skjema er et eget
bidrag, ikke en sperre. (Det var en bug til 02.09.2026 — foresatte så ingenting før de hadde
svart selv. Testen `Guardian_sees_the_same_as_the_player` fanget den.)

Asymmetrien er med vilje: at en trener leser sin egen uenighet med en fjortenåring er en
treneravgjørelse, og det samme tallet som dukker opp uanmeldt på spillerens telefon er det
ikke.

Frigivelsen er en append-only logg (`FeedbackRelease`), som samtykkeloggen — en frigivelse
som senere trekkes tilbake er fortsatt noe som skjedde. Trekker treneren tilbake, legges det
til en ny hendelse; den gamle raden blir stående.

Viktig for den som bygger videre: **redigeringen skjer i modellen, ikke i visningen.**
`FiveCFeedbackBuilder` fjerner trenerens tall fra modellen når det ikke er frigitt, slik at
en ny side eller en glemt partial ikke kan lekke dem. Ikke flytt den avgjørelsen inn i en
`.cshtml`-fil.

---

## Struktur

```
StartPraksisGruppe3Prosjekt/
├─ Controllers/
│  ├─ CoachController.cs        lagoversikt, søk, spillerdetalj
│  ├─ GuardianController.cs     foresatt ser eget barn
│  ├─ PlayerController.cs       spiller ser egne svar
│  ├─ SurveyController.cs       runder, utfylling, lagring
│  └─ AdminController.cs        brukere, lag, GDPR
├─ Models/                      entiteter, enums og PlayerRules
├─ Data/
│  ├─ AppDbContext.cs
│  ├─ Migrations/
│  └─ SeedData.cs
├─ Services/
│  ├─ IScoringService.cs + ScoringService.cs
│  ├─ IConsentService.cs + ConsentService.cs
│  ├─ IPeriodService.cs + PeriodService.cs          perioder, én vei inn
│  ├─ IFeedbackReleaseService.cs + …                trenerens frigivelse
│  ├─ IPlayerAccessLog.cs + PlayerAccessLog.cs      revisjonsloggen
│  └─ FiveC/                                        spørsmålskatalog, lagring, analyse
├─ Authorization/               policyer, krav og handlere
├─ ViewModels/
├─ Views/                       Coach/ Guardian/ Player/ Survey/ Admin/ Shared/
└─ Program.cs

StartPraksisGruppe3Prosjekt.Tests/   xUnit, SQLite i minnet. Se «Tester».
.github/workflows/ci.yml             build + test på push og pull request
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

**Rørt på tvers av eierskapet** under 5C-arbeidet, så ingen blir overrasket i en merge:
`ConsentService.GetCurrentLevelsAsync` (Brage), `CoachController` og `Views/Coach/` (Taavi),
`PlayerController` og `GuardianController` (Brage), `SurveyController` (Victor),
`Views/Shared/_Layout.cshtml` (Taavi). De eldre ti-påstands-TODO-ene er urørt.

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

### 2. Avviket (D) lagres aldri

Avviket regnes ut fra råsvarene i `ScoringService`, hver gang. Det finnes ingen kolonne
for det, og det skal ikke komme en heller.

Grunnen: et lagret avvik er en påstand om en mindreårig som blir liggende igjen etter at
svarene er rettet, samtykket er trukket eller runden er over.

Påstand nummer 5 er negativt formulert (`IsReversed = true`) og skåres som `6 - verdi`.
Regelen bor i `ScoringService.ScoreOf` — bruk den, ikke skriv `6 -` andre steder.

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
trener. Et lag er i seg selv bare et navn og en liste med spillerkoder; enkeltsvarene er
vernet av `CanViewPlayer` og loggen over.

**`CanViewTeamAggregate`** — trener med `CoachTeam` på laget, eller admin. I tillegg:
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
- `<style>`-blokker og `style="..."`-attributter i markup blokkeres. Bruk `wwwroot/css/site.css`.
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
`HomeController` (forside, personvern, feilside).

Fallbacken erstatter ikke `[Authorize(Roles = ...)]` og slett ikke de ressursbaserte
policyene. Den sier bare «innlogget», ikke «innlogget som riktig person».

### Innloggingssiden er vår egen

`Areas/Identity/Pages/Account/Login.cshtml` overstyrer siden fra Identity UI-pakken. To
grunner, og ingen av dem lot seg løse utenfra:

- Pakkesiden er et bart Bootstrap-skjema som ikke ligner resten av appen, og markupen lar
  seg ikke restyle langt nok med CSS alene.
- Den tilbyr «Register as a new user», som her er en lenke til 404 — selvregistrering er
  stengt i middleware. En død lenke på innloggingssiden er det første nye brukere møter.

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
utenfra. Lenken «Register as a new user» står fortsatt på innloggingssiden og fører nå
til 404; skal den bort, må siden scaffoldes.

### Cookies og hoder ellers

Sesjons- og antiforgery-cookies er `HttpOnly`, `SameSite=Strict` og https-only utenfor
utvikling. Sesjonen varer to timer med glidende utløp. HTML-svar til innloggede brukere
sendes med `no-store` — sidene skal ikke ligge igjen i nettleseren på en delt PC.

I tillegg: `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`,
`Permissions-Policy` uten kamera/mikrofon/posisjon, COOP/CORP `same-origin`, og HSTS i ett
år utenfor utvikling. Serverhodet er fjernet.

Kjøres appen bak en proxy må `ForwardedHeaders` settes opp, ellers ser rate limiteren
bare proxyens IP-adresse.

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
      frigir dem. Se «Samtaleflyten» over
- [ ] **Samtykke stanser ikke lenger en trener.** Dette må inn i Sikt-meldingen og i
      personvernerklæringen: trenere ser alle spillere, og det som dokumenterer bruken er
      revisjonsloggen. Klubben bør bekrefte at det er slik de vil ha det
- [ ] Foresatt ser det samme som spilleren, også for myndige spillere over 19. Vurder om
      det burde følge `PlayerRules.GuardianRequiredBelowAge`
- [ ] Regelen om foresatt for spillere under 19 håndheves i seed-data, men ikke ennå
      ved registrering i `AdminController`
