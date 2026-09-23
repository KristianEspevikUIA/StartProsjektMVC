# Velkomst med navn og bilde

Trenerne ønsket at spilleren skal møtes med «Welcome, Alex» og sitt eget bilde når hen logger
inn, så det føles mer personlig. Bildene er klubbens offisielle spillerbilder for U14, U15 og U17,
som allerede er publisert på nett. IK Start har gitt tillatelse til at de brukes her.

---

## Slik virker det

- **Spilleren** logger inn og lander på forsiden. Har klubben lagt inn fornavn eller bilde, står
  det «Welcome, Alex» i toppen, med bildet i en sirkel ved siden av. Uten noe lagt inn ser
  forsiden ut som før.
- **Admin** legger inn fornavn og bilde på `/Admin/Players` → «Name and photo». Siden viser hva
  spilleren kommer til å se, før det lagres. Samme liste har også lenker til innsyn (Export) og
  sletting for hver spiller, som tidligere bare kunne nås med ID-en i URL-en.
- **Trenere og admin** ser fornavnet på én side: «Best eleven» (`/Succession/Formation`), der
  hver spiller er en drakt med navnet under. Trenerne ba om det. Har to spillere på siden samme
  fornavn, står koden under. Bildet vises ikke der.
- **Ingen andre steder.** Trenersidene, lagoversiktene og succession-tavla bruker fortsatt
  spillerkoden, og det er en test som passer på at navnet ikke dukker opp der.

Velkomsten er ikke låst til U14, U15 og U17 i koden. Alle spillere med konto kan få den. Det er
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

**Egen tabell, med vilje.** Dette er det eneste stedet systemet lagrer navnet til en spiller.
Det ligger ikke på `Player`, så en side som skal vise en kode ikke kan vise et navn ved en feil.
Ingenting utenom velkomsten, admin-skjemaet og «Best eleven» leser tabellen, og alle går gjennom
`IPlayerWelcomeService`. «Best eleven» henter bare fornavnene (`FirstNamesAsync`), aldri bildet.

**Bildet ligger i databasen, ikke på klubbens nettsted.** Tre grunner:

1. Ingen ekte bilder eller navn havner i git-repoet, som er offentlig. De legges inn i appen.
2. Å lenke direkte til bildene på klubbens nettsted ville fortalt det nettstedet hver gang en
   spiller logget inn her. CSP-en tillater heller ikke bilder fra andre domener.
3. Når en spiller slettes, forsvinner bildet med det samme (cascade), og det er med i innsynet.

---

## Bildekontroll

`Services/PlayerPhotoRules.cs`, med tester i `PlayerPhotoRulesTests`.

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
| Spilleren selv | Ja, på forsiden. `/Player/Photo` har ingen ID, så den kan bare gi ditt eget bilde |
| Admin | Ja, på `/Admin/Players` og skjemaet. Å åpne skjemaet logges i revisjonsloggen |
| Trener | Fornavnet, bare på «Best eleven». Ikke bildet. Siden logges i revisjonsloggen for hver spiller den viser |
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
- **Demodata:** `Data/SeedWelcome.cs` gir spillerkontoene i Development oppdiktede fornavn, og
  ingen bilder. Ingen av navnene finnes i trenernes arbeidsbok.

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
| Tester | `PlayerWelcomeTests`, `PlayerPhotoRulesTests` |
