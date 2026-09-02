# Melding til Sikt — StartCompass

Utkast. **Ikke sendt.** Skrevet 02.09.2026, ut fra hva koden faktisk gjør.

Dokumentet er på norsk med vilje, selv om grensesnittet er engelsk: dette er en melding til
et norsk organ om nordmenn, og skal leses av dem.

Felter merket **`[KLUBBEN]`** er faktaopplysninger vi i gruppa ikke kan svare på. Alt annet
er hentet fra koden og skal stemme — hvis noe her ikke stemmer med koden, er det en feil, og
den skal rettes i koden eller her.

> **Ingen ekte data er samlet inn.** Databasen inneholder i dag bare oppdiktede spillere med
> pseudonyme koder. Meldingen skal være godkjent før det endres.

---

## 1. Behandlingsansvarlig og kontakt

| | |
| --- | --- |
| Behandlingsansvarlig | **`[KLUBBEN]`** — IK Start ved daglig leder? Eller UiA? Dette må avklares først, fordi flere av svarene under følger av det |
| Kontaktperson i klubben | **`[KLUBBEN]`** |
| Personvernombud | **`[KLUBBEN]`** — har klubben et? |
| Studentgruppe | TechSquad: Kristian Espevik, Victor, Taavi, Brage |
| Emne | IS-302 Praksisprosjekt, Universitetet i Agder |
| Faglig ansvarlig / veileder | **`[KLUBBEN]`** / UiA |

Merk at ansvarsspørsmålet ikke er formelt: er det klubben som er behandlingsansvarlig, er
studentgruppa databehandler og trenger en databehandleravtale.

## 2. Formål

Utviklingsavdelingen i IK Start følger spillerne tett, men oppfølgingen gjøres manuelt og
oversikten ligger spredt. StartCompass samler den.

Kjernen er en sammenligning: spilleren vurderer seg selv på fem utviklingsområder («de fem
C-ene»), treneren og foresatt vurderer det samme, og systemet viser hvor de tre er uenige.
Formålet er at et sprik mellom hvordan en spiller ser seg selv og hvordan treneren ser dem
blir synlig og kan snakkes om.

Systemet gir ikke uttelling: ingen tas ut eller vrakes på grunnlag av svarene, og de inngår
ikke i noen rangering. **`[KLUBBEN]`** må bekrefte at dette er og forblir riktig — det er en
vesentlig forutsetning for at spillerne kan svare ærlig.

## 3. Utvalg

Spillere i utviklingsavdelingen, deres foresatte, og trenerne.

**De fleste spillerne er mindreårige.** Det er premisset bak alle valgene i systemet.
Regelen `PlayerRules.GuardianRequiredBelowAge` krever at hver spiller under 19 år har minst
én registrert foresatt.

Antall: **`[KLUBBEN]`** — hvor mange spillere, hvor mange lag?

## 4. Hvilke opplysninger

Alt systemet lagrer om en spiller, uttømmende, fra datamodellen i `Models/`:

| Opplysning | Hvor | Merknad |
| --- | --- | --- |
| Spillerkode (f.eks. «TS-08-16») | `Player.Code` | Pseudonym. Brukes overalt i grensesnittet |
| Fødselsdato | `Player.BirthDate` | Kun til aldersgrensen for krav om foresatt |
| Posisjon og lag | `Player.Position`, `Player.TeamId` | |
| Kobling til brukerkonto | `Player.UserId` | Kan være tom |
| Svar på 25 påstander, skala 1–5 | `FiveCAnswers.Value` | Per periode, per respondent |
| Hvem som svarte og når | `FiveCSubmissions` | Spiller, trener eller foresatt |
| Samtykkehistorikk | `ConsentEvents` | Append-only |
| Hvem som har åpnet spillerens svar | `PlayerAccessEvents` | Append-only. Se punkt 7 |
| Når treneren delte svarene sine | `FeedbackReleases` | Append-only |
| Kobling til foresatt | `Guardianships` | |

