# Melding til Sikt — StartCompass

Utkast. **Ikke sendt.** Skrevet 02.09.2026, gått gjennom mot koden på nytt 16.09.2026.

Dokumentet er på norsk med vilje, selv om grensesnittet er engelsk: dette er en melding til
et norsk organ om nordmenn, og skal leses av dem.

Felter merket **`[KLUBBEN]`** er faktaopplysninger vi i gruppa ikke kan svare på. Alt annet
er hentet fra koden og skal stemme — hvis noe her ikke stemmer med koden, er det en feil, og
den skal rettes i koden eller her. Der koden i dag ikke gjør det systemet lover, er det merket
**A1**, **A2** osv. og samlet i avvikslisten nederst.

> **Ingen ekte data er samlet inn.** Databasen inneholder i dag bare oppdiktede spillere med
> pseudonyme koder. Meldingen skal være godkjent før det endres, og ekte data skal ikke inn i
> databasen vi utvikler mot (punkt 8).

## Endret siden utkastet 2. september

- **Fritekst er kommet til.** Refleksjonen som avslutter perioden har tre spørsmål som
  besvares med egne ord. «Ingen fritekst» stemmer ikke lenger (punkt 4).
- **Innsyn og sletting er bygget** (punkt 9 og 10).
- **Et samtykkeskjema finnes, men samtykket styrer fortsatt ingenting** (punkt 5).
- **Nye funn ved gjennomgangen:** grensen for krav om foresatt er 19 år og ikke 18,
  administratoroppslag via foresattsiden logges ikke, påbegynte skjemaer blir liggende i
  nettleseren, kontosidene i Identity lar brukere slette egen konto utenom sletterutinen, og
  databasen vi utvikler mot kan ikke brukes til ekte data.
- **Sikkerhetsoppsettet er oppdatert:** `SSL Mode=VerifyFull`, `AllowedHosts` og
  `ForwardedHeaders` er på plass (punkt 8).

---

## 1. Behandlingsansvarlig og kontakt

| | |
| --- | --- |
| Behandlingsansvarlig | **`[KLUBBEN]`** — IK Start ved daglig leder? Eller UiA? Dette må avklares først, fordi flere av svarene under følger av det |
| Kontaktperson i klubben | **`[KLUBBEN]`** |
| Personvernombud | **`[KLUBBEN]`** — har klubben et? Er UiA behandlingsansvarlig, er det UiAs |
| Studentgruppe | TechSquad: Kristian Espevik, Victor, Taavi, Brage |
| Emne | IS-302 Praksisprosjekt, Universitetet i Agder |
| Faglig ansvarlig / veileder | **`[KLUBBEN]`** / UiA |

Ansvarsspørsmålet er ikke formelt. Det avgjør hvem som melder hva, og om Sikt i det hele tatt
er riktig mottaker:

- **Sikt vurderer bare prosjekter der en institusjon med Sikt-avtale er
  behandlingsansvarlig** — her UiA. For studentoppgaver er det institusjonen som gir graden.
- **Skal IK Start bruke StartCompass i sin egen drift, er klubben behandlingsansvarlig for den
  bruken.** Den delen er ikke Sikts å vurdere. Klubben må selv ha protokoll (art. 30),
  informasjon til de registrerte (art. 13), databehandleravtaler og en vurdering av
  personvernkonsekvenser (punkt 11). Studentgruppa er da databehandler for klubben og trenger
  en databehandleravtale.
- **Bestemmer klubben og UiA formål og midler sammen,** er ansvaret felles (art. 26), og det
  krever en avtale mellom dem.

## 2. Formål

Utviklingsavdelingen i IK Start følger spillerne tett, men oppfølgingen gjøres manuelt og
oversikten ligger spredt. StartCompass samler den.

Kjernen er en sammenligning: spilleren vurderer seg selv på fem utviklingsområder («de fem
C-ene») gjennom 25 påstander på en skala fra 1 til 5, treneren og foresatt svarer på de samme
påstandene om spilleren, og systemet viser hvor de tre er uenige. Formålet er at et sprik
mellom hvordan en spiller ser seg selv og hvordan treneren ser dem blir synlig og kan snakkes om.

Hver periode avsluttes med en kort **refleksjon**: hvilken C som har vært sterkest, hvilken det
er verdt å jobbe med neste periode, og hva som kan hjelpe. To spørsmål besvares ved å velge en
C, tre med egne ord. Formålet er at perioden slutter med noe en samtale kan ta utgangspunkt i,
ikke bare med tall.

