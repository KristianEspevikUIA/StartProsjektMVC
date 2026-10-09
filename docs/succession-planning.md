# Succession planning

Trenernes Excel-ark «IK Start Succession Planning», bygget inn i appen. Hver trener vurderer
spillerne hver åttende uke. Appen holder vurderingene hver for seg, legger dem sammen og viser
trenerne side om side. Den plukker også beste ellever i en formasjon og anslår hvor mange uker
hver spiller er unna å være klar.

Sidene ligger på `/Succession`, og menypunktet heter «Succession».

**Tilgang:** trener og administrator. Bare **trenere** kan vurdere. Administrator ser alt og kan
oppdatere kontraktsopplysningene, men vurderer ikke. Spillere og foresatte ser ingenting herfra,
heller ikke om seg selv.

---

## Hva trenerne ba om

Beskrivelsen de ga, og hvor i appen det ligger:

| De sa | I appen |
| --- | --- |
| Succession planning – Formation – Position? | «Best eleven» (`/Succession/Formation`) |
| Overview of excel data etc. | «Squad board» (`/Succession`), én rad per spiller |
| Kept separate, pulls it all together | Én vurdering per trener. Tavla viser snittet |
| Compare coaches responses – only answers for the players | Spillersiden, «Coaches side by side». Bare trenere svarer |
| Every 8 weeks | Faste åtteukers sykluser, se under |
| What would the best 11 in a 4-3-3 look like? | Banen på «Best eleven». 4-3-3 er ett av valgene; siden åpner på 3-5-2 (se neste rad) |
| Who's in the building to be the best fit for the formation? | Tabellen «Who is next in line» under banen |
| Where the player is «off» / how many weeks till fit | «Off and weeks to ready» på tavla, banen og spillersiden |
| Click into a team, say U17, and see the players in a formation | Laglenkene «Choose from» på «Best eleven», og «Best eleven» på hvert lag under My teams |
| Formation might be 1-3-5-2 | 3-5-2, og siden åpner nå på den. `?formation=1-3-5-2` virker også, og siden skriver «1-3-5-2 with the goalkeeper» |
| If we could move players around too. Think of Football Manager | Dra og slipp på banen, eller trykk på en spiller og så dit hen skal. Se «Banen som et spill» |
| Less on the players: name, position (maybe shirt number) | Hver spiller er en rund markør med posisjonen, fornavnet under og én rating. Draktnummer finnes ikke ennå, se under |
| Substitutes to the right of the eleven, not under | Innbytterne står til høyre fra nettbrettbredde (700 px) og opp. Bare på mobil havner de under |
| Stats/overall change with who is subbed in where | Team rating og en stolpe for angrep, midtbane, forsvar og keeper, regnet ut på nytt for hvert bytte |
| The formation should be 3-5-2 | Siden åpner på 3-5-2 |
| The 3-5-2 and the background look ugly, something like this (skisse av en bane) | Banen er en tegning i målestokk (`img/pitch.svg`), og hver spiller står der posisjonen står på banen. 3-5-2 har nå en 10-er foran to 8-ere, som i skissen |

---

## Fra Excel-arket til appen

Arket har to faner. «Text - Do Not Touch» har listene, og de ligger nå i
`Data/Succession/succession-planning.json`. «Template - Do Not Touch (1-10)» har kolonnene, og de
ligger slik:

