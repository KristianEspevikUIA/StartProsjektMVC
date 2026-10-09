# Velkomst med navn og bilde

Trenerne ønsket at spilleren skal møtes med «Welcome, Alex» og sitt eget bilde når hen logger
inn, så det føles mer personlig. Bildene er klubbens offisielle spillerbilder for G14, G15, G17 og G19,
som allerede er publisert på nett. IK Start har gitt tillatelse til at de brukes her.

---

## Slik virker det

- **Spilleren** logger inn og lander på forsiden. Har klubben lagt inn fornavn eller bilde, står
  det «Welcome, Alex» i toppen, med bildet i en sirkel ved siden av. Bildet står også på
  kontoknappen oppe til høyre, på alle sider, i stedet for forbokstaven. Uten noe lagt inn ser
  forsiden ut som før.
- **Admin** legger inn fornavn og bilde på `/Admin/Players` → «Name and photo». Siden viser hva
  spilleren kommer til å se, før det lagres. Samme liste har også lenker til innsyn (Export) og
  sletting for hver spiller, som tidligere bare kunne nås med ID-en i URL-en.
- **Trenere og admin** ser fornavnet og bildet på én side: «Best eleven» (`/Succession/Formation`),
  der hver spiller er en markør på banen med bildet i og navnet under, og innbytterne har bildet
  ved navnet. Trenerne ba om det. Har to spillere på siden samme fornavn, står hele navnet under.
  Bildet hentes fra `/Succession/Photo/{id}`, som bare gir bildet av en spiller `CanViewPlayer`
  slipper treneren til. Den logges ikke for seg: siden bildet står på, logger hver spiller den viser.
- **Andre sider** viser spillerens fulle navn (`Player.Code`), ikke fornavnet herfra.

Velkomsten er ikke låst til bestemte lag i koden. Alle spillere med konto kan få den. Det er
klubben som bestemmer hvem som får navn og bilde lagt inn.

---

## Hva som lagres, og hvorfor bare det

Tabellen `PlayerPersonalDetails`, én rad per spiller:

| Felt | Merknad |
| --- | --- |
| `FirstName` | Bare fornavn. «Welcome, Alex» trenger ikke mer, og mindre er mindre å miste |
| `Photo`, `PhotoContentType` | Bildet, kontrollert og renset (se under). Høyst 2 MB |
| `PhotoSource` | Hvor bildet kom fra, f.eks. «ikstart.no, spillerbilder 2026» |
| `UpdatedByUserId`, `UpdatedAt`, `PhotoUpdatedAt` | Hvem som la det inn, og når |

**Egen tabell, med vilje.** Fornavnet og bildet ligger ikke på `Player`, så en side som bare
skal vise det fulle navnet ikke kan vise bildet ved en feil.
Ingenting utenom velkomsten, kontoknappen, admin-skjemaet og «Best eleven» leser tabellen, og alle
går gjennom `IPlayerWelcomeService`. «Best eleven» henter fornavnene (`FirstNamesAsync`) og hvilke
spillere som har bilde (`PhotoVersionsAsync`); selve bildet hentes ett og ett.

**Bildet ligger i databasen, ikke på klubbens nettsted.** Tre grunner:

1. Ingen ekte bilder eller navn havner i git-repoet, som er offentlig. De legges inn i appen.
2. Å lenke direkte til bildene på klubbens nettsted ville fortalt det nettstedet hver gang en
   spiller logget inn her. CSP-en tillater heller ikke bilder fra andre domener.
3. Når en spiller slettes, forsvinner bildet med det samme (cascade), og det er med i innsynet.

---

## Bildekontroll

`Services/PlayerPhotoRules.cs`.

- **Formatet leses av filens egne bytes**, ikke av filnavnet eller det nettleseren oppgir. Bare
  JPEG, PNG og WebP godtas. SVG (som kan inneholde skript), GIF og alt annet avvises.
- **Metadata fjernes før lagring:** EXIF (ofte med GPS-posisjon og tidspunkt), XMP og IPTC
  (bildetekst, fotograf, nøkkelord) og kommentarer. Det selve bildet trenger (bildedata,
  fargeprofil) beholdes byte for byte. Bildet kodes ikke om, så det er nøyaktig det klubben
  publiserte.
- **En fil som ikke kan leses, avvises**, og da lagres ingenting av skjemaet, heller ikke navnet.

---

## Tilgang

| Hvem | Ser navn og bilde |
| --- | --- |
| Spilleren selv | Ja, på forsiden og på kontoknappen. `/Player/Photo` har ingen ID, så den kan bare gi ditt eget bilde |
| Admin | Ja, på `/Admin/Players` og skjemaet. Å åpne skjemaet logges i revisjonsloggen |
| Trener | Fornavn og bilde, bare på «Best eleven». Siden logges i revisjonsloggen for hver spiller den viser |
| Foresatt | Nei |
| Ikke innlogget | Nei |

Bildet sendes med `Cache-Control: private`, så det ikke lagres i noen mellomlager på veien.
Forhåndsvisningen for admin sendes med `no-store`.

---

## Personvern: må avklares før ekte data legges inn

- **Sikt.** `docs/sikt-melding.md` sa at navn ikke lagres om spillere. Det stemmer ikke lenger,
  og meldingen er oppdatert. Prosjektets egen regel er at ekte spillerdata ikke skal inn før
  prosjektet er meldt til Sikt. Klubbens tillatelse gjelder publiseringen, men Sikt-meldingen må
  også vise dette før fornavn og bilder av ekte spillere legges inn i appen.
- **Informasjon til spillerne og foresatte.** Bildene er publisert fra før, men dette er en ny
  bruk: i et system som også har svarene deres. Det bør stå i personvernerklæringen.
- **De ekte troppene:** `scripts/squads/fetch_squads.py` henter navn, posisjon, fødselsdato og
  bilde for G14, G15, G17 og G19 fra klubbens spillersider til `Data/Squads/`, som er git-ignorert.
  Finnes fila, legger `Data/SeedSquads.cs` inn spillerne med konto (`spiller.leon.enger@ikstart.example`),
  fornavn og bilde i Development, og sletter de oppdiktede. Bildene går gjennom samme kontroll
  som en opplasting på admin-siden.
- **Spillere som ikke står på troppsidene,** men som trenerne vurderer (utlånt, rykket opp til
  A-laget, ikke lagt ut ennå), står i den git-ignorerte `Data/Squads/extra-players.json`, med lag
  og profilside. Uten profilside brukes navnet og fødselsåret fra trenernes ark, fødselsdatoen
  settes til 1. januar som for andre uten dato, og posisjonen står tom. Se «Players the coaches
  rate who are not on the pages» øverst i `fetch_squads.py`.
- **Demodata:** uten den fila gir `Data/SeedWelcome.cs` de oppdiktede spillerkontoene fornavn,
  og ingen bilder.

---

## Filer

| Hva | Hvor |
| --- | --- |
| Modell | `Models/PlayerPersonalDetails.cs` |
| Oppslag og lagring | `Services/PlayerWelcomeService.cs` |
| Bildekontroll | `Services/PlayerPhotoRules.cs` |
| Velkomsten | `Controllers/HomeController.cs`, `Views/Home/Index.cshtml` |
| Spillerens eget bilde | `PlayerController.Photo` (`/Player/Photo`) |
| Admin | `AdminController.Players`, `PlayerDetails`, `PlayerPhoto`, `Views/Admin/Players.cshtml`, `PlayerDetails.cshtml` |
| Migrasjon | `Data/Migrations/*_AddPlayerPersonalDetails.cs` |
