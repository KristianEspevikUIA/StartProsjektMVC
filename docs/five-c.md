# 5C-spørreskjemaet

Tjuefem påstander i fem kategorier, besvart på en skala fra 1 til 5 av spilleren, foresatte og
treneren — og en trenervisning som viser hvor de tre er uenige. Etter påstandene kommer en
kort refleksjon med egne ord: den sterkeste C-en, den det er verdt å jobbe med neste gang, og
hva som ville hjelpe.

All tekst i grensesnittet er engelsk, i tråd med StartCompass-nettstedet og wireframene.

---

## Hvor ting ligger

| Hva | Hvor |
| --- | --- |
| **Spørsmålene**, og refleksjonen etter dem | `Data/Questions/five-c-questions.json` |
| Innlesing og validering av dem | `Services/FiveC/QuestionCatalog.cs` |
| Skjemaet | `Controllers/SurveyController.cs`, `Views/Survey/` |
| Det som sendes når et skjema leveres | `Contracts/FiveC/SurveySubmission.cs` (+ speilet i `.ts`) |
| Lagring | `Services/FiveC/ISurveySubmissionStore.cs` og de to implementasjonene |
| Spiller mot foresatt mot trener | `Services/FiveC/FiveCAnalysisService.cs` |
| Differanseskårene | `Services/FiveC/FiveCDifference.cs` |
| Trenervisningene | `CoachController.FiveCTeam` / `.FiveCPlayer`, `Views/Coach/` |
| Refleksjonen: hva den er og hvordan den lagres | `Models/FiveC/QuestionSet.cs`, `Models/FiveCSubmission.cs` |
| Refleksjonen, slik den leses tilbake | `Views/Shared/_FiveCReflection.cshtml` |
| Skalagrensene, oppfølgingsregelen og differansebåndene | `Models/FiveC/FiveCRules.cs` |
| Stil | `wwwroot/css/startcompass.css` |

---

## Bytte ut spørsmålene

Rediger `Data/Questions/five-c-questions.json`. Ingenting annet.

Ingen `.cshtml`-fil inneholder et kategorinavn eller en påstand — skjemaet, forsiden og
treneroversikten leser alle katalogen. Å legge til en sjette kategori, eller gå ned til fire
spørsmål i en av dem, krever heller ingen kodeendring; appen logger en advarsel når formen
ikke er 5 × 5, men den kjører.

To regler når du redigerer:

- **`key` er det svarene lagres mot.** Skriv om `text` så mye du vil. Endrer du en `key`,
  blir hvert svar som allerede er gitt på den, stående uten spørsmål — det er et annet
  spørsmål, ikke et omformulert.
- **`reversed: true`** merker en negativt formulert påstand. Den skåres som `6 − verdi` når
  den leses, så en høy skår alltid betyr «bra». Ikke snu skalaen i skjemaet for å
  kompensere; da snus den to ganger.

`textAboutPlayer` er valgfri og null overalt i dag, så alle tre rollene får et identisk
skjema. Fyll den ut, og trenere og foresatte ser den ordlyden mens spilleren beholder `text`
— det er kroken wireframene beskriver, klar, men ikke slått på.

Fila valideres ved oppstart. En duplisert nøkkel, en manglende `text` eller en skala som ikke
henger sammen, stopper applikasjonen med en melding som navngir problemet. Det er med vilje:
alternativet er å oppdage det som et halvtomt skjema, midt i en runde, foran en fjortenåring.

`version` lagres med hver innsending, så det er mulig å vite i ettertid hvilken ordlyd et
sett svar ble gitt mot.

---

## Refleksjonen som avslutter perioden

Fem spørsmål etter de tjuefem, stilt til alle tre respondentene. To av dem er et valg mellom
de fem C-ene; tre besvares med respondentens egne ord. Start ba om dem: påstandene sier
*hvor* en spiller er, og et tall kan ikke si hva som bør gjøres videre.

De ligger i samme fil, i en `reflection`-blokk etter `categories`:

```json
"reflection": {
  "title": "End of period",
  "description": "Five short questions to close the period. …",
  "noAnswerLabel": "Not answered",
  "questions": [
    { "key": "reflection-strength", "type": "category",
      "text": "Which C has been your greatest strength during this meso period?",
      "textAboutPlayer": "Which C has been the player's greatest strength …" },
    { "key": "reflection-strength-example", "type": "text", "maxLength": 500,
      "text": "Please give one example of when you noticed this." }
  ]
}
```