I tillegg viser systemet utvikling over tid, per spiller og per lag, og markerer for treneren
en spiller som selv svarer lavt gjennom en hel kategori (snitt under 2 med minst tre besvarte
påstander, `FiveCRules.NeedsFollowUp`). Markeringen er et varsel til et menneske, ikke en
avgjørelse, og den lagres ikke.

Systemet gir ikke uttelling: ingen tas ut eller vrakes på grunnlag av svarene, og de inngår
ikke i noen rangering. **`[KLUBBEN]`** må bekrefte at dette er og forblir riktig — det er en
vesentlig forutsetning for at spillerne kan svare ærlig, og for at markeringen over ikke blir
en automatisert avgjørelse (art. 22).

## 3. Utvalg

Spillere i utviklingsavdelingen, deres foresatte, og trenerne. Administratorene er registrert
som brukere.

**De fleste spillerne er mindreårige.** Det er premisset bak alle valgene i systemet.
Regelen `PlayerRules.GuardianRequiredBelowAge` krever at hver spiller under **19** år har minst
én registrert foresatt. Den grensen holder ikke juridisk — se punkt 5.

Foresatte og trenere er ikke bare brukere, de er også registrerte: systemet lagrer kontoen
deres, det de svarer og skriver om spilleren, og hvilke spillere trenerne har åpnet.

Antall: **`[KLUBBEN]`** — hvor mange spillere, hvor mange lag?

## 4. Hvilke opplysninger

Alt systemet lagrer, uttømmende, fra datamodellen i `Models/` og brukerkontoene i Identity.

**Om spilleren**

| Opplysning | Hvor | Merknad |
| --- | --- | --- |
| Spillerkode (f.eks. «TS-08-16») | `Player.Code` | Pseudonym. Brukes overalt i grensesnittet |
| Fødselsdato | `Player.BirthDate` | Til kravet om foresatt, og vist som alder i skjemalisten til alle trenere |
| Posisjon og lag | `Player.Position`, `Player.TeamId` | |
| Kobling til brukerkonto | `Player.UserId` | Kan være tom |
| Svar på 25 påstander, skala 1–5 | `FiveCAnswers.Value` | Per periode, per respondent. Lagres som gitt |
| Refleksjon: valgt C | `FiveCReflectionAnswers.Value` | To spørsmål. Lagres som kategorinøkkel, f.eks. `confidence` |
| Refleksjon: fritekst | `FiveCReflectionAnswers.Value` | Tre spørsmål, frivillige, høyst 500 tegn hver. Se under |
| Hvem som svarte, i hvilken rolle, og når | `FiveCSubmissions` | Med respondentens bruker-ID og en kopi av spillerkoden |
| Samtykkehistorikk | `ConsentEvents` | Append-only, med bruker-ID til den som endret |
| Hvem som har åpnet spillerens svar | `PlayerAccessEvents` | Append-only. Se punkt 7 |
| Når trenerens svar ble frigitt eller trukket tilbake | `FeedbackReleases` | Append-only |
| Kobling til foresatt | `Guardianships` | |
| At spilleren er slettet | `PlayerDeletionEvents` | Bare spiller-ID, hvem og når. Blir stående etter slettingen, med vilje |
| Trenernes vurdering av spilleren, 1–10 på seks områder, hver åttende uke | `SuccessionAssessments`, `SuccessionRatings` | Succession planning. Per trener, per syklus. Vises bare for trenere og administratorer, aldri for spilleren. Se under |
| Trenernes kategori, posisjoner, prognose og risiko | `SuccessionAssessments` | Samme |
| Trenernes fritekst: prognoser, «what now», utviklingsfokus, styrker, notater | `SuccessionAssessments` | Frivillig, høyst 200/500/1000 tegn. Se under |
| Kontraktstype, kontraktsslutt og treningsgruppe | `PlayerSuccessionProfiles` | Én rad per spiller, lagt inn av trener eller admin |
| Fornavn | `PlayerPersonalDetails.FirstName` | Bare fornavn, til velkomsten når spilleren logger inn. Vises for spilleren selv og admin, og for trenerne på «Best eleven». Se under |
| Bilde (klubbens offisielle spillerbilde) | `PlayerPersonalDetails.Photo` | Samme. Metadata (GPS, bildetekst) fjernes før lagring |

**Om brukerkontoene** — spillere med konto, foresatte, trenere og administratorer

