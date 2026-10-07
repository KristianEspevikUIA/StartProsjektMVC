-- Hoveddatabasen: de to rollene og en tom database.
--
-- Kjøres ÉN gang, av en superbruker (postgres), på serveren databasen skal ligge på:
--
--     psql -h <server> -U postgres -v ON_ERROR_STOP=1 -f scripts/database/01-roles-and-database.sql
--
-- Skriptet setter INGEN passord, og det er meningen: et passord i en fil er et passord i git.
-- Sett dem etterpå, i psql, med \password. Den spør to ganger og sender ikke passordet i klartekst:
--
--     \password startcompass_owner
--     \password startcompass_app
--
-- To roller, fordi de gjør to forskjellige ting:
--
--   startcompass_owner  eier databasen og tabellene. Kjører migrasjonene. Brukes av de få
--                       som er databaseeiere, og aldri av appen.
--   startcompass_app    det appen kobler til med. Leser og skriver rader, og kan ikke endre
--                       skjemaet. Rettighetene på tabellene gis av 02-grants.sql etter hver
--                       migrasjon.
--
-- Se docs/database.md, «Opprette hoveddatabasen».

create role startcompass_owner login;
create role startcompass_app login;

create database startcompass
    owner startcompass_owner
    encoding 'UTF8';

-- Ingen andre roller på serveren skal kunne koble til denne databasen.
revoke all on database startcompass from public;
grant connect on database startcompass to startcompass_app;

\connect startcompass

-- Fra og med PostgreSQL 15 kan ikke hvem som helst opprette tabeller i public. Sagt
-- eksplisitt her, så det også gjelder en database som er oppgradert fra en eldre versjon.
revoke create on schema public from public;
grant usage on schema public to startcompass_app;
