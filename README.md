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

Svarene lagres i Supabase når `FiveC:Supabase` er satt opp i konfigurasjonen; ellers brukes
et minnelager slik at skjemaet og oversikten kan kjøres lokalt. Kontrakten frontend sender
ligger i `Contracts/FiveC/` — i C# og speilet i TypeScript.

Deling skjer med query-param: `/Survey/Fill?roundId=2&playerId=14&role=Coach`. Lenken gir
ingen tilgang i seg selv; den forhåndsvelger spiller og rolle, og begge sjekkene kjøres på
nytt på serveren.

**Alt om dette: [`docs/five-c.md`](docs/five-c.md)** — inkludert hvorfor det ikke ble token i
URL-en, hva Victor trenger å vite om skjemaet, og hva som gjenstår.

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

**`CanViewTeam`** (`AuthorizationHandler<CanViewTeamRequirement, Team>`) — admin, eller
trener med `CoachTeam` på laget. Avgjør om lagsiden i det hele tatt skal vises, og er et
annet spørsmål enn om snittet skal vises. Uten den kunne en hvilken som helst trener bla
gjennom lag-ID-er og få bekreftet hvilke lag som finnes.

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

- [ ] Melding til Sikt
- [ ] Personvernerklæring (`Views/Home/Privacy.cshtml`)
- [x] Selvregistrering stengt — kontoer opprettes av klubben. Admin-siden som faktisk
      oppretter dem er fortsatt TODO i `AdminController.Users`
- [ ] `AllowedHosts` står på `*` i `appsettings.json`. Settes til det faktiske vertsnavnet
      før produksjon
- [ ] Identity UI lar en bruker slette sin egen konto på
      `/Identity/Account/Manage/DeletePersonalData`. Det går utenom sletterutinen i
      `AdminController.Delete` og etterlater `Player.UserId` uten bruker. Avklar om siden
      skal stenges eller om sletting skal gå gjennom den
- [ ] Revisjonslogg for admin-oppslag på enkeltspillere
- [ ] Skal spilleren se trenerens gjetning og avviket? Ikke avgjort — se
      `PlayerController`
- [ ] Regelen om foresatt for spillere under 19 håndheves i seed-data, men ikke ennå
      ved registrering i `AdminController`
