# Speilet — StartPraksisGruppe3Prosjekt

Verktøy for IK Start. Spillere, trenere og foresatte svarer på de samme ti påstandene
om rolleforståelse, trygghet og mestring. Systemet viser avviket mellom hva treneren
*tror* spilleren svarer og hva spilleren *faktisk* svarer.

**Systemet behandler personopplysninger om mindreårige.** Det er premisset bak alle
valgene under, og det er grunnen til at autorisasjon ikke er noe som skrus på til slutt.

> Bare oppdiktede data i dette repoet. Ekte spillerdata skal ikke inn før prosjektet
> er meldt til Sikt.

---

## Status

Fase 0 er ferdig: struktur, datamodell, Identity med fire roller, ressursbasert
autorisasjon og tomme kontrollere/views med TODO-markører.
Selve funksjonaliteten er **ikke** implementert — den skal fordeles.

`dotnet build` kjører rent, og `dotnet run` oppretter databasen og legger inn seed-data.

---

## Kom i gang

```bash
dotnet run --project StartPraksisGruppe3Prosjekt
```

Første kjøring kjører migrasjonene og legger inn oppdiktede demodata. Databasen er en
SQLite-fil (`speilet.db`) i prosjektmappa, og den er git-ignorert.

Demokontoer opprettes bare i `Development`. Standard passord er `Dev!passord1` og kan
overstyres:

```bash
dotnet user-secrets set "Seed:DevPassword" "ditt-passord" --project StartPraksisGruppe3Prosjekt
```

| Konto | Rolle |
| --- | --- |
| `admin@ikstart.example` | Admin |
| `trener.senior@ikstart.example` | Trener (A-laget, G19) |
| `trener.ungdom@ikstart.example` | Trener (G16) |
| `spiller.ts0816@ikstart.example` m.fl. | Spiller |
| `foresatt1@example.test` … `foresatt7@example.test` | Foresatt |

Vil du begynne på nytt: slett `speilet.db` og kjør igjen.

---

## Stack

- ASP.NET Core MVC, **.NET 8 (LTS)**
- EF Core 8, code-first, SQLite i utvikling
- ASP.NET Core Identity med roller

Om rammeverkversjonen: maskinen som satte opp prosjektet har SDK 9.0.308 installert,
men .NET 9 er STS. Nyeste LTS som faktisk kan bygges og kjøres her er .NET 8 (runtime
8.0.22 er installert), og prosjektet står derfor på `net8.0`. Skal dere opp på .NET 10
LTS senere, er det `<TargetFramework>` i csproj-filen pluss pakkeversjonene — men gjør
det som en egen, samlet endring, ikke midt i en feature.

**Kode og identifikatorer på engelsk. All tekst brukeren ser, på norsk.**

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
│  └─ IConsentService.cs + ConsentService.cs
├─ Authorization/               policyer, krav og handlere
├─ ViewModels/
├─ Views/                       Coach/ Guardian/ Player/ Survey/ Admin/ Shared/
└─ Program.cs
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

(eller sletter `speilet.db` og starter appen på nytt).

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

Roller alene er ikke nok. En trener er ikke trener *for alle*, og en foresatt er ikke
foresatt *for alle*. Derfor er tilgangen ressursbasert: policyene vurderer én konkret
spiller eller ett konkret lag.

**`CanViewPlayer`** (`AuthorizationHandler<CanViewPlayerRequirement, Player>`)

| Hvem | Får se spilleren |
| --- | --- |
| Admin | alltid |
| Spilleren selv | `player.UserId` er innlogget bruker |
| Foresatt | bare hvis en `Guardianship` knytter brukeren til *denne* spilleren |
| Trener | bare hvis `CoachTeam` dekker spillerens lag **og** nyeste `ConsentEvent` er `Full` |
| Alle andre | nei |

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
stubs.

---

## Ting som må avklares før ekte data

- [ ] Melding til Sikt
- [ ] Personvernerklæring (`Views/Home/Privacy.cshtml`)
- [ ] Selvregistrering i Identity UI er åpen på `/Identity/Account/Register` og bør
      stenges — kontoer skal opprettes av klubben
- [ ] Revisjonslogg for admin-oppslag på enkeltspillere
- [ ] Skal spilleren se trenerens gjetning og avviket? Ikke avgjort — se
      `PlayerController`
- [ ] Regelen om foresatt for spillere under 19 håndheves i seed-data, men ikke ennå
      ved registrering i `AdminController`