| Kolonne i arket | I appen | Hvem fyller ut |
| --- | --- | --- |
| Last Name, First Name | Bare for å finne spilleren ved import, og lagres ikke. Se «Import av trenernes ark» | – |
| Coach/Coaches (Raters) | Den innloggede treneren, én vurdering hver | automatisk |
| Rated as (List) | `RatedAs`, fra nivålista. Aldersgruppene vises med klubbens lagnavn (G19, ikke U19s); arkets ord står i `workbook` i JSON-fila, så importen finner dem | trener |
| Ability Cat. (List) | `AbilityCategory` | trener |
| 1st/2nd/3rd Position (L) | `FirstPosition`, `SecondPosition`, `ThirdPosition` | trener |
| Year Born | `Player.BirthDate` (bare året vises) | finnes fra før |
| Contract type (L), Contract End | `PlayerSuccessionProfile` | trener eller admin, én gang per spiller |
| MESO Training Group (L) | `PlayerSuccessionProfile.TrainingGroup` | trener eller admin |
| Current Team (L) | `Player.Team` | finnes fra før |
| Physical … Availability (1-10) | `SuccessionRating`, én rad per vurdering. 0–10: trenerne bruker 0 også | trener |
| Overall Readiness (1-10) | **Regnes ut** hver gang, lagres ikke | – |
| Coaches Personal Readiness for next step | `PersonalReadiness` | trener |
| 0–6m, 6–18m, 18–36m Projection | tre korte tekstfelt | trener |
| Pathway Blocked?, External Needed? (Y/N) | ja / nei / ikke svart | trener |
| What Now?, Key Development Focus, Super Strengths, Notes | fritekst, med lengdegrense | trener |
| Succession Risk (Green/Amber/Red) | `SuccessionRisk` | trener |

Kontrakt og treningsgruppe er fakta og ikke vurderinger. Derfor legges de inn én gang per
spiller, ikke av hver trener.

### Formler og farger fra arket

| I arket | I appen |
| --- | --- |
| `T = SUM(N:S)/6` | Snittet av de seks vurderingene. Se forskjellen under |
| Fargeskala 1 (rød) – 6 (gul) – 10 (grønn) på vurderingene | `.sc-rate--1` … `--10`, ett trinn per hele poeng |
| Ikonsett med lys ved 6 og 8 på readiness | Not yet < 6 ≤ Developing < 8 ≤ Ready (`developingAt`, `readyAt`) |
| Ability: Potential grønn, Performance gul, Squad rød, P&P blå | `tone` på hver kategori |
| Kontrakt: Pro grønn, Youth gul, Non rød | `tone` på hver kontraktstype |

**Én forskjell, med vilje.** `SUM(N:S)/6` teller en tom celle som 0. En trener som ikke har fylt
inn Availability ennå, gjør spilleren en sjettedel dårligere. Appen utelater tomme vurderinger,
og skjemaet krever alle seks. I praksis gir de to samme tall.

---

## Sykluser

Åtte uker, regnet fra mandag 5. januar 2026 (`cycle.firstCycleStartsOn`). Syklusene følger
etter hverandre uten hull, så hver dato hører til nøyaktig én syklus. Ingen trenger å opprette
den neste. En vurdering lagrer syklusens første dag.

- Man vurderer i **gjeldende** syklus. Tidligere sykluser er historikk og kan ikke endres.
- Å vurdere samme spiller på nytt i samme syklus **erstatter** vurderingen. Unik indeks på
  (spiller, syklus, trener).
- Skjemaet fylles ut fra trenerens forrige vurdering når det ikke finnes en i denne syklusen, og
  sier fra om det. Da trenger treneren bare endre det som har flyttet seg.
- Har ingen vurdert en spiller i denne syklusen ennå, viser tavla forrige syklus, grået ut og
  merket med datoene. Uten det ville tavla vært tom første dag i hver syklus.

---

## Utregningene

Alt ligger i `Services/Succession/SuccessionMath.cs`.

**Sammen («Together»).** Snittet av trenerne, der hver trener teller én gang. Overall er snittet
av hver treners egen overall, ikke av alle enkeltvurderinger. Det er samme regel som lagsnittet
i 5C.

**Kategorier.** Svaret flest trenere ga, vinner. Ved likt antall står det «Split», og appen velger
ikke. Succession risk viser det alvorligste noen trener satte, og hvor mange som satte det.

