#!/usr/bin/env bash
# Prøver at den nyeste backupen faktisk lar seg gjenopprette.
#
#     BACKUP_DIR=/var/backups/startcompass scripts/database/restore-check.sh
#
# En backup ingen har gjenopprettet fra, er et håp og ikke en backup. Dette skriptet leser den
# nyeste fila inn i en egen prøvedatabase, teller radene i hver tabell, sammenligner med
# hoveddatabasen, og sletter prøvedatabasen igjen. Hoveddatabasen leses bare.
#
# Kjøres på databaseserveren av systembrukeren postgres, som backup.sh: den må få opprette og
# slette en database, og det får ikke rollene appen og databaseeieren bruker. Kjør den etter
# første backup, og siden en gang i måneden.
#
# Den feiler (exit 1) når fila ikke lar seg lese inn, eller når den gjenopprettede databasen
# mangler markeringen. Tabeller med flere rader i hoveddatabasen enn i backupen er normalt:
# det er det som er kommet til siden backupen ble tatt. Færre rader i hoveddatabasen betyr at
# noe er slettet siden. Er lista over migrasjoner en annen, sier den fra: backupen er da tatt
# før siste migrasjon, og må leses inn med appversjonen fra den gang.
set -euo pipefail

BACKUP_DIR="${BACKUP_DIR:?Sett BACKUP_DIR til mappa backupene ligger i.}"
DATABASE="${PGDATABASE:-startcompass}"
CHECK_DATABASE="${CHECK_DATABASE:-${DATABASE}_restore_check}"

if [ "$CHECK_DATABASE" = "$DATABASE" ]; then
    echo "Prøvedatabasen kan ikke være hoveddatabasen." >&2
    exit 2
fi

latest=""

if [ -d "$BACKUP_DIR" ]; then
    latest="$(find "$BACKUP_DIR" -maxdepth 1 -type f -name "$DATABASE-*.dump" | sort | tail -n 1)"
fi

if [ -z "$latest" ]; then
    echo "Fant ingen backup av $DATABASE i $BACKUP_DIR." >&2
    exit 1
fi

# Prøvedatabasen skal bort uansett hvordan dette ender. Den er en full kopi av alt.
trap 'dropdb --if-exists "$CHECK_DATABASE" 2> /dev/null' EXIT

# "Finnes ikke, hopper over" er det vanlige svaret her, og ikke noe å lese.
dropdb --if-exists "$CHECK_DATABASE" 2> /dev/null
createdb "$CHECK_DATABASE"
pg_restore --no-owner --no-privileges --exit-on-error --dbname="$CHECK_DATABASE" "$latest"

counts() {
    psql --no-psqlrc --quiet --tuples-only --no-align --field-separator=' ' --dbname="$1" --command="
        select table_name,
               (xpath('/row/c/text()', query_to_xml(format('select count(*) as c from %I', table_name), false, true, '')))[1]::text
        from information_schema.tables
        where table_schema = 'public' and table_type = 'BASE TABLE'
        order by table_name"
}

scalar() {
    psql --no-psqlrc --quiet --tuples-only --no-align --dbname="$1" --command="$2"
}

live="$(counts "$DATABASE")"
restored="$(counts "$CHECK_DATABASE")"

printf '%-34s %12s %12s\n' "tabell" "hoveddatabase" "backup"

status=0

while read -r table rows; do
    [ -n "$table" ] || continue
    before="$(printf '%s\n' "$restored" | awk -v t="$table" '$1 == t { print $2 }')"
    note=""

    if [ -z "$before" ]; then
        note="  finnes ikke i backupen"
    elif [ "$rows" -gt "$before" ]; then
        note="  +$((rows - before)) siden backupen"
    elif [ "$rows" -lt "$before" ]; then
        note="  -$((before - rows)) siden backupen"
    fi

    printf '%-34s %12s %12s%s\n' "$table" "$rows" "${before:--}" "$note"
done <<< "$live"

if [ "$(scalar "$CHECK_DATABASE" "select count(*) from database_marker")" != "1" ]; then
    echo "FEIL: den gjenopprettede databasen har ingen markering (database_marker)." >&2
    status=1
fi

migrations='select coalesce(string_agg(migration_id, '"','"' order by migration_id), '"''"') from "__EFMigrationsHistory"'

if [ "$(scalar "$DATABASE" "$migrations")" != "$(scalar "$CHECK_DATABASE" "$migrations")" ]; then
    echo "MERK: backupen har en annen liste over migrasjoner enn hoveddatabasen. Den er tatt før siste migrasjon." >&2
fi

if [ "$status" -eq 0 ]; then
    echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) gjenoppretting ok: $latest lot seg lese inn i sin helhet."
fi

exit "$status"