| Opplysning | Hvor | Merknad |
| --- | --- | --- |
| E-postadresse, som også er brukernavnet | `AspNetUsers` | Det finnes ikke noe navnefelt, men en e-postadresse inneholder ofte navnet |
| Passord-hash, låsestatus, mislykkede innloggingsforsøk | `AspNetUsers` | |
| Telefonnummer | `AspNetUsers.PhoneNumber` | Vi ber ikke om det, men brukeren kan legge det inn selv på kontosiden (A6) |
| Rolle | `AspNetUserRoles` | Player, Coach, Guardian eller Admin |
| Hvilke lag en trener er knyttet til | `CoachTeams` | Begrenser ikke lenger noe (punkt 7) |

**Spillerens fornavn og bilde lagres til velkomsten.** Når spilleren logger inn, står det
«Welcome, Alex» med spillerens eget bilde. Trenerne ser i tillegg fornavnet på én side, «Best
eleven», der laget vises som på en fotballbane; det ba de om. Bildet ser de ikke. Ellers bruker
grensesnittet spillerkoden overalt, også for trenerne. Bildet er klubbens offisielle spillerbilde, som
allerede er publisert, og IK Start har gitt tillatelse til bruken. Dette er likevel en ny bruk av
bildet, i et system som også har spillerens svar, og det må derfor stå i meldingen og i
informasjonen til spillerne og foresatte. Se `docs/player-welcome.md`.

Opplysningene kommer fra klubben (kode, fødselsdato, lag, posisjon, konto, fornavn og bilde) og
fra skjemaet (svar og refleksjon). Siden der klubben registrerer spillere og kontoer er ikke bygget ennå
(`AdminController.Users`).

### Succession planning

Nytt siden forrige utkast. Trenerne vurderer spillerne hver åttende uke, på de samme kolonnene
som klubbens eget Excel-ark («IK Start Succession Planning»). Hver trener fører sin egen
vurdering, og systemet viser dem side om side og regner ut et snitt.

Dette skiller seg fra resten av systemet på to måter, og begge bør med i meldingen:

- **Det er ikke spillerens egne svar.** Det er stabens vurdering av spilleren: evne, modenhet,
  hvilken posisjon hen passer i, og om hen er klar for neste nivå. Spilleren svarer ikke på
  noe her og ser det ikke i appen. Innsyn får spilleren gjennom `/Admin/Export/{id}`, der
  vurderingene er med, og trenerne står med rolle og løpenummer.
- **Kontraktsopplysninger** (kontraktstype og når den går ut) er nye. De registreres ikke noe
  annet sted i systemet.

Fritekstfeltene har samme risiko som refleksjonen (under): en setning er bare så pseudonym som
den som skrev den. Skjemaet ber trenerne holde seg til fotball, uten helseopplysninger og uten
navn på andre. Klubbens eget ark har fullt navn på spillerne. Arket er ikke lagt inn, og en
import er ikke bygget. Se `docs/succession-planning.md`.

**Spørsmålene er fortsatt plassholdere** (versjon `placeholder-2026-09-09` i
`Data/Questions/five-c-questions.json`). Det endelige settet — påstander og refleksjon — legges
ved meldeskjemaet.

### Fritekst

Nytt siden forrige utkast. Tre av refleksjonsspørsmålene besvares med egne ord: et eksempel på
når styrken viste seg, én atferd å utvikle, og hva som kan hjelpe. Spilleren, foresatt og
treneren svarer hver for seg.

Det som holder den i sjakk:

- Egen tabell (`FiveCReflectionAnswers`), aldri regnet med i et snitt.
- Frivillig, og høyst 500 tegn per spørsmål. Grensen håndheves på serveren.
- Følger spilleren: sletting tar den med, og innsyn har den med.
- Det treneren skriver vises ikke for spilleren før treneren har frigitt det — samme regel som
  for trenerens tall.

Det som gjør den til en annen risiko enn tallene:

- **En setning er bare så pseudonym som den som skrev den.** Et navn på en lagkamerat, en skade
  eller noe som har skjedd hjemme, og koden «TS-08-16» er ikke lenger et pseudonym.
- **Særlige kategorier kan ikke utelukkes.** «Hva kan hjelpe» inviterer til svar om helse,
  søvn, skole og familie. Meldeskjemaet må svare ærlig på det.
- **Skjemaet sier ingenting om hva som ikke skal skrives** (A12). En setning om å la være å
  skrive navn på andre eller helseopplysninger hører hjemme i spørsmålsfila. Teksten eies av
  trenerteamet, og det krever ingen kodeendring.
- **Den leses av flere enn man skulle tro.** Alle trenere i klubben leser alle tre svarene med
  en gang, og spilleren og foresatt leser hverandres (punkt 7).