**Posisjoner.** Tavla har tre kolonner, 1st, 2nd og 3rd, som i arket. Hver kolonne er en egen
avstemning: det flest trenere skrev i den kolonnen. Ved likt antall står begge posisjonene, merket
«Split». Beste ellever bruker en annen regel, der en posisjon teller med beste rangering noen
trener ga (se under). Det er med vilje: kolonnene svarer på «hva skrev trenerne», mens ellever
svarer på «hvem kan spille der».

**Hvem som har vurdert.** Antallet under «Rated by» på tavla kan åpnes. Da vises hver trener med
sin egen overall og datoen de vurderte, og en lenke til spillersiden med alle trenerne side om
side. For en grå rad er det trenerne fra den tidligere syklusen tallene kommer fra. Trenerne
vises med delen av e-postadressen før @, og den innloggede treneren som «You».

**Spillercellen og bredden.** Første celle i hver rad på tavla er spilleren: klubbens bilde i en
sirkel, navnet, og laget under navnet (`_SuccessionPlayerCell.cshtml`). Det er ingen egen
lagkolonne. En spiller uten bilde får forbokstavene i sirkelen. Bildene er de samme som på banen
og hentes fra `/Succession/Photo/{id}`, ett om gangen etter hvert som radene kommer til syne
(`loading="lazy"`). Readiness-tabellen har elleve kolonner, og fra 1260 px vindusbredde går den
ut forbi sidens 1180 px, opptil 1600 px (`.sc-table-wrap--wide`). Ingenting tvinges til én linje:
tabellen fordeler bredden selv, så kategorier, delte posisjoner og «Off and weeks to ready» står
på én linje fra omtrent 1640 px, og brytes gradvis i smalere vinduer i stedet for å rulle
sidelengs. De tre andre tabellene er like brede som siden.

**Uenighet.** To trenere som står `disagreementAt` (3) poeng eller mer fra hverandre på én
vurdering, eller er uenige om kategorien. Slike spillere vises i fanen «Where coaches disagree».

**Off og uker til klar.** Off er `readyAt − overall`. Ukene regnes med minste kvadraters metode:
en rett linje gjennom alle syklusene spilleren er vurdert i, forlenget til `readyAt`, og rundet
opp til hele uker. Spesialtilfellene:

| Situasjon | Siden sier |
| --- | --- |
| Overall ≥ 8 | Ready now |
| Bare én syklus | needs a second cycle for a trend |
| Linja er flat eller går ned | not closing the gap |
| Mer enn `horizonWeeks` (156) uker | years away at this rate |

Det er et anslag ut fra farten så langt, ikke et løfte, og siden sier det.

### Beste ellever

1. En spiller kan stå i en posisjon hvis en trener har ført den opp blant sine tre. Den teller
   med beste rangering noen trener ga. En posisjon kan også stå inne for andre: i JSON-fila har CF
   `"alsoPlays": ["LST", "RST"]`, så en spiller trenerne har som CF, kan velges som venstre eller
   høyre spiss med samme rangering (`SuccessionMath.PlayablePositions`). Det går bare den ene
   veien. En venstre spiss blir ikke CF i 4-3-3. Tavla viser fortsatt bare det trenerne skrev.
2. Fit for posisjonen er overall minus `positionRankPenalty`: 0 for 1. posisjon, 0,5 for 2. og 1,0
   for 3. En naturlig høyreback på 7,0 går foran en midtstopper på 7,2 som bare dekker opp der.
3. Alle par (spiller, posisjon) sorteres etter fit. Det sterkeste paret plasseres først. En
   spiller brukes bare én gang, og en posisjon ingen er ført opp på, står tom og sier det.
4. «Next in line» er de to neste for posisjonen, også hvis de starter et annet sted. Det svarer
   på hvem som tar over etter startspilleren.

Ved likhet avgjør overall, så rangering og til slutt navnet. Samme vurderinger gir derfor
alltid samme lag.

Formasjonene ligger i JSON-fila som rader, fra angrep til keeper. Vi har lagt inn 3-5-2 (standard,
fordi trenerne ba om 1-3-5-2), 4-3-3 og 4-2-3-1. Den første i fila er den siden åpner på. Til sammen bruker de alle 19 posisjonene. Filtrene «Choose from» (lag) og
«Rated as» (bare vurderinger mot et gitt nivå, f.eks. 1st team) gjelder både banen og tabellen.

