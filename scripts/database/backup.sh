#!/usr/bin/env bash
# Daglig backup av hoveddatabasen.
#
#     BACKUP_DIR=/var/backups/startcompass scripts/database/backup.sh
#
# Kjøres på databaseserveren, av systembrukeren postgres (cron eller en systemd-timer). Da
# trengs det ikke noe passord noe sted: postgres kobler til over den lokale socketen. Skal den
# kjøres fra en annen maskin, settes PGHOST og PGUSER, og passordet legges i ~/.pgpass
# (rettighet 600) -- aldri i dette skriptet eller i crontab.
#
# Hva den gjør, i denne rekkefølgen:
#   1. pg_dump av hele databasen til én fil, i PostgreSQLs eget format.
#   2. Sjekker at fila lar seg lese (pg_restore --list). En dump som ikke kan leses, får aldri
#      et navn som ser ut som en backup.
#   3. Kopierer fila til BACKUP_COPY_DIR, om den er satt. Den skal ligge et annet sted enn
#      databaseserveren: en backup på samme disk som databasen er borte samtidig med den.
#   4. Sletter backuper eldre enn KEEP_DAYS (30). Bare filer den selv har laget, og bare når
#      dagens backup gikk bra -- en backup som feiler, skal ikke koste de gamle.
#
# FILA INNEHOLDER ALT: navn, fødselsdato og bilde av spillerne, svar og vurderinger. Mappa
# skal ligge på en kryptert disk og bare kunne leses av brukeren som kjører skriptet, og kopien
# et annet sted skal være kryptert. En spiller som slettes i appen, ligger i backupene til de
# er rotert ut, altså i KEEP_DAYS dager. Det skal stå i informasjonen til de registrerte.
#
# Prøv gjenopprettingen med scripts/database/restore-check.sh. Se docs/database.md.
set -euo pipefail
umask 077

BACKUP_DIR="${BACKUP_DIR:?Sett BACKUP_DIR til mappa backupene skal ligge i.}"
BACKUP_COPY_DIR="${BACKUP_COPY_DIR:-}"
KEEP_DAYS="${KEEP_DAYS:-30}"
DATABASE="${PGDATABASE:-startcompass}"

case "$KEEP_DAYS" in
    ''|*[!0-9]*) echo "KEEP_DAYS må være et helt tall, ikke «$KEEP_DAYS»." >&2; exit 2 ;;
esac

stamp="$(date -u +%Y-%m-%dT%H%M%SZ)"
file="$BACKUP_DIR/$DATABASE-$stamp.dump"
partial="$file.partial"

mkdir -p "$BACKUP_DIR"

# Under et annet navn til den er ferdig og sjekket. Stopper skriptet halvveis, ligger det
# igjen en .partial, og ingen tar den for en backup.
trap 'rm -f "$partial"' EXIT

pg_dump --format=custom --file="$partial" "$DATABASE"
pg_restore --list "$partial" > /dev/null
mv "$partial" "$file"

if [ -n "$BACKUP_COPY_DIR" ]; then
    mkdir -p "$BACKUP_COPY_DIR"
    cp -p "$file" "$BACKUP_COPY_DIR/"
fi

# Rotasjonen. Mønsteret er databasens eget navn, så ingenting annet i mappa røres.
for directory in "$BACKUP_DIR" "$BACKUP_COPY_DIR"; do
    [ -n "$directory" ] || continue
    find "$directory" -maxdepth 1 -type f -name "$DATABASE-*.dump" -mtime "+$KEEP_DAYS" -delete
done

count="$(find "$BACKUP_DIR" -maxdepth 1 -type f -name "$DATABASE-*.dump" | wc -l)"
size="$(du -h "$file" | cut -f1)"

echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) backup ok: $file ($size). $count backuper i mappa, beholdes i $KEEP_DAYS dager."