**`[KLUBBEN]`**: trengs fritekst for formålet, eller holder de to spørsmålene der man velger en
C? Det som ikke samles inn, trenger ingen av tiltakene over.

### Særlige kategorier

Tallsvarene handler om innsats, kommunikasjon, konsentrasjon, selvkontroll og selvtillit i en
fotballsammenheng. De er ikke helseopplysninger. **`[KLUBBEN]`** bør likevel vurdere om
påstander om selvtillit og stressmestring hos mindreårige nærmer seg noe som skal behandles
strengere enn vanlige opplysninger — vi mener nei, men det er ikke vår vurdering å ta alene.

For friteksten er svaret et annet; se over.

### Det som ikke lagres

**Avviket lagres aldri.** Sammenligningen mellom spiller og trener regnes ut på nytt hver gang
den vises, og det samme gjelder oppfølgingsmarkeringen og utviklingen over tid. Grunnen: et
lagret avvik er en påstand om en mindreårig som blir liggende igjen etter at svarene er rettet,
samtykket trukket eller perioden over.

### Rester av det gamle skjemaet

Tabellene `Items`, `Responses` og `Answers` hører til det gamle skjemaet med ti påstander.
Ingen side skriver til dem lenger, men de finnes i databasen og er med i innsyn og sletting. De
bør fjernes før ekte data (A9).

### Utenfor databasen

| Hvor | Hva | Hvor lenge |
| --- | --- | --- |
| Nettleserens `localStorage` | Et skjema som er påbegynt, men ikke sendt — tall og fritekst | Til skjemaet sendes eller forkastes på samme enhet. **Ikke** ved utlogging (A5) |
| Nettleserens `sessionStorage` | Hvilken fane som var åpen | Til fanen lukkes |
| Cookies | `Speilet.Auth` (innlogging), `Speilet.Antiforgery`, `StartCompass.Period` (valgt periode) | Innlogging: to timer uten aktivitet. Periode: 30 dager |
| Applikasjonslogger | Spiller-ID ved innsending, IP-adresse og sti ved avviste kall, bruker- og spiller-ID hvis revisjonsloggen feiler | Der verten lagrer logger — ikke bestemt |
| Nedlastede innsynsfiler | Alt om én spiller, som JSON | På administratorens maskin, til noen sletter fila |
| Backup i Supabase | Hele databasen | Avhenger av Supabase-planen — må sjekkes |

Cookies og lagring i nettleseren er regulert av ekomloven § 3-15 og skal med i
personvernerklæringen.

## 5. Rettslig grunnlag

Utkastet la til grunn samtykke, jf. GDPR art. 6 nr. 1 bokstav a, gitt av foresatt for
mindreårige.

Samtykket har tre nivåer (`ConsentLevel`): ingen deling, kun anonyme lagsnitt, eller full
deling. Det er en append-only logg — et tilbaketrukket samtykke legges inn som en **ny**
hendelse med lavere nivå, og den gamle raden blir stående, slik at klubben kan dokumentere
hva som var lov når.

> ### ⚠️ Slik koden er i dag, kan samtykke ikke stå som grunnlag
>
> **Samtykkenivået registreres, men ingenting i koden styres av det** (A1).
>
> | Nivå | Det foresatt leser i skjemaet | Det systemet gjør |
> | --- | --- | --- |
> | Ingen deling | «Only the player and their guardians see the answers» | Alle trenere og administratorer ser svarene |
> | Kun lagsnitt | «not shown for the individual» | Vises per spiller for alle trenere |
> | Full deling | «The coach can see the player's own answers and the gap» | Stemmer |
>
> Spilleren tas dessuten med i lagsnittet uansett nivå, og en spiller uten registrert samtykke
> kan fylle ut skjemaet og få svar skrevet om seg. Svarene lagres og vises som for alle andre.
>
> Klubben ba om at trenere alltid skal ha tilgang, og det er slik systemet fungerer
> (`CanViewPlayerHandler`). Det som erstatter sperren er etterprøvbarhet: oppslag logges
> (punkt 7). Men et samtykke som ikke begrenser noe, er ikke informert, og et samtykke som kan
> trekkes uten at noe stopper, er ikke et samtykke (art. 4 nr. 11 og art. 7 nr. 3). Uten gyldig
> samtykke har behandlingen da ikke noe grunnlag.
>
> Ett av to må velges før meldingen sendes:
>
> - **A. Samtykke, håndhevet.** Ingenting lagres eller vises uten gyldig samtykke, nivået styrer
>   hva trenere ser og hva som går inn i lagsnitt, og et tilbaketrukket samtykke stopper videre
>   behandling. Det betyr å sette sperren tilbake, som klubben ba om å fjerne.
> - **B. Et annet grunnlag** — i praksis berettiget interesse (art. 6 nr. 1 bokstav f). Det
>   krever en dokumentert interesseavveining der barnas interesser veier tungt, rett til å
>   protestere (art. 21), og at samtykkenivåene som lover noe, fjernes.
>
> **`[KLUBBEN]`** og UiA avgjør. Koden følger etter.