Noen trenere teller keeperen med: 1-3-5-2 er 3-5-2. `FindFormation` godtar begge (bare som
reserve, så en formasjon som selv heter «1-…» i fila finnes på egen nøkkel først), og siden viser
begge skrivemåtene når navnet er tall (`FormationDefinition.GoalkeeperNotation`).

### Et lag i en formasjon

«Choose from» er en rad med lenker, én per lag (G14, G15, G17, G19), over filtrene: ett klikk på G15
gir G15s beste ellever i formasjonen som er valgt. Lenkene beholder formasjon, syklus og «Rated as». Hvert lag under
My teams (`/Coach`) har også en knapp «Best eleven». Menyen «Squad board / Best eleven» tar med
laget og en eldre syklus, så man blir i samme lag når man bytter side.

### Banen som et spill

Banen er tegnet som i et fotballspill, etter trenernes egen skisse: vår halvdel og den første
tredjedelen av motstanderens, i målestokk (`wwwroot/img/pitch.svg`, en vektortegning som er skarp i
alle størrelser), med 16-meter, 5-meter, D, midtsirkel, hjørnebuer og mål. Hver spiller er en rund
markør i klubbens gule farge med posisjonen på. Under markøren står fornavnet, og ved siden av én
rating.

**Hver spiller står der posisjonen står på banen**, ikke i en rad: en venstre vingback står bredt
og høyt, en midtstopper foran eget mål. Plassene ligger i `startcompass.css` som `.sc-spot--LWB`
osv., én per posisjon, regnet fra tegningens meter og stilt inn etter skissen. De gjelder i alle
formasjoner, så 4-3-3 og 4-2-3-1 tegnes riktig uten mer. Klasser og ikke koordinater i markupen,
fordi CSP-en ikke tillater `style=""`. En ny posisjon i JSON-fila trenger en plass i CSS-en,
ellers har drakten ingen plass å stå.

**3-5-2 er tegnet som i skissen:** to spisser, vingbackene i høyde med en 10-er (ACM), to 8-ere
(L8, R8) bak, tre midtstoppere og keeper. C6 er dermed ikke med i 3-5-2; den er med i 4-3-3.
Til høyre står innbytterne, «Substitutes»: alle vurderte spillere som ikke er i ellever, sterkest
først, med beste posisjon, navn og rating. `wwwroot/js/lineup.js` gjør det mulig å bytte, slik som
i Football Manager:

- **Dra** en innbytter inn på en spiller for å bytte dem, dra en spiller til en annen posisjon for å
  bytte de to, eller dra en spiller til innbytterne for å ta hen av.
- **Trykk** på en spiller og så dit hen skal. Det er slik det virker på nettbrett, og med
  tastaturet (Enter eller mellomrom, Escape for å avbryte).
- Mens en spiller er plukket opp, får posisjonene trenerne har ført opp for hen en hvit ring, med
  1st/2nd/3rd i hjørnet. Plukker man opp en posisjon, står innbytterne som er ført opp for den,
  øverst i lista, med hvor gode de er der.
- En spiller kan stå hvor som helst. Står hen der ingen trener har ført hen opp, får markøren et
  rødt «!» og navnet rød bakgrunn.

**Ratingen er spillerens beredskap i posisjonen hen står i**, ikke overall alene:
`SuccessionMath.PositionFit`. Det er overall for en 1. posisjon, minus `positionRankPenalty` for
2. og 3. (0,5 og 1,0), og minus `outOfPositionPenalty` (2,0) for en posisjon ingen trener har ført
opp. Det siste står i JSON-fila og må være minst like stort som trekket for 3. posisjon. Utvalget
bruker aldri det trekket; det gjelder bare det treneren selv flytter.

