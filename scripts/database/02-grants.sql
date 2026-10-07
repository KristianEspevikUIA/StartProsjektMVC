-- Rettighetene til app-rollen: lese og skrive rader, og ikke noe mer.
--
-- Kjøres av databaseeieren (startcompass_owner) ETTER HVER MIGRASJON, mot hoveddatabasen:
--
--     psql -h <server> -U startcompass_owner -d startcompass -v ON_ERROR_STOP=1 -f scripts/database/02-grants.sql
--
-- Skriptet kan kjøres så mange ganger man vil. Det tar først alt fra app-rollen og gir så
-- det den skal ha, slik at resultatet er det samme uansett hva som sto der fra før -- også
-- for en tabell som kom med siste migrasjon.
--
-- Se docs/database.md, «Kjøre migrasjonene».

begin;

revoke all on all tables in schema public from startcompass_app;
revoke all on all sequences in schema public from startcompass_app;

-- Det vanlige: lese og skrive rader. Ingen TRUNCATE, og ingen rett til å endre skjemaet --
-- den har bare eieren av tabellene.
grant select, insert, update, delete on all tables in schema public to startcompass_app;
grant usage, select on all sequences in schema public to startcompass_app;

-- Unntakene. Appen trenger mindre enn «lese og skrive» på disse:

-- EFs egen liste over kjørte migrasjoner. Appen leser den ved oppstart for å se at skjemaet
-- er det den forventer, og skal aldri skrive i den.
revoke insert, update, delete on "__EFMigrationsHistory" from startcompass_app;

-- Markeringen av at dette er driftsdatabasen. Appen skriver den én gang, i en tom database,
-- og skal ikke kunne endre eller fjerne den etterpå.
revoke update, delete on database_marker from startcompass_app;

-- De fire loggene som bare skal vokse. Appen legger til rader og endrer eller sletter dem
-- aldri (AppDbContext nekter); her nekter databasen også. Radene som forsvinner når en
-- spiller slettes, slettes av fremmednøkkelen (ON DELETE CASCADE), og den virker med
-- rettighetene til eieren av tabellen, ikke app-rollens. player_deletion_events har ingen
-- fremmednøkkel og blir stående.
revoke update, delete on consent_events from startcompass_app;
revoke update, delete on player_access_events from startcompass_app;
revoke update, delete on feedback_releases from startcompass_app;
revoke update, delete on player_deletion_events from startcompass_app;

commit;