**Samtykkeskjemaet er bygget** (`GuardianController.Consent`, `/Guardian/Consent/{id}`), men
(A2):

- ingen side lenker til det,
- teksten er engelsk,
- historikken viser bruker-ID-en til den som endret samtykket, rått — det innsynsfila bytter ut
  med pseudonymer, av god grunn (punkt 10),
- `ConsentService` lar en myndig spiller endre sitt eget samtykke, men siden slipper bare inn
  rollene Guardian og Admin.

**Alder** (A3):

- `PlayerRules.GuardianRequiredBelowAge` er 19. Spilleren kan først samtykke selv fra 19 år,
  mens foresatt kan registrere samtykke for spilleren uansett alder
  (`ConsentService.CanRecordConsentAsync`). Myndighetsalderen er 18: ingen kan samtykke på
  vegne av en myndig spiller.
- Koblingen til foresatt har ingen aldersgrense. Foresatt ser det samme som spilleren også
  etter at spilleren er myndig.
- Under 18 år er det en vurdering. Datatilsynet legger til grunn at jo større konsekvenser
  behandlingen har, desto høyere skal terskelen være for at barnet samtykker selv, og fra 15 år
  bestemmer barn selv om innmelding i foreninger (barneloven § 32). **`[KLUBBEN]`**/UiA: fra
  hvilken alder samtykker spilleren selv, og hva skal foresatte se etter det?

## 6. Informasjon til utvalget

**`[KLUBBEN]`** — informasjonsskrivet er ikke skrevet, og personvernerklæringen i appen
(`Views/Home/Privacy.cshtml`) er tom og venter på det samme.

Begge må være på **norsk**, selv om grensesnittet er engelsk, og på et språk en fjortenåring og
en forelder forstår — gjerne én versjon for spilleren og én for foresatte. Utover det vanlige
(hvem som er ansvarlig, formål, grunnlag, lagringstid, rettigheter, klage til Datatilsynet) må
de si det som ikke er selvsagt her:

- at **alle trenere i klubben** kan se alle svar, også på lag de ikke trener, at hvert oppslag
  logges, og at spilleren selv kan se loggen,
- at det spilleren og foresatt **skriver**, leses av den andre og av alle trenere, og at det
  treneren skriver kommer først når treneren deler det,
- hva man ikke bør skrive i fritekst,
- at et påbegynt skjema lagres på enheten til det er sendt,
- hvilke cookies og hvilken lagring i nettleseren som brukes,
- hvor dataene lagres, og hvem som drifter (punkt 8),
- hvordan man ber om innsyn, retting og sletting. I dag går alt gjennom en administrator.

## 7. Tilgangsstyring

Rolle alene avgjør ikke tilgang: den vurderes per spiller (`CanViewPlayer`). Men for trenere og
administratorer er svaret alltid ja.

| Hvem | Ser tallsvar | Ser fritekst | Kan svare om spilleren | Oppslaget logges |
| --- | --- | --- | --- | --- |
| Spilleren selv | Egne. Foresattes og trenerens når treneren har frigitt | Egen og foresattes. Trenerens når treneren har frigitt | Ja, om seg selv | Nei |
| Foresatt | Det samme som spilleren — kun eget barn, og kun der koblingen finnes i `Guardianships` | Det samme som spilleren | Ja | Nei |
| Trener | Alle spillere i klubben, fra alle tre, med en gang | Alle tre, med en gang | Ja, om alle spillere | Ja |
| Administrator | Alt | Alt | Nei | Ja, bortsett fra via foresattsiden (A4) |

**Trenerrollen er ikke knyttet til lag.** `CoachTeams` finnes, men begrenser ingenting: en
G16-trener ser og kan skrive om seniorspillere. Klubben har bedt om det, men det må kunne
forsvares som nødvendig for formålet (art. 5 nr. 1 bokstav c og art. 25). **`[KLUBBEN]`**
bekrefter.