- **`type: "category"`** besvares ved å velge en av de fem C-ene. Det som lagres, er
  kategoriens *nøkkel* — `confidence`, ikke `Confidence` — slik at et valg kan telles uten å
  matche på en overskrift som ventes å bli skrevet om. Alternativene bygges fra `categories`,
  så en sjette C dukker opp her også, uten kodeendring.
- **`type: "text"`** besvares med ord. `maxLength` setter taket (1000 som standard, aldri
  over `FiveCRules.ReflectionTextLimit`, som er størrelsen på kolonnen). Nettleseren får et
  `maxlength`-attributt, og serveren sjekker det samme tallet.
- **`required` er `false` med mindre et spørsmål sier noe annet.** De tjuefem påstandene er
  målingen; et obligatorisk avsnitt etter dem besvares med et punktum. Det er et flagg per
  spørsmål i fila hvis klubben bestemmer noe annet.
- **`noAnswerLabel`** er veien ut igjen av et `category`-spørsmål. En radioknapp kan ikke
  velges bort ved å klikke på den en gang til, så uten dette er den første C-en noen rører,
  den de blir sittende med — på et spørsmål siden nettopp har sagt er valgfritt. Den vises
  som siste alternativ i gruppa, sender en tom verdi, og lagres som ikke noe svar i det hele
  tatt: raden forsvinner, og resultatet kan ikke skilles fra å aldri ha valgt. Tilbys på
  hvert kategorispørsmål som ikke er `required`.
- Hele blokka er valgfri. Fjern den, og skjemaet er de tjuefem påstandene det var.
- Refleksjonsnøklene deler ett navnerom med påstandsnøklene. Katalogen nekter å laste en fil
  der en nøkkel er brukt to ganger.

Skrevne svar er **fritekst om et barn**, og derfor holdes de atskilt fra tallene: egen tabell
(`FiveCReflectionAnswers`), aldri skåret, aldri med i et snitt, og tatt eksplisitt med i
admin-eksporten. De følger innsendingen, som følger spilleren, så en sletting tar dem med
seg.

Skjemaet teller dem også for seg: fremdriftslinja øverst teller bare påstandene, og
refleksjonsfanen er merket valgfri, så ingenting flagger den som uferdig. Fanens egen teller
ser bort fra et valgt «Not answered»: ellers ville det å fjerne et valg latt telleren stå der
den var, og det ville vært en teller som ikke kan gå ned.

### Hvem leser den

| Side | Ser |
| --- | --- |
| `/Coach/FiveCPlayer` | alle tre refleksjonene, fra de er skrevet |
| `/Player`, `/Guardian/Player/{id}` | sin egen og foresattes — og trenerens **først når treneren har delt** |

Trenerens ord følger samme frigivelsesregel som trenerens tall, og av en sterkere grunn: en
setning om en fjortenåring er skarpere enn et snitt, ikke mildere.
`FiveCFeedbackViewModel.Redact` fjerner den fra modellen før viewet rendres, så det er
ingenting på den siden å lekke. Fram til da sier siden at treneren har skrevet noe og vil gå
gjennom det — det samme den allerede sier om tallene deres.

Begge sidene rendrer `Views/Shared/_FiveCReflection.cshtml`; bare kolonneoverskriftene er
forskjellige. To kopier av en side som viser hva noen skrev om et barn, ville vært to steder
å få regelen feil.

---

## Dele et skjema som lenke

```
/Survey/Fill?roundId=2&playerId=14&role=Coach
```

`role` er valgfri. Utelates den, brukes den mest direkte rollen den innloggede brukeren har
for den spilleren.

**Lenken gir ingen tilgang i seg selv.** Den forhåndsvelger perioden, spilleren og rollen, og
ikke noe mer. Den som følger den, logger inn som seg selv, og serveren kjører begge sjekkene
på nytt:

1. `CanViewPlayer` — den eksisterende ressurspolicyen.
2. Om brukeren faktisk har rollen i lenken (`SurveyAssignmentService`). En trener kan ikke
   svare som spilleren ved å redigere query-strengen.

