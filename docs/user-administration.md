# Kontoer

Hvem som kan logge inn, og hvordan en ny person slipper inn. Alt styres av en administrator
på `/Admin/Users`.

Det finnes ingen annen vei. Selvregistrering er stengt, og appen sender ikke e-post, så det
finnes verken invitasjonslenker eller «glemt passord»-lenker. Den første administratoren i
en ny database lages med `create-admin` (se [`docs/database.md`](database.md)). Alle andre
lages her.

---

## Slik virker det

1. **Administratoren oppretter kontoen** med e-postadressen personen skal logge inn med, og
   en rolle.
2. **Appen lager et midlertidig passord** og viser det én gang, på siden som kommer etterpå.
   Det kan ikke hentes fram igjen, bare byttes ut med et nytt.
3. **Administratoren gir passordet videre**, muntlig eller på telefon. Ikke i en e-post eller
   en gruppechat sammen med adressen.
4. **Personen logger inn** og sendes rett til siden der de velger sitt eget passord. Til det
   er gjort, åpner ingenting annet i appen.

Passordet er på fire grupper med fem tegn, for eksempel `Kf7qZ-p3mRt-8xWn2-cHd5v`, uten
tegnene som er lette å forveksle (0 og O, 1, l og I). Det nye passordet personen velger, må
følge de samme reglene som alle andre: minst 12 tegn, med stor og liten bokstav, tall og
spesialtegn, og det må være et annet enn det midlertidige.

---

## Hva en konto er knyttet til

| Rolle | Knyttet til | Ser |
| --- | --- | --- |
| Trener | Ingenting | Alle lag og alle spillere. Oppslag på enkeltspillere logges |
| Administrator | Ingenting | Alt, og styrer kontoer, perioder og spillere |
| Spiller | Én spiller (`players.user_id`) | Sine egne svar, og ingen andres |
| Foresatt | Én eller flere spillere (`guardianships`) | Svarene til de spillerne |

Tre skjemaer på siden, ett for hvert tilfelle. En foresatt knyttes til én spiller når kontoen
opprettes. Flere legges til på kontoens egen side etterpå, for eksempel for søsken.

En foresatt som ikke er knyttet til noen, kan logge inn og ser ingenting. Lista viser det med
merket «Nobody».

---

## Regler

Reglene ligger i `Services/AccountAdministration.cs`, ikke i controlleren, så de gjelder
uansett hvilken side som kaller.

- **En spiller under 19 får ikke konto før en foresatt er registrert.** Opprett den foresattes
  konto først, så spillerens. Og motsatt: den siste foresatte til en spiller under 19 som har
  konto, kan ikke fjernes. Grensen er `PlayerRules.GuardianRequiredBelowAge`.
- **Én konto per spiller, og én konto per e-postadresse.**
- **Ingen låser sin egen konto,** og den siste administratoren som kan logge inn, kan ikke
  låses.
- **En administrator gir ikke seg selv et midlertidig passord.** Eget passord byttes under
  «My account».
- **Kontoer låses, de slettes ikke.** Loggene navngir personer med konto-ID: hvem som så på en
  spiller, hvem som endret et samtykke. En konto som er borte, gir en logg som ikke lenger
  sier hvem. En spillers konto slettes sammen med spilleren, på `/Admin/Delete/{id}`, og bare
  der.

---

## Det en administrator kan gjøre med en konto

På `/Admin/Account/{id}`, som lista lenker til:

- **Nytt midlertidig passord.** Det gamle slutter å virke, kontoen logges ut der den er
  innlogget, og personen må velge sitt eget ved neste innlogging. **Dette er også svaret når
  noen har glemt passordet.** Innloggingssiden sier det samme, og sidene for «glemt passord»
  er stengt: de lovet en e-post appen aldri sendte.
- **Låse og låse opp.** For en som har sluttet i klubben. En låst konto logges ut innen fem
  minutter og kan ikke logge inn. Ingenting slettes, og kontoen kan låses opp igjen.
- **Legge til og fjerne spillere** for en foresatt.

Fem feil passord på rad stenger en konto i femten minutter. Det går over av seg selv. Et nytt
midlertidig passord åpner den med en gang.

Rollen til en konto kan ikke endres. Trenger noen en annen rolle, får de en ny konto.

---

## Hva som logges

Hver handling skrives til applikasjonsloggen med konto-ID-er, aldri e-postadresser eller
passord:

```
Account <id> created with the role Coach by administrator <id>.
Account <id> was given a new temporary password by administrator <id>.
Account <id> changed its password, replacing a temporary one.
Account <id> locked by administrator <id>.
```

Det finnes ingen egen tabell for dette. Loggen ligger der applikasjonsloggen ellers ligger,
og følger dens levetid.

---

## Mange på samme nett

Innlogging er begrenset til ti forsøk per fem minutter per IP-adresse. Grensen stopper den
som prøver ett passord mot mange kontoer. Den stopper også et helt lag som logger inn for
første gang fra klubbhusets nett, etter ti spillere. Passordbyttet teller ikke med i de ti,
men innloggingen gjør det. Del ut kontoene i puljer, eller la spillerne logge inn hjemmefra.
Grensen står i `Security/RateLimitingExtensions.cs`.

---

## Personvern

En konto er en e-postadresse, og for spillere og foresatte er det en ny personopplysning i
systemet: adressen til en mindreårig, eller til foreldrene. Den vises bare for
administratorer, på `/Admin/Users`. Adressene må komme fra klubben, og skal med i
informasjonen til de registrerte og i meldingen til Sikt når den sendes.

---

## Filer

| Hva | Hvor |
| --- | --- |
| Reglene | `Services/AccountAdministration.cs` |
| Sidene | `AdminController.Users`, `CreateUser`, `Account`, `IssuePassword`, `LockAccount`, `UnlockAccount`, `AddGuardianLink`, `RemoveGuardianLink` |
| Views | `Views/Admin/Users.cshtml`, `Account.cshtml`, `TemporaryPassword.cshtml` |
| Midlertidig passord, og sperren til det er byttet | `Security/TemporaryPassword.cs` |
| Passordbyttet | `Areas/Identity/Pages/Account/Manage/ChangePassword.cshtml` |
| Stengte sider | `Security/ClosedRegistrationExtensions.cs` |