**Revisjonslogg.** Når en trener eller administrator åpner svarene til én spiller, skrives en
rad i `PlayerAccessEvents`: hvem, hvilken spiller, hvilken side, hvilken periode, når. Loggen
er append-only. Sidene som skriver til den er `Coach/FiveCPlayer`, `Coach/FiveCTeam` (én rad
per spiller på laget), `Coach/PlayerDetail` og `Admin/Export`.

Det som ikke logges:

- spillerens og foresattes egne besøk,
- skjemalisten (`/Survey`), som viser kode, posisjon, lag og alder for alle spillere til alle
  trenere,
- **`/Guardian/Player/{id}` for andre enn foresatte.** Administratorer, og trenere som også har
  rollen Guardian, kan åpne hvilken som helst spiller der — svar og fritekst — uten at det
  skrives en rad. Det er en feil: spillerens side sier at hvert oppslag fra en administrator
  registreres (A4).

Det finnes ingen side der klubben leser loggen. Den leses i databasen, eller via innsyn.

**Spilleren ser loggen selv**, i fanen «Who has looked» på sin egen side: de 20 siste
oppslagene, med rolle og tidspunkt. Det er en bevisst utvidelse av innsynsretten: den
registrerte kan se at oppslag har skjedd, ikke bare be om å få vite det.

**Spilleren ser ikke trenerens svar før treneren deler dem.** Rekkefølgen er: spilleren
svarer, treneren svarer, spilleren får vite at treneren *har* svart — og at treneren har
skrevet noe, ikke hva — og treneren frigir. Grunnen er at et avvikstall eller en setning som
dukker opp uanmeldt på en fjortenårings telefon er noe annet enn det samme i en samtale.
Trenerens tall og tekst fjernes fra modellen før siden bygges
(`FiveCFeedbackViewModel.Redact`), ikke bare i visningen.

Frigivelsen har to svakheter (A11):

- Den gjelder spilleren og perioden, ikke treneren. Hvilken som helst trener eller
  administrator kan frigi, og det som frigis, er den nyeste trenerinnsendingen, uansett hvem som
  skrev den.
- Svarer flere trenere, eller to foresatte, om samme spiller, lagres alle, men bare den nyeste
  vises. De andre blir liggende uten å bli brukt. På trenersiden står den nyeste trenerens tekst
  merket «You», også for en trener som ikke skrev den.

**Lagsnitt vises ikke når færre enn tre spillere har svar** (`CanViewTeamAggregateRequirement`),
ellers kan snittet regnes tilbake til enkeltpersoner. Alle spillere er med i snittet, uansett
samtykkenivå (punkt 5).

## 8. Lagring og sikkerhet