Begge kjøres på GET og igjen på POST. De skjulte feltene i skjemaet er inndata, ikke bevis.

### Hvorfor ikke et token per innsending

Et engangstoken i URL-en er det andre opplagte alternativet, og det er det «unik lenke per
innsending» vanligvis betyr. Det ble ikke valgt, fordi et token som virker uten innlogging,
er et ihendehaverbevis til en mindreårigs opplysninger: det overlever i meldingshistorikk, i
videresendt e-post og i nettleserhistorikken, og det kan ikke knyttes til en person i
ettertid. Dette repoet er bygget den andre veien — selvregistrering er stengt,
fallback-policyen nekter som standard, og tilgang avgjøres per spiller, ikke per rolle.

Vil klubben likevel ha lenker som virker uten konto — realistisk sett for foresatte — er det
en bevisst beslutning med et reelt design bak: engangs, kortlevd, lagret hashet, avgrenset
til én spiller og én periode, og mulig å trekke tilbake. Det er ikke en query-parameter.

---

## Lagring av svar

> **Denne delen er nå halvveis utdatert, med vilje.** Den ble skrevet da appen kjørte på
> lokal SQLite og Supabase var noe eget man nådde over HTTP. Siden 26. august kobler appen
> seg direkte til Postgres-databasen i Supabase, gjennom EF Core og Npgsql — se «Kom i gang»
> i README.
>
> Det gjør `SupabaseSurveySubmissionStore` overflødig: den går ut over PostgREST til en
> database prosessen allerede er koblet til, med en ekstra nøkkel, ingen felles transaksjon
> og ingen fremmednøkler til `Players` eller `SurveyRounds`. Det enkle nå er to EF-entiteter
> og én migrasjon, som alt annet i modellen. Lagerabstraksjonen kan bli — det er den som
> holder minnelageret mulig — men den levende implementasjonen bør være en EF-implementasjon.
>
> Ikke endret ennå, fordi 5C-tabellene er Victors å definere, og dette er hans avgjørelse.
> Kontrakten i `Contracts/FiveC/` er upåvirket uansett: den beskriver hva skjemaet leverer
> fra seg, ikke hvordan det skrives.
>
> **Ikke bruk anon-/publishable-nøkkelen (`sb_publishable_…`) til dette.** Den er laget for
> å være offentlig, og den er underlagt row level security. Svar om mindreårige bak en nøkkel
> som sendes til nettlesere, er feil form uansett hva policyene sier.

`ISurveySubmissionStore` har tre implementasjoner, og konfigurasjonen velger én:

- **`EfSurveySubmissionStore`** — **standarden**, og det som kjører med mindre noe er
  konfigurert. Svarene havner i appens egen database, som etter overgangen til Npgsql *er*
  Supabase: `FiveCSubmissions`, `FiveCAnswers` og `FiveCReflectionAnswers`, med ekte
  fremmednøkler til `Players` og `SurveyRounds`. Én nøkkel, én tilkobling, én transaksjon.
- **`SupabaseSurveySubmissionStore`** — brukes når både `FiveC:Supabase:Url` og `:ApiKey` er
  satt, for et *genuint separat* Supabase-prosjekt. Snakker direkte med PostgREST; ikke noe
  klientbibliotek.
- **`InMemorySurveySubmissionStore`** — bare når `FiveC:Store` er satt til `"InMemory"`.
  Svarene ligger i minnet og er borte når prosessen stopper, og det er det som gjør den
  nyttig til en demo og ubrukelig til alt annet. I Development seeder den seg selv med
  oppdiktede innsendinger, så treneroversikten har noe å tegne.

En tom `Url` eller `ApiKey` betyr derfor databasen, ikke minnet. Det var omvendt mens appen
kjørte på lokal SQLite, og hvert svar forsvant ved omstart.

Hvilken som er i bruk, skrives til loggen ved oppstart og vises for admin på `/Survey`.

Alle tre implementerer `CountByRoundAsync`, som svarer på «hvor mange innsendinger har hver
av disse periodene» i én rundtur. Periodelista for admin er den eneste som kaller den, og den
spurte tidligere per periode — og leste hver innsending med alle tjuefem svarene bare for å
kalle `.Count` på lista.

### Konfigurasjon

`appsettings.json` har URL-en og tabellnavnene. Nøkkelen står ikke der:

```bash
dotnet user-secrets set "FiveC:Supabase:ApiKey" "..." --project StartPraksisGruppe3Prosjekt
```

Bruk service role-nøkkelen. Respondenten er innlogget *her*, ikke i Supabase, så det finnes
ingen bruker-JWT å sende videre, og row level security kan ikke vite hvem som svarer. Det
betyr også at nøkkelen aldri må nå nettleseren — og det gjør den ikke: hver forespørsel til
Supabase gjøres på serveren.

### Kontrakten

`Contracts/FiveC/SurveySubmission.cs` er det skjemaet leverer fra seg. `survey-submission.ts`
er det samme i TypeScript, for Supabase-siden; C#-fila er den som faktisk kjører, og er de to
uenige, vinner C#-fila.

```json
{
  "round_id": 2,
  "player_id": 14,
  "player_code": "Brage Kristoffersen",
  "respondent_role": "coach",
  "respondent_user_id": "9f0c...",
  "question_set_version": "placeholder-2026-08-26",
  "submitted_at": "2026-08-26T07:30:00+00:00",
  "answers": [
    { "question_key": "commitment-1", "category_key": "commitment", "value": 4 }
  ],
  "reflection": [
    { "question_key": "reflection-strength", "value": "confidence" },
    { "question_key": "reflection-strength-example", "value": "Took the last penalty at 2-2." }
  ]
}
```

**Databaseskjemaet er Victors.** De to tingene denne siden er avhengig av:

1. **Én innsending per `(round_id, player_id, respondent_user_id)`.** Å sende inn på nytt er
   en retting, ikke en rad til. Lageret gjør upsert på de tre kolonnene, så de trenger en
   unik indeks — ellers blir et rettet skjema stille til to meninger.
2. **`value` må kunne være null.** Null betyr «ikke besvart», og null er ikke 3. En
   `NOT NULL`-kolonne gjør hvert blanke svar til en middels mening, og det er umulig å se
   forskjellen etterpå.

Det ventede landingsstedet er tre tabeller:

```
five_c_submissions (id, round_id, player_id, player_code, respondent_role,
                    respondent_user_id, question_set_version, submitted_at)
five_c_answers     (submission_id -> five_c_submissions, question_key, category_key, value)
five_c_reflection_answers
                   (submission_id -> five_c_submissions, question_key, value text)
```

Den tredje holder den skrevne refleksjonen. Den er atskilt fordi ingenting i den skåres, og
fordi den er fritekst om et barn — se «Refleksjonen som avslutter perioden» over. Et blankt
svar etterlater ingen rad: ikke besvart er fraværet av en rad, ikke en rad som holder
ingenting.

Tabell- og kolonnenavn er konfigurasjon, ikke konstanter, så å gi en av dem nytt navn er en
endring i `appsettings.json` og ikke i koden.

**Ikke verifisert mot et ekte prosjekt ennå.** Tabellene fantes ikke da dette ble skrevet, så
forespørslene følger PostgREST-dokumentasjonen og ikke en faktisk kjøring. De to POST-ene i
`SupabaseSurveySubmissionStore` er det første å sjekke når tabellene er oppe.

---

## Treneroversikten

`/Coach` → lagkort → `/Coach/FiveCTeam/{id}` → `/Coach/FiveCPlayer/{id}`.

**Per kategori, per spiller** står spillerens, foresattes og trenerens snitt side om side som
stolper. Stolpene er inline SVG fordi CSP-en ikke har `unsafe-inline`: en stolpe kan ikke få
bredden sin fra et `style=""`-attributt, men `width` på en SVG-`rect` er markup og er
upåvirket. Et diagrambibliotek ville trengt en CDN, som CSP-en også blokkerer.

### Differanseskårene

Tre tall øverst på `/Coach/FiveCPlayer/{id}`, og de samme tre per kategori lenger ned:

| Skår | Hva den sammenligner |
| --- | --- |
| **Coach vs player** | Trenerens svar mot spillerens egne. |
| **Guardian vs player** | Foresattes svar mot spillerens egne. |
| **Between all three** | Snittet av de tre parvise skårene — trener/spiller, foresatt/spiller og trener/foresatt. |

