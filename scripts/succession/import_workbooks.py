#!/usr/bin/env python3
"""
Reads the coaches' filled-in "IK Start Succession Planning" workbooks, matches every row to a
player in the real squads, and writes what can be imported for the application to load.

    python3 scripts/succession/import_workbooks.py <workbooks or folders>
    python3 scripts/succession/import_workbooks.py <workbooks or folders> --table

Reads   StartPraksisGruppe3Prosjekt/Data/Squads/squads.json        (scripts/squads/fetch_squads.py)
        StartPraksisGruppe3Prosjekt/Data/Succession/Import/decisions.json   (optional, written by hand)
Writes  StartPraksisGruppe3Prosjekt/Data/Succession/Import/assessments.json
        StartPraksisGruppe3Prosjekt/Data/Succession/Import/report.md

The application loads assessments.json when it seeds in Development (Data/SeedSuccessionImport.cs).
That folder is git-ignored ON PURPOSE: the workbooks name every player, most of them minors,
next to the coaches' judgement of them, and the repository is public. See
docs/succession-planning.md.

Needs Python 3.9+. Nothing else -- the workbooks are read as the zip of XML files they are.

One workbook is one coach
-------------------------
Every coach got the same template with the same players in it, and filled in the players they
know. The coach is taken from the file name -- IK_Start_Succession_Planning_AB.xlsx is "ab",
CD_IK_Start_Succession_Planning.xlsx is "cd" -- because the sheet's own "Coach" column is empty
in half of them. Where the file name has a surname after the initials, only the capitals are
kept: "ENordmann" is "en". The application gives each coach a locked account,
trener.ab@ikstart.example.

A row is an assessment when it has at least one of the six ratings. A row with a coach's name
or a category but no rating is counted and left out: the application's own form will not save
one without ratings either.

What is taken, and what is not
------------------------------
Only the structured columns: the six ratings, "Rated as", the ability category, the three
positions, the coach's personal readiness, the succession risk when it is Green, Amber or Red,
"Pathway blocked?" and "External needed?" when they start with yes or no, and the contract
type, contract end and MESO training group.

The free-text columns -- the notes, the three projections, "What now?", the development focus
and the strengths -- are never read. In the workbooks the coaches handed in, they and the
columns meant for lists hold injuries, growth, family circumstances and other players' names,
which the application's own form asks coaches to leave out. A list column that holds text
instead of a list value is skipped the same way, and the report says which column, never what
it said.

A value the application cannot hold -- a personal readiness of 5.5, an availability of 0 -- is
left out rather than rounded, and listed in the report.

How a row is matched
--------------------
Against the names in squads.json, which are the club's own spelling and the names the players
have in the application. Names are compared without case, accents, hyphens or word order, with
ae/o/aa for the Norwegian letters.

  exact       The same words, perhaps in another order. Matched.
  partial     One name is the other with a middle name left out: matched only when the club's
              page gives the same year of birth as the workbook.
  close       A letter or two apart in a word: matched only on the same year of birth too.

Two candidates for one row, two rows for one player, or a year of birth that contradicts an
exact name: the row is not matched, and the report says why and who the candidates were.

decisions.json settles the rest by hand, keyed "Last name, First name" as the workbook writes
it. A club name links the row to that player; null leaves it out for good:

    { "matches": { "Nordmann, Ola": "Ola Kristian Nordmann", "Hansen, Per": null } }

Why it refuses rather than guesses
----------------------------------
A workbook without the template's sheet or columns, two workbooks for the same coach, a file
name without a coach in it, or a decision naming a player who is not in the squads stops the
run with the problem named. Half an import would otherwise replace the last full one.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import re
import sys
import unicodedata
import zipfile
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
APP = REPO / "StartPraksisGruppe3Prosjekt"
DEFAULT_SQUADS = APP / "Data" / "Squads" / "squads.json"
DEFAULT_OUT = APP / "Data" / "Succession" / "Import"
CATALOG = APP / "Data" / "Succession" / "succession-planning.json"

SCHEMA_VERSION = 1

SHEET = "Template - Do Not Touch (1-10)"
FILE_MARKER = "ik_start_succession_planning"

MAIN = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"
DOC_REL = "{http://schemas.openxmlformats.org/officeDocument/2006/relationships}"

# The template's headings, lower-case, and what each column is. The free-text columns are not
# here, so they are never read. "Availabilty" is the template's own spelling.
COLUMNS = {
    "last name": "last",
    "first name": "first",
    "coach/coaches (raters)": "coach",
    "rated as (list)": "ratedAs",
    "ability cat. (list)": "abilityCategory",
    "1st position (l)": "firstPosition",
    "2nd position (l)": "secondPosition",
    "3rd position (l)": "thirdPosition",
    "year born": "yearBorn",
    "contract type (l)": "contractType",
    "contract end": "contractEndsOn",
    "meso training group (l)": "trainingGroup",
    "current team (l)": "team",
    "physical (1-10)": "physical",
    "technical (1-10)": "technical",
    "tactical (1-10)": "tactical",
    "mental (1-10)": "mental",
    "professionalism (1-10)": "professionalism",
    "availabilty (1-10)": "availability",
    "availability (1-10)": "availability",
    "coaches personal readiness for next step (1-10)": "personalReadiness",
    "pathway blocked?": "pathwayBlocked",
    "succession risk (green/amber/red)": "successionRisk",
    "external needed? (y/n)": "externalNeeded",
}

REQUIRED = {"last", "first", "physical", "technical", "tactical", "mental", "professionalism", "availability"}

# The workbook's "Current Team" and the application's team, for the report only: the team a
# player is on in the application comes from the squads, not from here.
TEAMS = {"u14s": "G14", "u15s": "G15", "u17s": "G17", "u19s": "G19"}

# Columns the template came with empty: a row with one of these filled in, but no rating, is a
# coach's row that is left out, not an untouched one.
TOUCHED = ("coach", "personalReadiness", "successionRisk", "pathwayBlocked", "externalNeeded")

YES = {"y", "yes", "ja", "j"}
NO = {"n", "no", "nei"}


class Refused(Exception):
    pass


# --- Reading a workbook ----------------------------------------------------------------------


def text_of(node: ET.Element) -> str:
    """A shared or inline string: its own <t>, or the <t> of each run -- not the phonetic hints."""
    own = node.find(MAIN + "t")
    if own is not None:
        return own.text or ""
    return "".join(t.text or "" for r in node.findall(MAIN + "r") for t in r.findall(MAIN + "t"))


def read_sheet(path: Path) -> list[dict[str, object]]:
    """The template sheet as one {column letter: value} per row, with formula cells left out."""
    with zipfile.ZipFile(path) as book:
        workbook = ET.fromstring(book.read("xl/workbook.xml"))
        relations = ET.fromstring(book.read("xl/_rels/workbook.xml.rels"))
        targets = {r.get("Id"): r.get("Target") for r in relations}

        sheet = next((s for s in workbook.iter(MAIN + "sheet") if s.get("name") == SHEET), None)
        if sheet is None:
            raise Refused(f"{path.name}: no sheet called '{SHEET}'. Is it the coaches' template?")

        target = targets[sheet.get(DOC_REL + "id")]
        target = target.lstrip("/") if target.startswith("/") else f"xl/{target}"

        shared = []
        if "xl/sharedStrings.xml" in book.namelist():
            shared = [text_of(si) for si in ET.fromstring(book.read("xl/sharedStrings.xml")).iter(MAIN + "si")]

        rows: dict[int, dict[str, object]] = {}

        for cell in ET.fromstring(book.read(target)).iter(MAIN + "c"):
            # Overall readiness is the template's =SUM(N:S)/6. The application works it out.
            if cell.find(MAIN + "f") is not None:
                continue

            reference = cell.get("r")
            column = re.match(r"[A-Z]+", reference).group()
            kind = cell.get("t")
            raw = cell.find(MAIN + "v")

            if kind == "inlineStr":
                value: object = text_of(cell.find(MAIN + "is"))
            elif raw is None or raw.text is None or kind == "e":
                continue
            elif kind == "s":
                value = shared[int(raw.text)]
            elif kind in ("str", "b"):
                value = raw.text
            else:
                number = float(raw.text)
                value = int(number) if number.is_integer() else number

            if isinstance(value, str):
                value = " ".join(value.split())
                if not value:
                    continue

            rows.setdefault(int(reference[len(column):]), {})[column] = value

        return [dict(rows[number], _row=number) for number in sorted(rows)]


def saved_on(path: Path) -> dt.date:
    """When the coach last saved the workbook: the cycle the assessments belong to."""
    with zipfile.ZipFile(path) as book:
        core = book.read("docProps/core.xml").decode("utf-8")

    match = re.search(r"<dcterms:modified[^>]*>(\d{4}-\d{2}-\d{2})", core)
    if not match:
        raise Refused(f"{path.name}: the workbook does not say when it was saved. Use --rated-on.")

    return dt.date.fromisoformat(match.group(1))


def rater_of(path: Path) -> str:
    stem = path.stem
    at = stem.lower().find(FILE_MARKER)
    if at < 0:
        raise Refused(f"{path.name}: the file name does not say whose workbook it is "
                      "(expected IK_Start_Succession_Planning_XX.xlsx).")

    after = re.findall(r"[A-Za-z]+", stem[at + len(FILE_MARKER):])
    before = re.findall(r"[A-Za-z]+", stem[:at])
    tag = after[0] if after else before[-1] if before else None

    if tag is None:
        raise Refused(f"{path.name}: the file name does not say whose workbook it is.")

    capitals = "".join(ch for ch in tag if ch.isupper())
    return (capitals if len(capitals) >= 2 else tag).lower()


def read_workbook(path: Path, rated_on: dt.date | None) -> dict:
    rows = read_sheet(path)
    if not rows or rows[0]["_row"] != 1:
        raise Refused(f"{path.name}: the first row has no headings.")

    header = {column: COLUMNS.get(str(text).strip().lower())
              for column, text in rows[0].items() if column != "_row"}
    missing = REQUIRED - set(header.values())
    if missing:
        raise Refused(f"{path.name}: the sheet has no column for {', '.join(sorted(missing))}.")

    players = []
    for row in rows[1:]:
        fields = {header[c]: v for c, v in row.items() if header.get(c)}
        if fields.get("last") or fields.get("first"):
            fields["_row"] = row["_row"]
            players.append(fields)

    return {
        "file": path.name,
        "rater": rater_of(path),
        "ratedOn": rated_on or saved_on(path),
        "rows": players,
    }


# --- The catalog: the application's own lists ------------------------------------------------


def option_key(text: str) -> str:
    """'ACM (10)' and 'Attacking midfielder (10)' both read as their words before the brackets."""
    return re.sub(r"\s*\([^)]*\)\s*$", "", text).strip().lower()


def load_catalog() -> dict:
    # The file has // comments on lines of their own, which json does not take.
    text = "\n".join(line for line in CATALOG.read_text(encoding="utf-8").splitlines()
                     if not line.lstrip().startswith("//"))
    catalog = json.loads(text)

    def table(name: str) -> dict[str, str]:
        found = {}
        for option in catalog[name]:
            found[option_key(option["key"])] = option["key"]
            found[option_key(option["name"])] = option["key"]
        return found

    return {
        "version": catalog["version"],
        "ratings": [r["key"] for r in catalog["ratings"]],
        "positions": table("positions"),
        "levels": table("levels"),
        "abilityCategories": table("abilityCategories"),
        "contractTypes": table("contractTypes"),
        "risks": table("risks"),
    }


# --- Reading the values ----------------------------------------------------------------------

TEXT = "text"


def as_rating(value) -> int | None | str:
    """1-10, or None when empty, or the reason it cannot be used."""
    if value is None:
        return None
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        if value == int(value) and 1 <= value <= 10:
            return int(value)
        return f"{value:g}"
    return TEXT


def as_option(value, table: dict[str, str]) -> str | None:
    if value is None:
        return None
    return table.get(option_key(str(value)), TEXT)


def as_yes_no(value) -> bool | None | str:
    if value is None:
        return None
    first = re.findall(r"[a-zæøå]+", str(value).lower())
    if first and first[0] in YES:
        return True
    if first and first[0] in NO:
        return False
    return TEXT


def as_date(value) -> str | None:
    if value is None:
        return None
    if isinstance(value, (int, float)):
        return (dt.date(1899, 12, 30) + dt.timedelta(days=int(value))).isoformat()
    for pattern in ("%Y-%m-%d", "%d.%m.%Y", "%d/%m/%Y"):
        try:
            return dt.datetime.strptime(str(value), pattern).date().isoformat()
        except ValueError:
            pass
    return TEXT


def problem(problems: list, workbook: dict, row: dict, column: str, value) -> None:
    # Text is never repeated: it is what the coaches wrote about a teenager.
    shown = "text" if value == TEXT else value
    problems.append((workbook["rater"], row["_row"], player_key(row), column, shown))


def assessment_of(workbook: dict, row: dict, catalog: dict, problems: list) -> dict | None:
    ratings = {}
    for key in catalog["ratings"]:
        value = as_rating(row.get(key))
        if isinstance(value, int):
            ratings[key] = value
        elif value is not None:
            problem(problems, workbook, row, key, value)

    if not ratings:
        return None

    found = {"rater": workbook["rater"], "ratings": ratings}

    for field, table in (("ratedAs", "levels"), ("abilityCategory", "abilityCategories"),
                         ("firstPosition", "positions"), ("secondPosition", "positions"),
                         ("thirdPosition", "positions"), ("successionRisk", "risks")):
        value = as_option(row.get(field), catalog[table])
        if value == TEXT:
            problem(problems, workbook, row, field, value)
            value = None
        found[field] = value

    readiness = as_rating(row.get("personalReadiness"))
    if readiness is not None and not isinstance(readiness, int):
        problem(problems, workbook, row, "personalReadiness", readiness)
        readiness = None
    found["personalReadiness"] = readiness

    for field in ("pathwayBlocked", "externalNeeded"):
        value = as_yes_no(row.get(field))
        if value == TEXT:
            problem(problems, workbook, row, field, value)
            value = None
        found[field] = value

    return found


# --- Matching --------------------------------------------------------------------------------


def words(name: str) -> list[str]:
    name = name.lower().replace("æ", "ae").replace("ø", "o").replace("å", "aa")
    name = unicodedata.normalize("NFKD", name).encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9]+", " ", name).split()


def player_key(row: dict) -> str:
    return f"{' '.join(str(row.get('last', '')).split())}, {' '.join(str(row.get('first', '')).split())}"


def distance(a: str, b: str) -> int:
    previous = list(range(len(b) + 1))
    for i, x in enumerate(a, 1):
        current = [i]
        for j, y in enumerate(b, 1):
            current.append(min(previous[j] + 1, current[j - 1] + 1, previous[j - 1] + (x != y)))
        previous = current
    return previous[-1]


def contained(short: list[str], long: list[str], close: bool) -> bool:
    """Every word of the shorter name has its own word in the longer one, alike or close."""
    rest = list(long)
    for word in short:
        allowed = (2 if len(word) >= 8 else 1) if close else 0
        best = min(rest, key=lambda other: distance(word, other), default=None)
        if best is None or distance(word, best) > allowed:
            return False
        rest.remove(best)
    return True


def tier(workbook_name: list[str], club_name: list[str]) -> str | None:
    if sorted(workbook_name) == sorted(club_name):
        return "exact"

    short, long = sorted((workbook_name, club_name), key=len)
    if len(short) < 2:
        return None
    if len(short) < len(long) and contained(short, long, close=False):
        return "partial"
    if contained(short, long, close=True):
        return "close"
    return None


TIERS = ["exact", "partial", "close"]


def load_squads(path: Path) -> dict:
    if not path.exists():
        raise Refused(f"{path} does not exist. Run scripts/squads/fetch_squads.py first: the "
                      "rows are matched against the squads the application has.")

    squads = json.loads(path.read_text(encoding="utf-8"))
    players = []
    for team in squads["teams"]:
        for p in team["players"]:
            players.append({
                "name": p["name"],
                "team": team["team"],
                "year": None if p.get("birthDateEstimated") else int(p["birthDate"][:4]),
                "words": words(p["name"]),
            })
    return {"fetchedOn": squads.get("fetchedOn"), "players": players}


def most_common(values: list):
    """The value most workbooks agree on, or None when there is no clear majority."""
    counted = Counter(v for v in values if v is not None)
    if not counted:
        return None, False
    (value, count), *rest = counted.most_common()
    return (value, False) if count * 2 > sum(counted.values()) else (None, True)


def match(people: dict[str, dict], squads: dict, decisions: dict[str, str | None]) -> None:
    by_name = {p["name"]: p for p in squads["players"]}

    for key, person in people.items():
        if key in decisions:
            chosen = decisions[key]
            if chosen is None:
                person.update(outcome="left out", reason="left out in decisions.json")
            elif chosen not in by_name:
                raise Refused(f"decisions.json: '{key}' is matched to '{chosen}', who is not in "
                              f"{DEFAULT_SQUADS.name}. Write the name as the club does.")
            else:
                person.update(outcome="decided", player=by_name[chosen], how="decisions.json")
            continue

        name = words(f"{person['first']} {person['last']}")
        candidates = []
        for club in squads["players"]:
            found = tier(name, club["words"])
            if found:
                candidates.append((TIERS.index(found), found, club))

        person["candidates"] = sorted(candidates, key=lambda c: c[0])

        if not candidates:
            person.update(outcome="unmatched", reason="no one in the squads by that name")
            continue

        best = min(c[0] for c in candidates)
        top = [c for c in candidates if c[0] == best]
        if len(top) > 1:
            person.update(outcome="ambiguous", reason=f"{len(top)} players fit equally well ({top[0][1]})")
            continue

        _, found, club = top[0]
        year = person["year"]
        same_year = year is not None and club["year"] is not None and year == club["year"]
        other_year = year is not None and club["year"] is not None and year != club["year"]

        if found == "exact" and not other_year:
            person.update(outcome="sure", player=club, how="exact")
        elif found == "exact":
            person.update(outcome="ambiguous",
                          reason=f"same name, but born {club['year']} on the club's page and {year} in the workbook")
        elif same_year:
            person.update(outcome="verified", player=club, how=f"{found} name, same year of birth on the club's page")
        else:
            why = "no year of birth to confirm it" if not other_year else \
                f"born {club['year']} on the club's page and {year} in the workbook"
            person.update(outcome="uncertain", reason=f"{found} name only, {why}")

    # Two rows for one player: neither is safe.
    claimed = Counter(p["player"]["name"] for p in people.values() if "player" in p)
    for person in people.values():
        if "player" in person and claimed[person["player"]["name"]] > 1:
            person.pop("player")
            person.update(outcome="ambiguous", reason="another row in the workbooks matches the same player")


# --- Putting it together ---------------------------------------------------------------------


def workbooks_in(paths: list[Path]) -> list[Path]:
    found = []
    for path in paths:
        if path.is_dir():
            found.extend(sorted(p for p in path.iterdir() if p.suffix.lower() == ".xlsx" and not p.name.startswith("~$")))
        elif path.suffix.lower() == ".xlsx":
            found.append(path)
        else:
            raise Refused(f"{path}: not a workbook or a folder.")
    if not found:
        raise Refused("no workbooks found.")
    return found


def load_decisions(path: Path) -> dict[str, str | None]:
    if not path.exists():
        return {}
    matches = json.loads(path.read_text(encoding="utf-8")).get("matches", {})
    return {" ".join(k.split()): v for k, v in matches.items()}


def run(args) -> int:
    catalog = load_catalog()
    squads = load_squads(args.squads)
    decisions = load_decisions(args.decisions or args.out / "decisions.json")

    workbooks = [read_workbook(p, args.rated_on) for p in workbooks_in(args.workbooks)]

    raters = Counter(w["rater"] for w in workbooks)
    twice = [r for r, n in raters.items() if n > 1]
    if twice:
        raise Refused(f"two workbooks for the same coach: {', '.join(twice)}. One workbook per coach.")

    # Every workbook has the same players. They are matched once, on what most workbooks say.
    people: dict[str, dict] = {}
    for workbook in workbooks:
        for row in workbook["rows"]:
            person = people.setdefault(player_key(row), {
                "last": " ".join(str(row.get("last", "")).split()),
                "first": " ".join(str(row.get("first", "")).split()),
                "years": [], "teams": [], "contracts": [], "groups": [], "ends": [], "rows": [],
            })
            person["years"].append(row.get("yearBorn") if isinstance(row.get("yearBorn"), int) else None)
            person["teams"].append(row.get("team"))
            person["contracts"].append(as_option(row.get("contractType"), catalog["contractTypes"]))
            person["groups"].append(as_option(row.get("trainingGroup"), catalog["levels"]))
            person["ends"].append(as_date(row.get("contractEndsOn")))
            person["rows"].append((workbook, row))

    problems: list = []
    conflicts: list = []
    for key, person in people.items():
        person["year"], _ = most_common(person["years"])
        person["team"], _ = most_common(person["teams"])
        for field, values in (("contractType", "contracts"), ("trainingGroup", "groups"), ("contractEndsOn", "ends")):
            value, split = most_common([v for v in person[values] if v != TEXT])
            person[field] = value
            if split:
                conflicts.append((key, field))

        person["assessments"] = []
        person["withoutRatings"] = 0
        for workbook, row in person["rows"]:
            assessment = assessment_of(workbook, row, catalog, problems)
            if assessment:
                person["assessments"].append(assessment)
            elif any(row.get(f) is not None for f in TOUCHED):
                person["withoutRatings"] += 1

    match(people, squads, decisions)

    imported = [p for p in people.values() if p["outcome"] in ("sure", "verified", "decided")]

    output = {
        "schemaVersion": SCHEMA_VERSION,
        "source": "IK Start Succession Planning",
        "createdOn": dt.date.today().isoformat(),
        "catalogVersion": catalog["version"],
        "squadsFetchedOn": squads["fetchedOn"],
        "raters": [{"key": w["rater"], "ratedOn": w["ratedOn"].isoformat()} for w in workbooks],
        "players": [
            {
                "name": p["player"]["name"],
                "contractType": p["contractType"],
                "contractEndsOn": p["contractEndsOn"],
                "trainingGroup": p["trainingGroup"],
                "assessments": p["assessments"],
            }
            for p in sorted(imported, key=lambda p: p["player"]["name"])
        ],
    }

    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "assessments.json").write_text(json.dumps(output, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (args.out / "report.md").write_text(report(workbooks, people, problems, conflicts, squads), encoding="utf-8")

    summary = Counter(p["outcome"] for p in people.values())
    rows = sum(len(w["rows"]) for w in workbooks)
    assessments = sum(len(p["assessments"]) for p in people.values())
    carried = sum(len(p["assessments"]) for p in imported)

    print(f"{len(workbooks)} workbooks, {rows} rows, {len(people)} players, {assessments} rows with ratings.")
    print("Players: " + ", ".join(f"{n} {o}" for o, n in summary.most_common()))
    print(f"Assessments to import: {carried} of {assessments}. Values left out: {len(problems)}.")

    if args.table:
        for p in people.values():
            if p["outcome"] not in ("sure", "verified", "decided"):
                print(f"  {p['outcome']:<10} {p['last']}, {p['first']}  ({p['reason']}; {len(p['assessments'])} assessments)")

    print(f"Wrote {args.out / 'assessments.json'} and {args.out / 'report.md'}")
    return 0


def report(workbooks, people, problems, conflicts, squads) -> str:
    outcomes = Counter(p["outcome"] for p in people.values())
    with_ratings = [p for p in people.values() if p["assessments"]]
    lines = [
        "# Succession planning: import report",
        "",
        f"Made {dt.date.today().isoformat()}, against the squads fetched {squads['fetchedOn']}. "
        "Git-ignored: it names players.",
        "",
        "## Workbooks",
        "",
        "| Coach | Saved | Rows | With ratings |",
        "| --- | --- | --- | --- |",
    ]
    for w in workbooks:
        rated = sum(1 for r in w["rows"] if any(isinstance(as_rating(r.get(k)), int) for k in
                                                ("physical", "technical", "tactical", "mental", "professionalism", "availability")))
        lines.append(f"| {w['rater']} | {w['ratedOn'].isoformat()} | {len(w['rows'])} | {rated} |")

    lines += ["", "## Players", ""]
    for outcome in ("sure", "verified", "decided", "uncertain", "ambiguous", "unmatched", "left out"):
        group = [p for p in people.values() if p["outcome"] == outcome]
        if not group:
            continue
        carried = sum(len(p["assessments"]) for p in group)
        lines.append(f"- **{outcome}**: {len(group)} players, {carried} assessments")

    lines += ["", "## Not imported", "",
              "| Workbook | Year | Team | Assessments | Why | Candidates |", "| --- | --- | --- | --- | --- | --- |"]
    for p in sorted(people.values(), key=lambda p: (-len(p["assessments"]), p["last"])):
        if p["outcome"] in ("sure", "verified", "decided"):
            continue
        candidates = "; ".join(f"{c['name']} ({c['team']}, {c['year'] or '?'}, {t})" for _, t, c in p.get("candidates", [])[:3]) or "–"
        lines.append(f"| {p['last']}, {p['first']} | {p['year'] or '?'} | {p['team'] or '?'} | "
                     f"{len(p['assessments'])} | {p['reason']} | {candidates} |")

    lines += ["", "## Matched on more than the name", "", "| Workbook | Club | How |", "| --- | --- | --- |"]
    for p in people.values():
        if p["outcome"] in ("verified", "decided"):
            lines.append(f"| {p['last']}, {p['first']} | {p['player']['name']} ({p['player']['team']}) | {p['how']} |")

    moved = [p for p in people.values() if "player" in p and TEAMS.get(str(p["team"]).lower()) not in (None, p["player"]["team"])]
    if moved:
        lines += ["", "## On another team than the workbook says", "",
                  "| Club | Workbook | Application |", "| --- | --- | --- |"]
        lines += [f"| {p['player']['name']} | {p['team']} | {p['player']['team']} |" for p in moved]

    if problems:
        lines += ["", "## Values left out", "",
                  "The value is shown only when it is a number. Text is never repeated here.", "",
                  "| Coach | Row | Workbook | Column | Value |", "| --- | --- | --- | --- | --- |"]
        lines += [f"| {r} | {row} | {who} | {col} | {val} |" for r, row, who, col, val in problems]

    if conflicts:
        lines += ["", "## Workbooks disagree on a fact", "",
                  "No value is imported where there is no clear majority.", ""]
        lines += [f"- {who}: {field}" for who, field in conflicts]

    without = sum(p["withoutRatings"] for p in people.values())
    lines += ["", f"Rows a coach filled in without any rating, and so not imported: {without}.",
              f"Players with at least one assessment: {len(with_ratings)}.", ""]
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("workbooks", nargs="*", type=Path, default=[DEFAULT_OUT / "workbooks"],
                        help="workbooks, or folders of them (default: Data/Succession/Import/workbooks)")
    parser.add_argument("--squads", type=Path, default=DEFAULT_SQUADS, help="squads.json to match against")
    parser.add_argument("--decisions", type=Path, help="decisions.json (default: in the output folder)")
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT, help="folder to write to")
    parser.add_argument("--rated-on", type=dt.date.fromisoformat,
                        help="the date the assessments were made, for all workbooks (default: when each was saved)")
    parser.add_argument("--table", action="store_true", help="print the rows that were not matched")
    args = parser.parse_args()

    try:
        return run(args)
    except Refused as refusal:
        print(f"Refused: {refusal}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