| | |
| --- | --- |
| Hvor | Supabase (Postgres), region `eu-west-1` — **Irland, innenfor EØS** |
| Databehandler | Supabase. [Databehandleravtalen](https://supabase.com/legal/dpa) er en del av vilkårene, med Supabase Pte. Ltd. i Singapore som part. Dataene lagres i valgt region og behandles «primarily» der, og overføring ut av EØS dekkes av EUs standardkontrakter. Meldeskjemaet spør om overføring til land utenfor EØS, og svaret er ikke et rent nei. **`[KLUBBEN]`**/UiA vurderer |
| Eier av Supabase-prosjektet | **Må sjekkes.** Den som godtok vilkårene, er avtaleparten. For ekte data må det være behandlingsansvarlig, ikke en students konto |
| Vert for appen | **Ikke bestemt.** Repoet har ikke noe driftsoppsett. Verten blir også databehandler og ser loggene |
| Overføring | TLS med verifisert sertifikat (`SSL Mode=VerifyFull` og Supabase-CA-en), ikke bare kryptert |
| Passord og nøkler | Aldri i kildekoden. Ligger i user-secrets lokalt og i miljøet i drift |
| Autentisering | ASP.NET Core Identity. Minst 12 tegn, kontoen låses i 15 minutter etter fem mislykkede forsøk, og en bruker som mister en rolle, mister tilgangen innen fem minutter |
| Selvregistrering | Stengt. Kontoer opprettes av klubben |
| Kontosidene i Identity | **Åpne** (`/Identity/Account/Manage/*`). En bruker kan legge inn telefonnummer, laste ned «personal data» — bare kontofeltene, ikke svarene — og slette sin egen konto. Den slettingen går utenom sletterutinen og etterlater svarene og en `Player.UserId` uten bruker (A6) |
| Sesjon | To timer, glidende. «Remember me» gir en cookie som overlever at nettleseren lukkes. Cookies er HttpOnly, SameSite=Strict, https utenfor utvikling |
| Sider i nettleserens cache | HTML til innloggede sendes `no-store` — delt PC hjemme er normalen |
| Påbegynte skjemaer | Blir liggende i `localStorage`, også etter utlogging. Det undergraver `no-store` på raden over (A5) |
| Øvrig | CSP uten `unsafe-inline`, antiforgery på alle skrivende kall, rate limiting, HSTS, `Referrer-Policy: no-referrer` |

Åpne punkter før drift:

- **Ekte data skal ikke inn i databasen vi utvikler mot** (A10). Den i `appsettings.json`
  brukes av hele gruppa, alle har passordet, og utviklingsmiljøet fyller den med demokontoer —
  administrator inkludert — med et standardpassord som står i kildekoden. Drift trenger et eget
  Supabase-prosjekt uten demodata, med færrest mulig som har tilgang.
- `AllowedHosts` slipper som standard bare gjennom lokale navn, og appen advarer i loggen hvis
  den står på `*`. I drift settes det faktiske vertsnavnet.
- Bak en proxy må `ForwardedHeaders:KnownProxies` fylles inn, ellers ser rate limiteren bare
  proxyens IP-adresse.
- Lagring via PostgREST (`FiveC:Supabase:Url` og `ApiKey`) skriver svarene til et annet
  Supabase-prosjekt, der innsyn og sletting ikke når. Det skal ikke brukes med ekte data.

## 9. Varighet og sletting

**`[KLUBBEN]`** — hvor lenge skal svarene beholdes? Forslag til utgangspunkt: ut sesongen
pluss ett år, slik at utvikling over tid kan vises over minst to sesonger, og deretter
sletting. Det må være et bevisst valg, ikke «til noen rydder». Friteksten kan godt ha kortere
lagringstid enn tallene.

**Sletting av en spiller er bygget** (`/Admin/Delete/{id}`). Administratoren bekrefter ved å
skrive av spillerkoden. I én transaksjon slettes:

- 5C-innsendinger med tallsvar og fritekst, og svar i de gamle tabellene,
- samtykkelogg, revisjonslogg og frigivelser,
- foresattkoblinger,
- spillerens egen brukerkonto, hvis den finnes.

Slettingen registreres i `PlayerDeletionEvents`: spiller-ID, hvem og når, og ingenting om
spilleren. Samtykkehistorikken forsvinner med spilleren, så etter slettingen kan klubben ikke
lenger dokumentere hva som var samtykket til.

Slettingen tar **ikke**:

- kontoene til foresatte, heller ikke når de ikke lenger har noe barn i systemet,
- applikasjonslogger og backup i Supabase,
- innsynsfiler som er lastet ned — og slettesiden råder til å eksportere først,
- påbegynte skjemaer i nettlesere.

For å kunne følge en lagringstid mangler (A7):

- **sletting per periode, eller automatisk sletting.** I dag kan eldre svar bare fjernes ved å
  slette hele spilleren, eller direkte i databasen — og da uten logg,
- en sletterutine for foresatte og trenere,
- en side for å finne spilleren som skal slettes. Administratoren må kjenne ID-en.

Prosjektets sluttdato: **`[KLUBBEN]`** / UiA. Hva skjer med dataene når praksisprosjektet er
over og gruppa er ferdig?

## 10. Den registrertes rettigheter

| Rettighet | Status |
| --- | --- |
| Innsyn | Bygget for spillere. En administrator laster ned alt om én spiller som JSON (`/Admin/Export/{id}`), og oppslaget logges. Andre personers bruker-ID-er er byttet ut med «Coach 1», «Guardian 1» osv. Spilleren ser dessuten egne svar, refleksjonen og de siste oppslagene i appen |
| Innsyn — det som mangler | Fila har ikke med kontoopplysningene (e-post, ev. telefonnummer), og viser periode og lag som ID-er og spørsmål som nøkler (f.eks. `commitment-1`) i stedet for teksten. Den må forklares ved utlevering (A8). Innsyn for foresatte og trenere er ikke bygget |
| Retting | Ja, så lenge perioden er åpen: hver respondent kan endre sitt eget skjema. Etter at perioden er stengt, finnes ingen vei utenom databasen |
| Sletting | Bygget for spillere (punkt 9). Ikke for foresatte og trenere, bortsett fra kontosiden i Identity, som går utenom (A6) |
| Trekke samtykke | Skjemaet finnes, men er ikke lenket, og et tilbaketrukket samtykke stopper ingenting (punkt 5) |
| Dataportabilitet | JSON-fila fra innsynet er maskinlesbar og dekker spilleren |
| Protestere (art. 21) | Bare aktuelt hvis grunnlaget blir berettiget interesse (punkt 5). Ikke bygget |

## 11. Vurdering av personvernkonsekvenser

Vi mener det må gjøres en vurdering av personvernkonsekvenser (DPIA, art. 35). Systemet
vurderer barn systematisk over tid og markerer dem for oppfølging. Det treffer to av kriteriene
i veilederen fra Artikkel 29-gruppen (WP248) — evaluering eller poengsetting, og sårbare
registrerte — og to kriterier er normalt nok. Datatilsynets liste over behandlinger som alltid
krever vurdering har det samme i skolen: å «evaluere læring, mestring og trivsel». Friteksten
kan legge til et tredje kriterium: opplysninger av svært personlig karakter.

Hvem som gjør den, følger av punkt 1: **`[KLUBBEN]`** / UiA.

---

## Avvik mellom kode og løfter

| | Avvik | Punkt | Rettes av |
| --- | --- | --- | --- |
| A1 | Samtykkenivået styrer ingenting | 5 | Klubben/UiA velger, så kode |
| A2 | Samtykkeskjemaet er ikke lenket, er på engelsk, viser rå bruker-ID-er og stenger ute myndige spillere | 5 | Kode |
| A3 | Grensen for foresatt er 19 år, ikke 18, og foresattes samtykke og tilgang har ingen aldersgrense | 5 | Klubben/UiA velger, så kode |
| A4 | Oppslag via `/Guardian/Player/{id}` logges ikke for andre enn foresatte | 7 | Kode |
| A5 | Påbegynte skjemaer, med fritekst, blir liggende i `localStorage` etter utlogging | 4, 8 | Kode: `sessionStorage`, eller tømmes ved utlogging |
| A6 | Kontosidene i Identity: telefonnummer, og kontosletting utenom sletterutinen | 8, 10 | Kode: steng alt utenom passordbytte |
| A7 | Ingen sletting per periode eller automatisk sletting, og ingen rutine for foresatte og trenere | 9 | Kode, når lagringstiden er bestemt |
| A8 | Innsynsfila mangler kontoopplysninger og tekst | 10 | Kode |
| A9 | De gamle tabellene `Items`, `Responses` og `Answers` | 4 | Kode (migrasjon) |
| A10 | Utviklingsdatabasen, med demokontoer, kan ikke brukes til ekte data | 8 | Oppsett |
| A11 | Frigivelsen gjelder spilleren og ikke treneren, og bare nyeste trener og foresatt vises | 7 | Kode |
| A12 | Skjemaet sier ikke hva som ikke skal skrives i fritekst | 4 | Trenerteamet, i spørsmålsfila |

## Hva som gjenstår før dette kan sendes

1. **`[KLUBBEN]`**/UiA avklarer behandlingsansvarlig — og dermed om klubbens egen bruk skal
   meldes til Sikt i det hele tatt (punkt 1).
2. **`[KLUBBEN]`**/UiA velger rettslig grunnlag: håndhevet samtykke eller berettiget interesse
   (punkt 5).
3. **`[KLUBBEN]`** bekrefter at alle trenere skal se og skrive om alle spillere (punkt 7).
4. **`[KLUBBEN]`**/UiA bestemmer fra hvilken alder spilleren samtykker selv, og hva foresatte
   ser etter det (punkt 5).
5. **`[KLUBBEN]`** avgjør om fritekst trengs, og hva skjemaet skal si om den (punkt 4).
6. Lagringstid bestemmes (punkt 9).
7. Databehandleravtaler med Supabase og verten, vurdering av overføring ut av EØS, og hvem som
   eier Supabase-prosjektet (punkt 8).
8. Vurdering av personvernkonsekvenser (punkt 11).
9. Informasjonsskriv og personvernerklæring skrives, på norsk (punkt 6).
10. Endelig spørsmålssett legges ved (punkt 4).
11. Avvikene A1–A12 rettes, eller beskrives her slik de er.

Punkt 11 ligger i hovedsak hos oss — men A1 og A3 venter på punkt 2 og 4, og A12 er
trenerteamets. Resten er klubbens og UiAs.

Skal prosjektet meldes til Sikt, må meldeskjemaet være sendt senest 30 dager før
datainnsamlingen starter, og ingenting kan samles inn før Sikt har svart.