**Team rating** øverst er snittet av spillerne på banen, med en stolpe for angrep, midtbane, forsvar
og keeper. Hvilken rad som er hva, følger av at formasjonene i fila tegnes fra angrep til keeper:
første rad er angrep, siste er keeper, nest siste er forsvar, resten er midtbane
(`SuccessionMath.UnitOf`). Alt regnes ut på nytt for hvert bytte, og det som endrer seg, blinker.
Ved siden av står hvor mye laget er over eller under beste ellever («−0.3 on the best eleven»),
hvor mange som er klare (overall 8 eller mer), og hvor mange som står utenfor posisjon.

**Navn.** Markøren viser spillerens fornavn, slik admin har lagt det inn til velkomsten
(`PlayerPersonalDetails`, via `IPlayerWelcomeService.FirstNamesAsync`). Har ikke klubben lagt inn
noe, står hele navnet. Har to spillere på siden samme fornavn, står hele navnet i liten skrift
under. Resten av appen viser hele navnet. Bildet vises ikke. Se
`docs/player-welcome.md`.

**Draktnummer** finnes ikke i databasen. Det krever en ny kolonne og en migrasjon, og det er
Kristian som lager migrasjoner. Når nummeret finnes, kan det stå på markøren i stedet for posisjonen.

**Ingenting lagres.** Utvalget er trenernes vurderinger og skal være det samme for alle. Laget man
setter opp, står i adressen (`?lineup=12.5.0.7…`, én spiller-id per posisjon i sidens rekkefølge, 0
for tom), så lenken åpner det igjen og kan sendes til en annen trener. En id som ikke er på siden,
gjør at hele verdien ignoreres. «Back to the best eleven» fjerner den. Tabellen «Who is next in
line» viser alltid utvalget, ikke det man har flyttet.

Å lagre egne oppstillinger i databasen krever en migrasjon, og den er ikke laget (se husreglene).

Skriptet regner ikke ut noe selv utover `PositionFit` og snittene av den. Alt annet det viser,
kommer ferdig fra serveren i en datablokk (`<script type="application/json" id="lineup-data">`,
`SuccessionFormationViewModel.Editor`). Den kjøres aldri, så CSP-en trenger ingen unntak, og
JSON-koderen gjør `<` og `>` om til `\u003C`/`\u003E`, så ingenting i den kan avslutte blokka.
Uten JavaScript er siden banen og lista slik serveren tegner dem.

---

## Personvern

Dette er en ny kategori opplysninger om spillerne, de fleste mindreårige: trenernes vurdering av
evner og modenhet, kontraktsforhold, og fritekst. Det følger de samme reglene som resten av
systemet:

- **Ingen navn fra arket i repoet.** Excel-arkene trenerne leverte har fullt navn på alle spillerne.
  De ligger ikke i repoet og skal ikke dit. Importen leser dem fra en git-ignorert mappe, se under.
- **Friteksten fra arkene er med, etter klubbens ønske.** Den har skader, vekst, hjemmeforhold og
  navn på andre spillere -- opplysninger skjemaet i appen ellers ber trenerne holde utenfor. Den
  vises bare for stab, på spillersiden under «In their words», er med i innsynet
  (`/Admin/Export/{id}`) og slettes med spilleren. Den står aldri i loggen eller i
  importrapporten.
- **Bare stab.** `[Authorize(Roles = Coach,Admin)]` på hele controlleren, og `CanViewPlayer` per
  spiller.
- **Revisjonslogg.** Spillersiden og skjemaet skriver én rad hver (`Succession/Player`,
  `Succession/Rate`). Tavla og banen skriver én rad per spiller som vises med tall
  (`Succession/Overview`, `Succession/Formation`), med én lagring for hele siden
  (`IPlayerAccessLog.RecordManyAsync`). På tavla gjelder det også en spiller som bare vises med
  bilde, uten tall eller kontrakt. På banen er det alle vurderte spillere, også de på
  benken, fordi lista viser tall for dem.