**Navn lagres ikke om spillere.** Grensesnittet bruker spillerkode gjennomgående. Navn og
e-postadresser finnes bare på brukerkontoene (`AspNetUsers`), som er voksne trenere og
foresatte, samt de spillerne som har fått egen konto.

**Ingen fritekst.** Det finnes ikke noe felt der en trener kan skrive en merknad om en
spiller. Det er et bevisst valg: en trenernotat om en mindreårig er en ny kategori
personopplysninger, og den er ikke i modellen.

**Ingen særlige kategorier.** Svarene handler om innsats, kommunikasjon, konsentrasjon,
selvkontroll og selvtillit i en fotballsammenheng. De er ikke helseopplysninger.
**`[KLUBBEN]`** bør likevel vurdere om påstander om selvtillit og stressmestring hos
mindreårige nærmer seg noe som skal behandles strengere enn vanlige opplysninger — vi mener
nei, men det er ikke vår vurdering å ta alene.

**Avviket lagres aldri.** Sammenligningen mellom spiller og trener regnes ut på nytt hver
gang den vises. Grunnen: et lagret avvik er en påstand om en mindreårig som blir liggende
igjen etter at svarene er rettet, samtykket trukket eller perioden over.

## 5. Rettslig grunnlag

Samtykke, jf. GDPR art. 6 nr. 1 bokstav a. For mindreårige gis samtykket av foresatt.

Samtykket har tre nivåer (`ConsentLevel`): ingen deling, kun anonyme lagsnitt, eller full
deling. Det er en append-only logg — et tilbaketrukket samtykke legges inn som en **ny**
hendelse med lavere nivå, og den gamle raden blir stående, slik at klubben kan dokumentere
hva som var lov når.

> ### ⚠️ Dette må avklares før meldingen sendes
>
> **Samtykke stanser ikke lenger en trener fra å åpne en spillers svar.**
>
> Tidligere krevde systemet fullt samtykke før en trener fikk se en enkeltspiller. Klubben
> ba om at trenere alltid skal ha tilgang, og det er slik systemet nå fungerer
> (`CanViewPlayerHandler`).
>
> Det som erstatter sperren er etterprøvbarhet, ikke hindring: hvert oppslag logges (punkt
> 7). Men det betyr at samtykkenivået i praksis styrer *dokumentasjon og videre bruk*, ikke
> *hvem som får se*. Meldingen må beskrive det slik det er, og **`[KLUBBEN]`** må bekrefte at
> det er slik de vil ha det. Er svaret nei, er det én endring i én fil å sette sperren
> tilbake.
>
> Samtykkeskjemaet der foresatte faktisk gir eller trekker samtykket er **ikke bygget ennå**
> (`GuardianController.Consent`). Det må på plass før ekte data.

## 6. Informasjon til utvalget

**`[KLUBBEN]`** — informasjonsskrivet er ikke skrevet. Det må dekke punktene i dette
dokumentet, på et språk en fjortenåring og en forelder forstår, og på **norsk** selv om
grensesnittet er engelsk.

Personvernerklæringen i appen (`Views/Home/Privacy.cshtml`) er tom og venter på det samme.

## 7. Tilgangsstyring

Rolle alene avgjør ikke tilgang. Tilgangen vurderes per spiller (`CanViewPlayer`):

| Hvem | Får se en enkeltspillers svar |
| --- | --- |
| Spilleren selv | ja, sine egne |
| Foresatt | kun sitt eget barn, og kun der koblingen finnes i `Guardianships` |
| Trener | alle spillere |
| Administrator | alle spillere |

**Revisjonslogg.** Hvert oppslag en trener eller administrator gjør på en enkeltspillers svar
skriver en rad i `PlayerAccessEvents`: hvem, hvilken spiller, hvilken side, når. Loggen er
append-only. Spillerens egne besøk på sin egen side logges ikke.