Hver skår er en **gjennomsnittlig absoluttdifferanse per påstand**, på samme 1–5-skala som
svarene. Den går fra 0 til 4: 0 er samme svar hver gang, 4 ville vært motsatte ender av
skalaen på alle tjuefem.

Tre regler gjør at tallet betyr det det sier:

- **Paret på spørsmålsnøkkelen, ikke på kategorisnittet.** En 5 og en 1 gir samme snitt, 3,
  som to 3-ere. En differanse bygget på snitt ville kalt det enighet, så
  `RespondentGap.Between` parer de to respondentene påstand for påstand.
- **Bare påstander begge har svart på.** Det finnes ingen avstand mellom et svar og et blankt
  felt, så en ubesvart påstand tas ut i stedet for å telle som noe.
- **Reverserte påstander skåres først.** Differansen måles på skårer, ikke på råsvar, så en
  negativt formulert påstand kan ikke snu fortegnet på et avvik.

Ved siden av skåren uten fortegn beholder `RespondentGap.SignedDifference` **retningen** —
positiv betyr at respondenten til venstre vurderte spilleren høyere. De to sier forskjellige
ting, og siden viser begge: en liten retning oppå en reell avstand er uenighet som opphever
seg selv, ikke enighet, og kortet sier det med ord.

`Between all three` degraderer ærlig. Med bare to respondenter er det det ene paret, og
overskriften endres til «Between all who answered», så et toveis tall aldri går for å være
et treveis. Med én respondent finnes ingen skår i det hele tatt — kortet sier hvilket skjema
som mangler, i stedet for å vise en null.

De tre båndene er `FiveCRules.AgreementThreshold` (0,5) og
`FiveCRules.LargeDifferenceThreshold` (1,0), og `FiveCRules.LevelOf` runder til én desimal
før den plasserer i bånd — båndet står ved siden av tallet på skjermen, og det tallet skrives
med én desimal.

**Oppfølgingsflagget** (`FiveCRules.NeedsFollowUp`) slår ut når en spillers eget snitt i en
kategori er **under 2,0**, med **minst 3 besvarte påstander** bak seg. Begge tallene er
konstanter i `FiveCRules` — ett sted å endre dem.

To er «Disagree»-punktet på skalaen, så en spiller under det på tvers av en hel kategori er
uenig i de positive påstandene i hele den. Minstekravet til antall svar er det som gjør det
*konsekvent* og ikke én dårlig dag. Det bygger på spillerens egne svar, aldri på hva noen
andre mener om dem.

Spillere med flagg får et rødt merke i lagtabellen, en rød rad, en rød stolpe i diagrammet og
et banner på detaljsiden.

Ingenting på disse sidene lagres. Hvert tall regnes ut på nytt fra råsvarene ved hver
forespørsel — samme regel som avviket i ti-påstandsskjemaet følger, og av samme grunn: en
lagret vurdering av en mindreårig overlever svarene bak den, samtykket som tillot den, og
runden den hørte til.

### Lagoversikten

Over spillerradene på `/Coach/FiveCTeam/{id}` leses troppen som én, på de samme tre nivåene
som en enkeltspiller leses på: på tvers av alle tjuefem påstandene, per kategori og per
påstand. Samme stolper, samme partial (`_FiveCCategoryChart`), samme 1–5-skala — og det er
poenget. En trener skal ikke måtte lære seg diagrammet på nytt ett nivå opp.

**Hvert tall er et snitt av spillere, ikke av svar.** På hvert nivå er troppens tall snittet
av spillernes tall på det nivået, så én spiller teller én gang enten hen svarte på fem
påstander eller tjuefem. Å slå sammen alle svarene i stedet ville latt den som fylte ut
skjemaet mest fullstendig, stille veie mest, og et lagsnitt skal beskrive den
gjennomsnittlige spilleren.

To ting skiller den fra radene under:

- Den avgjøres av **`CanViewTeamAggregate`**, én gang, mot det faktiske antallet spillere bak
  tallene — ikke av `CanViewPlayer` gjentatt for alle. Antallet er en del av ressursen, slik
  at en controller ikke kan hoppe over grensen ved å glemme å se på den.
