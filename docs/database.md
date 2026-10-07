# Databasen

StartCompass har to slags databaser, og de skal aldri blandes:

| | Utvikling | Drift (hoveddatabasen) |
| --- | --- | --- |
| Hvor | PostgreSQL 17 på din egen maskin | PostgreSQL 17 på en server IK Start eier |
| Data | Bare oppdiktede | De ekte spillerne |
| Hvem har den | Én per utvikler. Ingen deles | Én. Få har tilgang |
| Skjemaet | Appen migrerer selv ved oppstart | Databaseeieren kjører et migrasjonsskript |
| Appen kobler til som | Eieren av databasen | En egen rolle som bare kan lese og skrive rader |
| Kontoer | Demokontoer med et passord som står i kildekoden | Første administrator med `create-admin` |

Appen er ikke avhengig av Supabase. Den delte Supabase-databasen gruppa utviklet mot, brukes
ikke lenger, og slås av når spillerne er flyttet. Se
[«Flytte de ekte spillerne og slå av Supabase»](#flytte-de-ekte-spillerne-og-slå-av-supabase).

Innhold:

- [Utvikling: din egen lokale database](#utvikling-din-egen-lokale-database)
- [Vernet mellom utvikling og drift](#vernet-mellom-utvikling-og-drift)
- [Opprette hoveddatabasen](#opprette-hoveddatabasen)
- [Kjøre migrasjonene](#kjøre-migrasjonene)
- [Flytte de ekte spillerne og slå av Supabase](#flytte-de-ekte-spillerne-og-slå-av-supabase)
- [Bruke hoveddatabasen](#bruke-hoveddatabasen)
- [Oppslag: innstillinger, kommandoer og tabeller](#oppslag-innstillinger-kommandoer-og-tabeller)
- [Må oppdateres i Sikt-meldingen](#må-oppdateres-i-sikt-meldingen)

---

## Utvikling: din egen lokale database

Tre steg, første gang.

**Steg 1: start PostgreSQL.** Med Docker, fra rotmappa i repoet:

```bash
copy .env.example .env
```

(`cp .env.example .env` på macOS og Linux.) Åpne `.env` og sett et passord etter
`POSTGRES_PASSWORD=`. Velg ditt eget: det gjelder bare databasen på din maskin. Fila er
git-ignorert.

```bash
docker compose up -d
```

Det starter PostgreSQL 17 med databasen `startcompass_dev` og brukeren `startcompass`, bare
tilgjengelig fra din egen maskin.

**Steg 2: gi appen passordet.** Det samme passordet som i `.env`:

```bash
dotnet user-secrets set "Database:Password" "PASSORDET_DITT" --project StartPraksisGruppe3Prosjekt
```

Resten av tilkoblingsstrengen står i `appsettings.Development.json`, uten passord. Har du den
gamle Supabase-strengen i user-secrets fra før, må den bort. Den overstyrer ellers fila, og
appen prøver å nå Supabase:

```bash
dotnet user-secrets remove "ConnectionStrings:DefaultConnection" --project StartPraksisGruppe3Prosjekt
```

**Steg 3: kjør.**

```bash
dotnet run --project StartPraksisGruppe3Prosjekt
```

Første gang lager appen tabellene, markerer databasen som en utviklingsbase, og legger inn
roller, lag og oppdiktede data: demokontoene (se README, «Demokontoer»), tropper, perioder,
5C-svar og succession-vurderinger. Senere oppstarter legger bare til det som mangler.

### Begynne på nytt

Slett den lokale databasen og start igjen. Ingen andre merker det.

```bash
docker compose down -v
```

```bash
docker compose up -d
```

`-v` sletter volumet med dataene. Neste `dotnet run` bygger alt opp igjen.

### Uten Docker, på Windows

1. Installer PostgreSQL 17 med installasjonsprogrammet fra
   [postgresql.org/download/windows](https://www.postgresql.org/download/windows/), eller med
   `winget install PostgreSQL.PostgreSQL.17`. Installasjonen spør om et passord for
   superbrukeren `postgres`, og legger PostgreSQL inn som en Windows-tjeneste på port 5432.
2. Lag brukeren og databasen appen forventer. Åpne «SQL Shell (psql)» fra startmenyen, logg
   inn som `postgres`, og skriv:

   ```sql
   create role startcompass login;
   \password startcompass
   create database startcompass_dev owner startcompass;
   ```

   `\password` spør etter passordet to ganger. Det havner ikke i noen historikk.
3. Legg passordet i user-secrets (steg 2 over) og kjør appen (steg 3).

Begynne på nytt uten Docker, i psql som `postgres`:

```sql
drop database startcompass_dev;
create database startcompass_dev owner startcompass;
```

### Porten er opptatt

Står det allerede en PostgreSQL på port 5432, får ikke containeren porten. Sett en annen i
`.env`, for eksempel `POSTGRES_PORT=5433`, og gi appen hele strengen (fortsatt uten passord;
`Database:Password` gjelder som før):

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5433;Database=startcompass_dev;Username=startcompass" --project StartPraksisGruppe3Prosjekt
```

### Hvis appen ikke starter

Meldingen står sist i loggen og sier hva som mangler.

| Melding | Hva som er galt |
| --- | --- |
| «Databasepassordet mangler» | `Database:Password` er ikke satt i user-secrets |
| «Kom ikke gjennom oppstarten mot PostgreSQL» | Containeren kjører ikke (`docker compose up -d`), feil passord eller feil port |
| «Databasen har N tabeller, men ingen markering» | Strengen peker på noe annet enn en database laget av denne versjonen, typisk den gamle Supabase-strengen i user-secrets. Fjern den |
| «Denne databasen er markert som DRIFT» | Strengen peker på hoveddatabasen. Utvikling skal aldri dit |

### De ekte troppene i utvikling

`Data/Squads/` (git-ignorert) kan holde en `squads.json` med bilder. Finnes den, legger
seedingen spillerne derfra inn i stedet for de oppdiktede, med fornavn og bilde og ikke noe
mer. Det lages aldri oppdiktede kontoer, foresatte, samtykker, svar eller vurderinger for en
ekte spiller. Se [`docs/player-welcome.md`](player-welcome.md). Mappa følger aldri med i git,
og heller ikke i `dotnet publish`.

---

## Vernet mellom utvikling og drift

Development migrerer ved oppstart og legger inn demokontoer med et passord som står i
kildekoden. Det skal aldri skje i databasen med de ekte spillerne. Og appen i drift skal aldri
kjøre mot en utviklingsbase, der de kontoene allerede ligger.

Databasen sier derfor selv hva den er. Tabellen `database_marker` har én rad, `Development`
eller `Production`:

- En **tom** database markeres av den første appen som starter mot den, med miljøet appen
  kjører i. «Development» er miljøet Development. Alle andre miljøer er drift.
- En **markert** database slipper bare inn sitt eget miljø. Development starter ikke mot en
  database markert som drift, og Production ikke mot en markert som utvikling.
- En **umarkert database som har tabeller eller data**, slipper ikke inn noen. Det er enten
  en database fra før ordningen (den gamle Supabase-databasen), eller noen har fjernet
  markeringen. Da må et menneske si hva den er.

Sjekken skjer **før** noe migreres eller skrives, så en utviklingsmaskin som peker feil,
stoppes før den har endret noe. Koden er `Data/DatabaseGuard.cs`.

En liste over tillatte verter hadde ikke vært nok. Appen og databasen kan stå på samme server,
og da er verten `localhost` i begge miljøer.

Tre ting til hører med:

- `create-admin` og `import-players` kjører bare mot en database som er markert for miljøet de
  kjøres i. Seedingen av demodata sjekker markeringen en gang til selv.
- I hoveddatabasen kan ikke app-rollen endre eller slette markeringen
  (`scripts/database/02-grants.sql`).
- `dotnet ef database update` går utenom appen, og dermed utenom vernet. Den skal bare brukes
  mot din egen lokale database. Mot hoveddatabasen stopper den uansett på at app-rollen ikke
  får endre skjemaet.

Er en database umarkert, og du er sikker på hva den er, markerer databaseeieren den for hånd:

```sql
insert into database_marker (id, environment, marked_at) values (1, 'Production', now());
```

---

## Opprette hoveddatabasen

Oppskriften for databasen på Start sin server. Stegene er i rekkefølge. Steg 2 til 8 er prøvd
mot en lokal PostgreSQL 17 med oppdiktede data. Steg 1, med brannmur og sertifikat, kan bare
prøves på den ekte serveren.

### 1. Serveren

- **PostgreSQL 17 på Linux.**
- **Brannmuren slipper bare inn appserveren** på databaseporten. Ingen andre adresser, og ikke
  åpent mot internett.
- **TLS** med et sertifikat som virker med `SSL Mode=VerifyFull`: signert av en CA appserveren
  stoler på, og utstedt til vertsnavnet appen kobler til med. Sett `ssl = on` i
  `postgresql.conf`, og bruk `hostssl` med `scram-sha-256` i `pg_hba.conf`, ikke `host`.
  Unntaket er når appen og databasen står på **samme maskin**. Da går tilkoblingen over
  `localhost` og trenger ikke TLS.
- **Backup** fra første dag. Se [«Bruke hoveddatabasen»](#bruke-hoveddatabasen).

### 2. Rollene og en tom database

På databaseserveren, som superbruker:

```bash
sudo -u postgres psql -v ON_ERROR_STOP=1 -f scripts/database/01-roles-and-database.sql
```

Skriptet lager to roller og en tom database, `startcompass`:

| Rolle | Brukes av | Kan |
| --- | --- | --- |
| `startcompass_owner` | Databaseeieren, til migrasjoner | Eier databasen og tabellene |
| `startcompass_app` | Appen | Lese og skrive rader. Ikke endre skjemaet |

Skriptet setter **ingen passord**. Sett dem etterpå, i psql:

```sql
\password startcompass_owner
\password startcompass_app
```

Lag to lange, forskjellige passord, og oppbevar dem i klubbens passordhvelv. Passordet til
`startcompass_owner` skal ikke ligge på appserveren.

### 3. Tabellene

Følg [«Kjøre migrasjonene»](#kjøre-migrasjonene) under. Etterpå har databasen alle tabellene,
og ingen rader utenom listen over kjørte migrasjoner.

### 4. Innstillingene for drift

På appserveren, som miljøvariabler for tjenesten appen kjører som. Ingen av dem står i en fil
i repoet.

| Miljøvariabel | Verdi |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__DefaultConnection` | `Host=<databaseserver>;Port=5432;Database=startcompass;Username=startcompass_app;Password=<passordet>;SSL Mode=VerifyFull` |
| `AllowedHosts` | Vertsnavnet appen svarer på, f.eks. `startcompass.ikstart.no` |
| `ForwardedHeaders__KnownProxies__0` | IP-adressen til reverse proxyen foran appen |
| `DataProtection__KeysPath` | En mappe bare appens bruker kan lese, f.eks. `/var/lib/startcompass/keys` |

Om tilkoblingsstrengen:

- **`SSL Mode=VerifyFull` er påkrevd** når databasen ikke står på samme maskin som appen. Uten
  den starter ikke appen i drift. `Require` krypterer, men kontrollerer ikke hvem som er i den
  andre enden.
- **`Root Certificate=<sti til CA-fila>`** trengs bare når serversertifikatet er signert av en
  CA som ikke ligger i appserverens rotlager, for eksempel klubbens egen. Stien må være full
  og bokstavelig. Npgsql utvider ikke miljøvariabler.
- Står databasen på samme maskin, er `Host=localhost` nok, uten `SSL Mode`.

Om nøkkelmappa: nøklene der krypterer innloggingscookien. Uten en fast mappe logges alle ut
hver gang appen starter på nytt, og derfor er den påkrevd utenfor Development. Mappa skal
være med i backupen. Mistes den, må alle logge inn på nytt, og ikke noe annet går tapt.

Appen krever https i drift. Cookiene sendes ikke over http.

### 5. Start appen

Første gang appen starter mot den tomme databasen, markerer den databasen som drift og legger
inn rollene (Player, Coach, Guardian, Admin) og lagene (G14, G15, G17, G19). Ikke noe annet:
ingen kontoer, ingen spillere og ingen perioder. Loggen sier:

```
Databasen var tom og er nå markert som drift.
Grunnoppsett: 4 roller og 4 lag opprettet.
```

Mangler en migrasjon, eller er en innstilling feil, starter ikke appen, og den siste meldingen
i loggen sier hvorfor.

### 6. Første administrator

Selvregistrering er stengt, og appen sender ikke e-post. Den første kontoen lages derfor fra
kommandolinja, på appserveren, med de samme miljøvariablene som tjenesten:

```bash
dotnet StartPraksisGruppe3Prosjekt.dll create-admin --email navn@ikstart.no
```

Kommandoen spør etter passordet to ganger, uten å vise det. Passordet leses fra standard input
og aldri fra argumentlista, så det havner ikke i shell-historikken. Det må følge appens
passordregler: minst 12 tegn, med stor og liten bokstav, tall og spesialtegn.

Kommandoen lager **bare den første** administratoren. Finnes det en fra før, nekter den.
Loggen sier at kontoen ble opprettet, med bruker-ID, og aldri passordet.

### 7. Spillerne

Kopier mappa fra eksporten (se neste hovedavsnitt) til appserveren over en kryptert kanal
(`scp`), og kjør:

```bash
dotnet StartPraksisGruppe3Prosjekt.dll import-players --from /sti/til/eksporten
```

Loggen sier hvor mange spillere og bilder som ble lagt inn, og hvor mange databasen har
etterpå. **Sjekk at antall spillere og antall bilder er det samme som eksporten sa.**
Kommandoen kan kjøres flere ganger. Den legger til og oppdaterer, og sletter aldri.

### 8. Sjekk i appen

Logg inn som administratoren, se at spillerne står med bilde på `/Admin/Players`, og lag den
første perioden på `/Admin/Periods`. Uten en periode har ingen noe skjema å svare på.

Opprett så kontoene på `/Admin/Users`: trenerne først, deretter foresatte og spillere når
klubben har e-postadressene. Hver konto får et midlertidig passord som vises én gang og må
byttes ved første innlogging. Se [`docs/user-administration.md`](user-administration.md).

Slett til slutt eksportmappa fra appserveren.

### Til Start har serveren klar

Det samme kan kjøres på en annen PostgreSQL 17 i EU/EØS, så lenge den oppfyller kravene over:

- en **databehandleravtale** med den som drifter den,
- **TLS** som virker med `VerifyFull`,
- **backup** med en gjenoppretting som er prøvd.

Når Start sin server er klar, flyttes databasen dit. Se
[«Flytte til en annen server»](#flytte-til-en-annen-server).

---

## Kjøre migrasjonene

Den faste prosedyren, første gang og hver gang en ny versjon av appen har en ny migrasjon.
Appen migrerer **ikke** selv i drift. Den kobler til med en rolle som ikke får endre skjemaet,
og starter ikke hvis en migrasjon mangler.

Databaseeieren gjør dette, fra en maskin med repoet og .NET 8 SDK:

**1. Sjekk ut nøyaktig den versjonen av appen som skal ut.**

```bash
git checkout <commit eller tag>
```

**2. Lag skriptet.** Trenger verken database eller passord:

```bash
dotnet ef migrations script --idempotent --project StartPraksisGruppe3Prosjekt --output migrate.sql
```

(`dotnet-ef` installeres med `dotnet tool install --global dotnet-ef --version 8.0.11`.)
Skriptet er idempotent: det ser i `__EFMigrationsHistory` hva som er kjørt, og kjører bare
resten. Les gjennom det før det kjøres, særlig når det endrer eller fjerner en kolonne.

**3. Ta en backup**, hvis databasen har data.

**4. Stopp appen**, hvis migrasjonen endrer eller fjerner noe den gamle versjonen bruker.

**5. Kjør skriptet som eieren:**

```bash
psql -h <databaseserver> -U startcompass_owner -d startcompass -v ON_ERROR_STOP=1 -f migrate.sql
```

**6. Gi app-rollen rettighetene**, også som eieren. Skriptet kan kjøres så mange ganger man vil,
og må kjøres etter hver migrasjon, fordi en ny tabell ellers ikke kan leses av appen:

```bash
psql -h <databaseserver> -U startcompass_owner -d startcompass -v ON_ERROR_STOP=1 -f scripts/database/02-grants.sql
```

**7. Start den nye versjonen av appen.**

`migrate.sql` er laget for én versjon og skal ikke inn i git.

Hva app-rollen får, står i `scripts/database/02-grants.sql`: lese og skrive rader i alle
tabellene, med noen unntak der den får mindre. Den kan ikke endre eller slette markeringen i
`database_marker`, ikke skrive i `__EFMigrationsHistory`, og ikke endre eller slette rader i
de fire loggene som bare skal vokse (`consent_events`, `player_access_events`,
`feedback_releases` og `player_deletion_events`). Når en spiller slettes, går loggradene med
gjennom fremmednøklene, som virker med eierens rettigheter.

---

## Flytte de ekte spillerne og slå av Supabase

De ekte spillerne med fornavn og bilder skal beholdes, og flyttes fra den gamle
Supabase-databasen til hoveddatabasen. **Rekkefølgen er viktig:** eksporten må kjøres mot den
gamle databasen med den gamle strukturen, altså med versjonen av appen fra **før** den nye
databasen kom inn.

| Når | Hva |
| --- | --- |
| Etter PR 1 (`export-players` og `import-players`) | Kjør eksporten mot Supabase. Steg 1 under |
| Etter PR 2 (den nye databasen) | Opprett hoveddatabasen og kjør importen |

Versjonen etter PR 2 kan ikke lese den gamle databasen: tabellene har fått nye navn. Er PR 2
allerede flettet inn, sjekk ut committen fra PR 1 for å kjøre eksporten.

### 1. Eksporter fra Supabase

Med versjonen fra PR 1, og den gamle Supabase-strengen i user-secrets som før:

```bash
dotnet run --project StartPraksisGruppe3Prosjekt -- export-players --out D:\spillerflytting\eksport
```

Kommandoen leser bare. Transaksjonen er satt til `READ ONLY`, så databasen selv nekter alt
annet. Den kjører før migrering og seeding, og starter ikke webserveren. Den skriver en
`squads.json` og bildene i `photos/`, og loggen sier hvor mange spillere og bilder. **Noter
de to tallene.** De oppdiktede spillerne er ikke med.

Sier loggen at spillere «med navn fra de oppdiktede troppene har et bilde», sjekk dem på
`/Admin/Players` før Supabase slås av. Det kan være en ekte spiller som deler navn med en
oppdiktet, og da er hen ikke med i fila.

Lagene het U14, U15, U17 og U19 til 07.10.2026. Står de fortsatt slik i den gamle databasen,
skriver eksporten dem med dagens navn (G14, G15, G17 og G19), og loggen sier fra.

### 2. Oppbevar fila trygt

Mappa inneholder navn, fødselsdato og bilde av mindreårige. Den skal ligge på en kryptert
disk, hos én person, til importen er bekreftet. **Aldri i git, chat eller e-post**, og ikke i
en synkronisert skymappe. Kommandoene nekter å skrive til eller lese fra andre mapper i
repoet enn `Data/Squads/`, som er git-ignorert.

### 3. Sjekk at ingenting ekte blir igjen

Det som flyttes, er spilleren (navn, lag, posisjon, fødselsdato) og velkomsten (fornavn, bilde,
bildekilde). Kontoer, foresatte, samtykker, 5C-svar og succession-vurderinger flyttes
**ikke**. I den gamle databasen er alt det laget av seedingen, også for de ekte spillerne.

Sjekk med hele gruppa og med trenerne at ingen har lagt inn ekte svar eller vurderinger i den
gamle databasen. Har noen det, må de flyttes for seg før Supabase slås av. Det finnes ingen
kommando for det.

### 4. Slå av Supabase

Når hoveddatabasen er opprettet, spillerne importert (steg 7 over) og tallene stemmer:

1. Slett eksportmappa, alle kopier.
2. Slå av og slett Supabase-prosjektet.
3. Alle på gruppa fjerner den gamle strengen fra user-secrets:

   ```bash
   dotnet user-secrets remove "ConnectionStrings:DefaultConnection" --project StartPraksisGruppe3Prosjekt
   ```

4. Slett CA-fila fra Supabase (`prod-ca-2021.crt`) fra egen maskin. Den trengs ikke lenger.

---

## Bruke hoveddatabasen

**Hvem som har tilgang.** Tre nivåer, og færrest mulig på hvert:

- *Databaseeieren* (`startcompass_owner`): migrasjoner og backup. Få personer.
- *App-rollen* (`startcompass_app`): bare appen.
- *Administratorene i appen*: ser alle spillere, og oppretter og låser kontoer. Oppslag på
  enkeltspillere logges.

Utviklere trenger ikke tilgang til hoveddatabasen for å utvikle, og skal ikke ha den.

**Nye eller endrede spillere** legges inn med `import-players`, til brukeradministrasjonen i
appen finnes. Lag en `squads.json` med de nye spillerne (samme format som eksporten) og kjør
kommandoen. En spiller som finnes fra før (samme navn), får lag, posisjon og fødselsdato fra
fila. Importen sletter aldri. **En spiller fjernes bare med `/Admin/Delete/{id}`**, som også
sletter alt som er lagret om spilleren og logger slettingen.

**Kontoer** opprettes av en administrator på `/Admin/Users`, for trenere, spillere og
foresatte. Importen lager ingen. Spillernes og de foresattes kontoer krever e-postadresser
fra klubben, og en spiller under 19 får ikke konto før en foresatt er registrert. Se
[`docs/user-administration.md`](user-administration.md).

**Ny migrasjon.** Lag skriptet fra samme commit som appversjonen som skal ut, og kjør det før
den nye versjonen startes. Se [«Kjøre migrasjonene»](#kjøre-migrasjonene).

**Backup.** Daglig, med en kopi et annet sted enn databaseserveren, og en gjenoppretting som
faktisk er prøvd. En backup ingen har gjenopprettet fra, er et håp og ikke en backup. Ta med
nøkkelmappa (`DataProtection__KeysPath`). En spiller som slettes i appen, ligger i backupene
til de er rotert ut. Det skal stå i informasjonen til de registrerte, og rotasjonstiden må
bestemmes.

### Flytte til en annen server

1. Stopp appen, så ingenting skrives underveis.
2. Ta en dump som eieren:

   ```bash
   pg_dump -h <gammel server> -U startcompass_owner -d startcompass -Fc -f startcompass.dump
   ```

3. På den nye serveren: lag rollene og en tom database med
   `scripts/database/01-roles-and-database.sql`, og sett passordene (steg 2 i
   [«Opprette hoveddatabasen»](#opprette-hoveddatabasen)).
4. Les inn dumpen som eieren, og gi app-rollen rettighetene:

   ```bash
   pg_restore -h <ny server> -U startcompass_owner -d startcompass --no-owner --exit-on-error startcompass.dump
   ```

   ```bash
   psql -h <ny server> -U startcompass_owner -d startcompass -v ON_ERROR_STOP=1 -f scripts/database/02-grants.sql
   ```

5. Sjekk antall rader per tabell i begge databasene, og at `__EFMigrationsHistory` har de samme
   radene:

   ```sql
   select table_name,
          (xpath('/row/c/text()', query_to_xml(format('select count(*) as c from %I', table_name), false, true, '')))[1]::text as rows
   from information_schema.tables
   where table_schema = 'public' and table_type = 'BASE TABLE'
   order by table_name;
   ```

6. Bytt `ConnectionStrings__DefaultConnection` på appserveren, og start appen. Markeringen
   følger med i dumpen, så appen kjenner databasen igjen som drift.
7. Slett dumpfila og den gamle databasen når den nye er bekreftet. Dumpen inneholder alt, også
   bildene.

Bruk `pg_dump` og `pg_restore` fra samme hovedversjon som serveren.

### .NET 8

.NET 8 går ut av støtte 10. november 2026 (README, «Stack»). Oppgraderingen til .NET 10 er en
egen PR, og bør være gjort før hoveddatabasen tas i bruk med ekte data.

---

## Oppslag: innstillinger, kommandoer og tabeller

### Innstillinger

| Innstilling | Utvikling | Drift |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | `appsettings.Development.json`, uten passord | Miljøvariabelen `ConnectionStrings__DefaultConnection`, hele strengen |
| `Database:Password` | user-secrets | Brukes ikke. Passordet står i strengen |
| `DataProtection:KeysPath` | Tom | Påkrevd |
| `AllowedHosts` | `*` | Vertsnavnet |
| `ForwardedHeaders:KnownProxies` | Tom | IP-adressen til proxyen |
| `Seed:DevPassword` | Valgfri, overstyrer demopassordet | Brukes ikke |
| `FiveC:Store` | `InMemory` for en demo som ikke skal lagre noe | Skal ikke settes |

`appsettings.json` har ingen tilkoblingsstreng, med vilje.

### Kommandoene

Alle tre kjører før appen migrerer eller seeder, gjør én ting, og avslutter uten å starte
webserveren. Fra kildekoden skrives de `dotnet run --project StartPraksisGruppe3Prosjekt -- <kommando>`.

| Kommando | Hva | Krever |
| --- | --- | --- |
| `create-admin --email <adresse>` | Lager den første administratoren. Passordet fra standard input | En markert database uten administrator |
| `import-players --from <mappe>` | Legger til og oppdaterer spillere fra en `squads.json`. Sletter aldri | En markert database |
| `export-players --out <mappe>` | Skriver de ekte spillerne til en `squads.json` med bilder. Leser bare | Tabellene fra denne versjonen |

Detaljene om eksport og import står i [`docs/player-welcome.md`](player-welcome.md).

### Tabellene

Skjemaet lages av én migrasjon, `InitialCreate`. Tabeller og kolonner har små bokstaver og
understrek, så ingenting må skrives i anførselstegn i SQL. Unntaket er EFs egen
`"__EFMigrationsHistory"`.

| Tabell | Innhold |
| --- | --- |
| `asp_net_users`, `asp_net_roles`, `asp_net_user_roles` og de fire andre `asp_net_*` | Kontoer og roller (ASP.NET Core Identity) |
| `teams`, `players`, `guardianships` | Lag, spillere, og hvem som er foresatt for hvem |
| `player_personal_details` | Fornavn og bilde til velkomsten |
| `survey_rounds` | Måleperiodene |
| `five_c_submissions`, `five_c_answers`, `five_c_reflection_answers` | 5C-svarene |
| `feedback_releases` | Trenerens frigivelse av egne svar |
| `consent_events`, `player_access_events`, `player_deletion_events` | Samtykkeloggen, revisjonsloggen og sletteloggen |
| `succession_assessments`, `succession_ratings`, `player_succession_profiles` | Succession planning |
| `database_marker` | Én rad: utvikling eller drift |

**Fremmednøkler til kontoer.** `players.user_id` (kontoen slettes: spilleren blir stående uten
konto) og `guardianships.guardian_user_id` (kontoen slettes: koblingen slettes) peker på
`asp_net_users`. Kolonnene i logger og historikk som bærer en bruker-ID, har ingen
fremmednøkkel, fordi raden skal overleve at kontoen slettes. Begrunnelsen står i
`Data/AppDbContext.cs`.

**Hva som ble fjernet** da skjemaet ble laget på nytt: tabellene `Items`, `Responses` og
`Answers` fra det gamle ti-påstandsskjemaet, `CoachTeams` (styrte ingen tilgang), og kolonnen
`FiveCSubmissions.PlayerCode` (en kopi av spillerens navn). `Players.Code` heter nå
`players.name`.

### Migrasjoner i utvikling

Alle på gruppa kan lage en migrasjon. Si fra i kanalen før du gjør det: to migrasjoner laget
hver for seg mot samme modell gir en konflikt i `AppDbContextModelSnapshot.cs` som git ikke
klarer å flette.

```bash
dotnet ef migrations add <Navn> --project StartPraksisGruppe3Prosjekt --output-dir Data/Migrations
```

Appen kjører migrasjonen mot den lokale databasen ved neste `dotnet run`. I drift følger den
[«Kjøre migrasjonene»](#kjøre-migrasjonene).

---

## Må oppdateres i Sikt-meldingen

`docs/sikt-melding.md` er ikke endret i denne omgangen. Dette stemmer ikke lenger der, og må
rettes før meldingen sendes:

- **Punkt 4, hvilke opplysninger.** Spilleren lagres med **fullt navn** (`players.name`), ikke
  en pseudonym kode, og med fornavn og bilde. Raden om `Player.Code` og setningene om at
  grensesnittet bruker spillerkoden overalt, må skrives om. Raden om `CoachTeams` og
  avsnittet «Rester av det gamle skjemaet» utgår: tabellene er fjernet. `FiveCSubmissions` har
  ikke lenger en kopi av navnet. Tabellnavnene har fått små bokstaver og understrek.
- **Punkt 7, tilgangsstyring.** `CoachTeams` finnes ikke lenger.
- **Punkt 8, lagring og sikkerhet.** Ny lagringsplass og ny databehandler: en PostgreSQL-server
  IK Start eier (eller, til den er klar, en PostgreSQL i EU/EØS med databehandleravtale), ikke
  Supabase i Irland. Radene om Supabase, eieren av Supabase-prosjektet og Supabase-CA-en
  utgår. Nytt: app-rollen uten rett til å endre skjemaet, vernet mellom utvikling og drift, og
  at demokontoene ikke kan havne i driftsdatabasen. Avsnittet om lagring via PostgREST utgår:
  det lageret er fjernet.
- **Punkt 9, varighet og sletting.** Backup er ikke lenger «i Supabase»: hvor backupene ligger,
  hvor lenge de beholdes, og at en slettet spiller ligger i dem til de er rotert ut.
- **Avvik A9** (de gamle tabellene) er løst. **A6** er delvis løst: sletter en bruker sin egen
  konto, blir ikke lenger `players.user_id` stående og peke på en konto som ikke finnes.
- **«Hva som gjenstår», punkt 7.** Databehandleravtalen gjelder nå den som drifter
  databaseserveren og verten for appen, ikke Supabase.