- **Innsyn og sletting.** `/Admin/Export/{id}` tar med `SuccessionAssessments` med vurderinger og
  fritekst, og `SuccessionProfile`. Trenerne står som «Coach 1», «Coach 2». Sletting av spilleren
  tar alt med seg (cascade), og slettesiden viser hvor mange vurderinger som forsvinner.
- **Fritekst.** Lengdegrenser (200/500/1000 tegn), og skjemaet ber trenerne holde seg til
  fotball: ingen helseopplysninger og ingen navn på andre.
- **Må avklares:** `docs/sikt-melding.md` er oppdatert med de nye opplysningene. Klubben må
  likevel ta stilling til dette før ekte data legges inn: trenernes vurderinger er ikke noe
  spilleren har svart på selv, og de vises ikke for spilleren.

---

## Ingen demodata

Succession planning viser bare trenernes egne ark (se under). De oppdiktede vurderingene og
kontraktene appen hadde før arkene kom, og de to demotrenerne `trener.akademi` og
`trener.utvikling`, fjernes ved hver oppstart i Development
(`SeedSuccessionImport.RemoveMadeUpAsync`), så en delt base som ble seedet med dem, blir ryddet
neste gang noen starter appen. Grensen er det demokontoene skrev før 10.10.2026; det
`trener.senior` vurderer i appen etter det, blir stående.

---

## Import av trenernes ark

Trenerne fylte ut hvert sitt ark i september 2026. Importen gjør dem om til vurderinger i appen, én
trener per ark, koblet til spillerne fra de ekte troppene (`docs/player-welcome.md`). Bare i
Development, og ingenting av det står i repoet.

```bash
python3 scripts/squads/fetch_squads.py                       # troppene, som radene matches mot
python3 scripts/succession/import_workbooks.py <mappe med arkene> --table
dotnet run --project StartPraksisGruppe3Prosjekt              # leser inn ved oppstart
```

**Skriptet** (`scripts/succession/import_workbooks.py`) leser arkene uten noe annet enn Python, og
skriver `Data/Succession/Import/assessments.json` og `report.md`. Mappa er git-ignorert.

- **Én trener per ark.** Treneren leses av filnavnet, fordi trenerkolonnen er tom i halvparten av
  arkene: `IK_Start_Succession_Planning_AB.xlsx` er «ab». Hver får kontoen
  `trener.ab@ikstart.example`, uten passord og låst, til klubben bestemmer hvem som skal ha den.
- **En rad er trenerens egen** når den har et tall, noe i en kolonne malen lot stå tom (trenerens
  navn, readiness, risiko …), eller en listeverdi som er endret fra malen. Den blir en vurdering,
  med eller uten de seks tallene. Malradene ingen har rørt, er ingens mening og blir ikke med. Et
  ark uten egne rader gir ingen konto.
- **Alt treneren skrev:** de seks vurderingene (0–10), «Rated as», kategori, tre posisjoner,
  personal readiness (0–10 i halve, som 5,5), risiko når den er Green/Amber/Red, «Pathway blocked?»
  og «External needed?» når de begynner med ja eller nei, kontrakt og treningsgruppe, og
  friteksten: notes, de tre prognosene, What now, Key development focus og Super strengths.
- **Ord i tall-, liste- og ja/nei-kolonnene** («Yes, by players in the first team», et navn, en
  grunn i stedet for en farge) lagres i et eget felt ved siden av verdien:
  `PersonalReadinessNote`, `PathwayBlockedNote`, `SuccessionRiskNote` og `ExternalNeededNote`.
  Ja-et eller nei-et først i teksten leses fortsatt. Et tall utenfor skalaen utelates i stedet for
  å rundes av, og står i rapporten. Tekst gjentas aldri i rapporten.
- **Syklusen** er den arket sist ble lagret i. `--rated-on` overstyrer.
- **Kontrakt og treningsgruppe** er fakta, lagret én gang. Der en trener har rettet malens verdi
  (en kontrakt som er Youth, ikke Non), vinner rettingen. Har to trenere rettet ulikt, står malens
  verdi, og rapporten sier fra.