- **Linja for én enkelt rolle holdes tilbake på samme grense.** Har færre enn
  `CanViewTeamAggregateRequirement.MinimumResponses` foresatte svart, er foresattsnittet de
  foresattes egne svar med et lags navn på. `TeamRoleAverage.From` slipper tallet i stedet
  for å sende det videre, så ingen visning har det å lekke — og `Withheld` holder «for få
  svarte» atskilt fra «ingen svarte», som er to forskjellige fakta om en runde.

Tallene per påstand her er **skårede, ikke rå** — det motsatte av påstandstabellen for én
spiller. De står ved siden av kategorisnitt på en skala der høyere er bedre, og et råsnitt på
en reversert påstand ville vært den ene kolonnen i seksjonen som pekte motsatt vei.
Påstandene er fortsatt merket `Reversed`, så en leser kan se hvilke som er snudd.

**Over tid** er det samme aggregatet målt gjentatte ganger: for hver periode og hver C,
snittet av spillernes egne snitt. Bare spillernes svar, av den grunnen den individuelle linja
gir — en lagslinje som flyttet seg når en trener skiftet mening, ville blitt lest som at
troppen hadde utviklet seg. En periode med for få spillere bak seg er et hull og ikke et
tegnet punkt, og siden navngir den: et uforklart hull i en linje leses som «ingen svarte», og
noen svarte.

### Én komponent, fire sider

Hver lange side i 5C-funksjonen er bygget på samme måte, av samme kode, og en trener eller en
spiller som har lært én, har lært alle:

| Side | Paneler |
| --- | --- |
| `Coach/FiveCTeam` | Overview, Per statement, Over time, Players |
| `Coach/FiveCPlayer` | Differences, The five C's, Statements, Over time, Sharing |
| `Shared/FiveCFeedback` | Status, Your answers, Who has looked |
| `Survey/Fill` | ett panel per C, pluss Back/Next og en levende teller for besvarte |

En seksjon melder seg på med `data-tab-panel` og en `data-tab-label`; `survey.js` gjør
resten. To ting er bevisst holdt UTENFOR panelene, fordi de er grunnen til at siden ble
åpnet og aldri skal ligge bak en fane: oppfordringen «svar på skjemaet» på
tilbakemeldingssiden, og **Save** på skjemaet.

Lagsiden har fire seksjoner — troppens snitt, det samme påstand for påstand, troppen over
tid, og spillerne — og stablet i én kolonne er det rundt fem skjermhøyder med rulling før en
trener når spilleren hen åpnet siden for.

`survey.js` gjør dem om til faner. Stripa **bygges av panelene som faktisk står der**, den
skrives ikke i viewet: en runde uten aggregat har ingenting å bryte ned og får ingen
«Per statement»-fane, og et lag med én periode får ikke noe trendpanel å bla til. Ingenting i
markupen må holdes i takt med det controlleren bestemte.

Det er en forbedring, ikke strukturen. Med JavaScript av finnes ingen stripe, og ingen
paneler er skjult — siden er kolonnen av seksjoner den alltid var, i samme rekkefølge. Det er
samme regel som søkefeltet følger, og grunnen til at fanene ikke rendres på serveren: en
stripe som ikke byttet noe, ville vært verre enn en lang side.

Valgt fane huskes per side i `sessionStorage`, så å åpne en spiller og komme tilbake fører
til seksjonen som var åpen. En `#sc-panel-N`-lenke vinner over den huskede, og
`data-tab-open` fra serveren vinner over begge — det er slik skjemaet åpner den første C-en
som fortsatt har en ubesvart påstand etter en avvist lagring, i stedet for å la feilmeldingen
ligge bak en fane ingen ble bedt om å trykke på.

**Skjemaet er det som gjør mer enn å bytte.** Det er fortsatt faner, og ser fortsatt ut som
de andre, men et skjema trenger framdrift: `initFormSteps` legger Back og Next til hvert
panel, skriver antall besvarte på hver fane (`3/5`), og merker fanene der noe fortsatt
mangler. Skjulte paneler sendes inn akkurat som de ville gjort i den lange kolonnen — fanene
endrer hva som står på skjermen, og ingenting ved hva som lagres.

### Finne en spiller i troppen

Spillertabellen filtreres levende, på navn og posisjon, over den troppen som allerede står på
siden. Ingenting hentes, og ingen kode forlater nettleseren — hver rad ligger i dokumentet,
og filteret avgjør bare hvilke som vises. Feltet er `hidden` i markupen og avdekkes av
`survey.js`, så med JavaScript av er tabellen komplett, og det dukker ikke opp en død
søkeboks.