**Spilleren ser loggen selv**, nederst på sin egen side — rolle og tidspunkt. Det er en
bevisst utvidelse av innsynsretten: den registrerte kan se at oppslag har skjedd, ikke bare
be om å få vite det.

**Spilleren ser ikke trenerens svar før treneren deler dem.** Rekkefølgen er: spilleren
svarer, treneren svarer, spilleren får vite at treneren *har* svart, treneren frigir. Grunnen
er at et avvikstall som dukker opp uanmeldt på en fjortenårings telefon er noe annet enn det
samme tallet i en samtale.

**Lagsnitt vises ikke under tre besvarelser** (`CanViewTeamAggregateRequirement`), ellers kan
snittet regnes tilbake til enkeltpersoner.

## 8. Lagring og sikkerhet

| | |
| --- | --- |
| Hvor | Supabase (Postgres), region `eu-west-1` — **Irland, innenfor EØS** |
| Databehandler | Supabase. **`[KLUBBEN]`** må inngå databehandleravtale |
| Overføring | TLS påkrevd (`SSL Mode=Require`) |
| Passord og nøkler | Aldri i kildekoden. Ligger i user-secrets lokalt og i miljøet i drift |
| Autentisering | ASP.NET Core Identity. Minst 12 tegn, kontolåsing etter fem forsøk |
| Selvregistrering | Stengt. Kontoer opprettes av klubben |
| Sesjon | To timer, glidende. Cookies er HttpOnly, SameSite=Strict, https utenfor utvikling |
| Sider i nettleserens cache | HTML til innloggede sendes `no-store` — delt PC hjemme er normalen |
| Øvrig | CSP uten `unsafe-inline`, antiforgery på alle skrivende kall, rate limiting, HSTS |

Åpne punkter før drift:
- `AllowedHosts` står på `*` og må settes til det faktiske vertsnavnet.
- Kjøres appen bak en proxy må `ForwardedHeaders` settes opp, ellers ser rate limiteren bare
  proxyens IP-adresse.

## 9. Varighet og sletting

**`[KLUBBEN]`** — hvor lenge skal svarene beholdes? Forslag til utgangspunkt: ut sesongen
pluss ett år, slik at utvikling over tid kan vises over minst to sesonger, og deretter
sletting. Det må være et bevisst valg, ikke «til noen rydder».

Sletting av en spiller fjerner i dag svar, samtykkelogg, revisjonslogg og foresattkoblinger
via cascade. Rutinen i `AdminController.Delete` er **ikke implementert ennå** — den må på
plass før ekte data, sammen med innsynssiden (`AdminController.Export`).

Prosjektets sluttdato: **`[KLUBBEN]`** / UiA. Hva skjer med dataene når praksisprosjektet er
over og gruppa er ferdig?

## 10. Den registrertes rettigheter

| Rettighet | Status |
| --- | --- |
| Innsyn | Delvis. Spilleren ser egne svar og revisjonsloggen i appen. Full utlevering (`AdminController.Export`) er ikke bygget |
| Retting | Ja, så lenge perioden er åpen — spilleren kan endre svarene sine selv |
| Sletting | Ikke bygget (`AdminController.Delete`) |
| Trekke samtykke | Modellen støtter det; skjemaet er ikke bygget |
| Dataportabilitet | Ikke bygget. Går sammen med innsyn |

---

## Hva som gjenstår før dette kan sendes

1. **`[KLUBBEN]`** avklarer behandlingsansvarlig — flere svar over henger på det.
2. **`[KLUBBEN]`** bekrefter at trenere skal ha tilgang uavhengig av samtykke (punkt 5).
3. Samtykkeskjemaet bygges (`GuardianController.Consent`).
4. Innsyn og sletting bygges (`AdminController.Export` og `.Delete`).
5. Informasjonsskriv og personvernerklæring skrives, på norsk.
6. Databehandleravtale med Supabase.
7. Lagringstid bestemmes.

Punkt 3 og 4 er kode og ligger hos oss. Resten er klubbens.