**Matchingen** er mot navnene i `Data/Squads/squads.json`, uten store og små bokstaver, aksenter,
bindestrek og rekkefølge, og med ae/o/aa for æ/ø/å.

| Treff | Importeres når |
| --- | --- |
| Samme ord | alltid, med mindre klubbens fødselsår sier noe annet enn arket |
| Det ene navnet mangler et mellomnavn | klubbens side har samme fødselsår som arket |
| En bokstav eller to fra hverandre | klubbens side har samme fødselsår som arket |

To kandidater for én rad, eller to rader for én spiller, gir ingen import. Resten står i
`report.md` med grunn og kandidater, og avgjøres for hånd i `Data/Succession/Import/decisions.json`
(«Etternavn, Fornavn» → klubbens navn, eller `null` for å holde raden ute). Se README-en i mappa.

**Spillere arkene har, men troppsidene ikke** (utlånt, rykket opp til A-laget, ikke lagt ut ennå)
legges i `Data/Squads/extra-players.json` med laget appen skal ha dem på. `fetch_squads.py` henter
navn, fødselsdato, posisjon og bilde fra profilsiden deres på ikstart.no der den finnes, og ellers
tas navnet og fødselsåret fra arkene. Da står de i troppene og matches som alle andre. Se
`docs/player-welcome.md`.

**Appen** (`Data/SeedSuccessionImport.cs`) leser fila ved oppstart, etter troppene.

- **Idempotent** per (spiller, trener, syklus), samme nøkkel som den unike indeksen. Samme fil to
  ganger endrer ingenting.
- **Fila er fasiten for kontoene den lager.** En vurdering som er endret i fila, rettes. En som er
  tatt ut, slettes, men bare i syklusen fila gjelder, så neste syklus ikke visker ut historikken.
- **Kontrakt og treningsgruppe** legges inn der de mangler. Det admin har lagt inn, røres ikke.
- **Loggen sier hvor mange, aldri hvem.** En fil som ikke stemmer med listene i
  `succession-planning.json`, stopper oppstarten med feilen nummerert, ikke navngitt.

---

## Ikke bygget, og spørsmål til trenerne

- **«Rated as».** Vi har tolket det som nivået spilleren vurderes mot, og derfor kan man filtrere
  beste ellever på det. Stemmer ikke det, er det bare filteret som endres.
- **«External Needed?»** står per spiller i arket. Handler det egentlig om posisjonen, altså om
  klubben må hente noen utenfra? På banen vises posisjoner uten etterfølger uansett.
- **Projections** er fritekst. Hvis trenerne alltid skriver et nivå, som «U19s» eller «1st team»,
  kan feltene bli lister, og da kan appen tegne en tidslinje.
- **Tersklene** (8 for klar, 3 poeng for uenighet, trekket for 2. og 3. posisjon) ligger i
  JSON-fila og kan endres uten kodeendring.

---

## Filer

| Hva | Hvor |
| --- | --- |
| Lister, terskler, formasjoner | `Data/Succession/succession-planning.json` |
| Modell | `Models/SuccessionAssessment.cs` (også `SuccessionRating`, `PlayerSuccessionProfile`), `Models/Succession/` |
| Utregninger | `Services/Succession/SuccessionMath.cs` |
| Database og oppslag | `Services/Succession/SuccessionPlanningService.cs` |
| Fila, lest og validert ved oppstart | `Services/Succession/SuccessionCatalog.cs` |
| Controller | `Controllers/SuccessionController.cs` |
| Views | `Views/Succession/` |
| Flytte spillere på banen | `wwwroot/js/lineup.js`, stilene «Moving players about» i `startcompass.css` |
| Migrasjon | `Data/Migrations/*_AddSuccessionPlanning.cs` |
| Import av trenernes ark | `scripts/succession/import_workbooks.py`, `Data/SeedSuccessionImport.cs`, `Data/Succession/Import/` (git-ignorert) |