Koder og posisjoner, fordi det er det eneste som finnes: dette systemet har ingen navn.

### Hva treneren ser og ikke ser

- **Ingenting holdes tilbake for en trener lenger.** En trener ser hver spiller og hvert
  tall. Koden som rendrer en rad uten tall, og formuleringen «no consent for individual
  views» ved siden av, er rester fra da samtykke stanset en trener — de kan ikke lenger nås.
  Å fjerne dem står på lista.
- «Has not answered» er fortsatt sin egen tilstand, og sier det fortsatt med ord i stedet
  for å vise en strek som kunne leses som en null.
- Tellinger av *hvem som har svart* sier ingenting om en enkeltperson. De ble vist for hver
  rad også da tallene ikke ble det, og det var derfor de ble skilt ut i utgangspunktet.
- Ikke noe fritekstfelt og ingen notater. En treners skrevne notat om en mindreårig er en ny
  kategori personopplysninger og finnes ikke i datamodellen.

---

## Kjente begrensninger

- **Ingenting begrenser hvilke spillere en trener kan nå.** En trener er en trener: hver
  trener ser hvert lag, får et skjema for hver spiller i klubben, og `CanViewTeam` /
  `CanViewTeamAggregate` ser ikke lenger på `CoachTeam`. Samtykke var den siste gjenværende
  grensen; klubben ba om at også den skulle bort, og det gjorde den.

  Det som står i stedet, er `PlayerAccessEvent` — en append-only logg over hvem som åpnet
  hvilken spiller, fra hvilken side, når. Den hindrer ingenting; den gjør hvert oppslag
  etterprøvbart. **Slutter den å skrives, har regelen i `CanViewPlayerHandler` ingen motvekt
  i det hele tatt**, så enhver ny side som viser én spillers svar, må kalle
  `IPlayerAccessLog.RecordAsync`.
- **`CoachTeam` er fortsatt i modellen, men gir eller begrenser ingenting lenger.** Tabellen,
  entiteten og de seedede radene er urørt — å fjerne dem er en skjemamigrasjon på en delt
  database, og ingen har bedt om det. Den eneste som fortsatt leser den, er seedingen av
  demodata i utvikling, som bruker den til å velge en plausibel trener. Skal den ikke
  tilbake, bør den fjernes bevisst, i en egen endring.
- **`/Survey` lister hver spiller i klubben for en trener**, og derfor har den siden filtre:
  periode, lag, rolle, status og navn. Blir den ubrukelig igjen ved noen hundre spillere, er
  svaret et bedre filter — ikke en stille retur til lagavgrenset tilgang, som er en
  autorisasjonsbeslutning og hører hjemme i `Authorization/`.
- **Samtykke avgjør ikke lenger noe en trener gjør.** Det registreres fortsatt, er fortsatt
  append-only, og vises fortsatt på trenerens spillerside — men ingenting i koden forgrener
  seg på det lenger. Slik må det beskrives i Sikt-meldingen, og klubben må bekrefte at det er
  det de vil. Se `docs/sikt-melding.md`.
- **`ConsentService.GetCurrentLevelsAsync` ble implementert her** (det var en av Brages
  TODO-er) fordi lagoversikten lister en hel tropp og ellers ville gjort én spørring per
  spiller. Samme regel som versjonen for én spiller. Resten av den tjenesten er urørt.
- **Ti-påstandsskjemaet har fortsatt ikke noe lagaggregat.** `CoachController.Team` er
  fortsatt en TODO. 5C-siden går nå gjennom `CanViewTeamAggregate` og grensen på tre svar, så
  det eldre skjemaet har et ferdig eksempel å følge i stedet for en policy ingen kaller.
- **Et lagsnitt kan ikke sammenlignes med et annet lags.** Hver tropp leses for seg, og det
  er bevisst inntil videre: en tabell som rangerer tropper av mindreårige, er en annen
  produktbeslutning, og ingen har bedt om den.
- **`Coach/Index` ble implementert** for å gjøre 5C-sidene tilgjengelige. Den rikere
  versjonen, med avvikstall for ti-påstandsskjemaet, er fortsatt Taavis.
