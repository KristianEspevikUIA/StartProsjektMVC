#!/usr/bin/env python3
"""
Extracts IK Start's identity markers from StatsBomb Match Report (v1.1) PDFs.

    python3 scripts/identity/extract_stats.py --team U14 --source "<folder with the PDFs>"
    python3 scripts/identity/extract_stats.py --team U14 --source "<folder>" --table

Writes StartPraksisGruppe3Prosjekt/Data/Identity/Matches/<team>.json, which the Identity
Benchmarking page reads. That folder is git-ignored ON PURPOSE: the reports name every
player, most of them minors, and the repository is public. See docs/identity-benchmarking.md.

Needs Python 3.9+ and poppler (pdftotext, pdfinfo). Nothing else.

What it takes from each report, and nothing more
------------------------------------------------
Only the four markers the report can actually answer, each with the file and the page it
came from:

  possessionPct       Start's "Possession %", Match Statistics page.
  passCompletionPct   Start's "Pass Completion %", Match Statistics page.
  successfulDribbles  Sum of "Drib" over Start's players, Appendix: Defensive Actions / Other.
  interceptions       Sum of "I" over Start's players, same table.

Per player it keeps the name, Drib and I -- what the Player Highlight shows -- and nothing
else. The other six Gold Standard markers have no basis in the report and are not
estimated here or anywhere else.

Why it refuses rather than guesses
----------------------------------
Page numbers move between reports (Match Statistics is on page 3 or 4, the defensive table
anywhere from 22 to 24), a column can be clipped off the page, and Start is sometimes the
left column and sometimes the right. Each of those is checked against something else in the
same report, and any mismatch stops the run with the file named:

  * home/away order in the page header == order in the file name
  * team names above the Match Statistics columns == the page header
  * each player row: T+I == T + I, and T/DP% == T / (T + DP)   (catches a shifted column)
  * sum of T for Start == "Tackles Won", sum of T + DP == "Attempts" on Match Statistics
    (proves the player table is complete and is Start's)
  * Possession % home + away == 100, and Pass Completion % == completed / total passes
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import unicodedata
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUTPUT_DIR = REPO_ROOT / "StartPraksisGruppe3Prosjekt" / "Data" / "Identity" / "Matches"

SCHEMA_VERSION = 1
SOURCE_FORMAT = "Statsbomb Match Report Version 1.1"

# The formulas are written into the JSON next to every value, and the page shows them. One
# wording, here, so the file and the page cannot describe the same number differently.
FORMULAS = {
    "possessionPct": "Start's Possession % from the Match Statistics page.",
    "passCompletionPct": "Start's Pass Completion % from the Match Statistics page.",
    "successfulDribbles": (
        "Sum of Drib (dribbles past an opponent) over every Start player in "
        "Appendix, Player Stats: Defensive Actions / Other."
    ),
    "interceptions": (
        "Sum of I (interceptions) over every Start player in "
        "Appendix, Player Stats: Defensive Actions / Other."
    ),
}

# The defensive table's columns, in order, as they appear under the "Player" header. "Win" is
# "Aer Win" -- "Aer" sits on the line above. A twelfth column, Disp, is often clipped off the
# page in the PDF itself, so it is allowed to be missing and is never read.
DEFENSIVE_COLUMNS = ["T", "I", "T+I", "DP", "T/DP%", "Fouls", "Clear", "Win", "Aer%", "Drib"]


def slug(text: str) -> str:
    """'Vålerenga U17' -> 'valerenga-u17'. Used in match ids, which end up in URLs."""
    text = text.casefold().replace("æ", "ae").replace("ø", "o").replace("å", "a")
    text = unicodedata.normalize("NFKD", text).encode("ascii", "ignore").decode("ascii")
    return re.sub(r"[^a-z0-9]+", "-", text).strip("-")


def nfc(text: str) -> str:
    """macOS stores file names decomposed (A + combining ring); the PDF text is composed (Å).
    Without this, "Vålerenga" in the file name never equals "VÅLERENGA" in the header."""
    return unicodedata.normalize("NFC", text)


class ExtractionError(Exception):
    """A report that does not add up. Always names the file."""


@dataclass
class Page:
    number: int
    text: str

    @property
    def lines(self) -> list[str]:
        return self.text.splitlines()


# --------------------------------------------------------------------------------------------
# PDF access
# --------------------------------------------------------------------------------------------

def read_pages(pdf: Path) -> list[Page]:
    """Every page as layout-preserving text. pdftotext separates pages with a form feed."""
    try:
        output = subprocess.run(
            ["pdftotext", "-layout", str(pdf), "-"],
            capture_output=True, text=True, check=True,
        ).stdout
    except FileNotFoundError:
        sys.exit("pdftotext was not found. Install poppler (macOS: brew install poppler).")

    chunks = nfc(output).split("\f")
    # A trailing form feed leaves an empty last chunk; it is not a page.
    if chunks and not chunks[-1].strip():
        chunks = chunks[:-1]

    pages = [Page(number=index + 1, text=chunk) for index, chunk in enumerate(chunks)]

    for page in pages:
        # The footer prints its own page number when the page is narrow enough to fit it.
        # Where it does, it has to agree with the position -- the page number is what the UI
        # sends a coach to look up.
        footer = re.search(r"Statsbomb Match Report Version 1\.1\s+page (\d+)\s*$", page.text.rstrip(), re.M)
        if footer and int(footer.group(1)) != page.number:
            raise ExtractionError(
                f"{pdf.name}: page {page.number} is printed as page {footer.group(1)}.")

    return pages


def find_page(pdf: Path, pages: list[Page], pattern: str) -> Page:
    matches = [page for page in pages if re.search(pattern, page.text, re.M)]
    if len(matches) != 1:
        raise ExtractionError(
            f"{pdf.name}: expected exactly one page matching /{pattern}/, found {len(matches)}.")
    return matches[0]


# --------------------------------------------------------------------------------------------
# Header: teams, date, which side is Start
# --------------------------------------------------------------------------------------------

@dataclass
class MatchHeader:
    home: str
    away: str
    date: str
    competition: str
    start_is_home: bool


def parse_header(pdf: Path, page: Page, team: str) -> MatchHeader:
    header = re.search(r"MATCH REPORT\s+(.+? U\d{2})\s+V\s+(.+? U\d{2})\s*$", page.text, re.M)
    date = re.search(r"^\s*(\S.*?)\s+(\d{4}-\d{2}-\d{2})\s*$", page.text, re.M)
    if not header or not date:
        raise ExtractionError(f"{pdf.name}: no 'MATCH REPORT <home> V <away>' header on page {page.number}.")

    home_upper, away_upper = header.group(1).strip(), header.group(2).strip()

    # The same two names, in the same order, must be in the file name. This is the check that
    # stops a home and an away report being read the wrong way round.
    stem = nfc(pdf.stem).casefold()
    joined = f"{home_upper} {away_upper}".casefold()
    position = stem.find(joined)
    if position < 0:
        raise ExtractionError(
            f"{pdf.name}: header says '{home_upper} V {away_upper}', which is not the order in the file name.")

    competition = nfc(pdf.stem)[:position].strip()

    start_name = f"START {team}".casefold()
    home_is_start = home_upper.casefold() == start_name
    away_is_start = away_upper.casefold() == start_name
    if home_is_start == away_is_start:
        raise ExtractionError(
            f"{pdf.name}: expected 'Start {team}' as exactly one of the two teams, "
            f"got '{home_upper}' and '{away_upper}'.")

    return MatchHeader(
        home=home_upper,
        away=away_upper,
        date=date.group(2),
        competition=competition,
        start_is_home=home_is_start,
    )


# --------------------------------------------------------------------------------------------
# Match Statistics page
# --------------------------------------------------------------------------------------------

STAT_VALUE = r"(?:\d+(?:\.\d+)?(?:\s*\(\d+\))?|\d+\s*/\s*\d+)"
STAT_ROW = re.compile(rf"^\s*(?P<home>{STAT_VALUE})\s{{2,}}(?P<label>\S.*?\S)\s{{2,}}(?P<away>{STAT_VALUE})\s*$")


@dataclass
class MatchStatistics:
    page: Page
    home_name: str
    away_name: str
    rows: dict[str, tuple[str, str]]

    def start_value(self, label: str, start_is_home: bool) -> str:
        home, away = self.rows[label]
        return home if start_is_home else away

    def opponent_value(self, label: str, start_is_home: bool) -> str:
        home, away = self.rows[label]
        return away if start_is_home else home


def parse_match_statistics(pdf: Path, pages: list[Page], header: MatchHeader) -> MatchStatistics:
    page = find_page(pdf, pages, r"^MATCH STATISTICS\s*$")
    lines = page.lines

    rows: dict[str, tuple[str, str]] = {}
    goals_index = None
    for index, line in enumerate(lines):
        match = STAT_ROW.match(line)
        if match:
            rows[match.group("label")] = (match.group("home"), match.group("away"))
            if match.group("label") == "Goals":
                goals_index = index

    required = ["Goals", "Possession %", "Pass Completion %", "Total Passes (Completed)", "Tackles Won (Attempts)"]
    missing = [label for label in required if label not in rows]
    if missing or goals_index is None:
        raise ExtractionError(f"{pdf.name}: Match Statistics (page {page.number}) is missing {missing}.")

    # The line above "Goals" names the two columns. It has to agree with the header.
    names_line = next(line for line in reversed(lines[:goals_index]) if line.strip())
    names = re.split(r"\s{2,}", names_line.strip())
    if len(names) != 2 or [n.casefold() for n in names] != [header.home.casefold(), header.away.casefold()]:
        raise ExtractionError(
            f"{pdf.name}: Match Statistics columns are {names}, header says '{header.home}' / '{header.away}'.")

    return MatchStatistics(page=page, home_name=names[0], away_name=names[1], rows=rows)


def pair(value: str) -> tuple[int, int]:
    """'252 (183)' -> (252, 183)."""
    match = re.fullmatch(r"(\d+)\s*\((\d+)\)", value.strip())
    if not match:
        raise ValueError(value)
    return int(match.group(1)), int(match.group(2))


# --------------------------------------------------------------------------------------------
# Appendix: Defensive Actions / Other
# --------------------------------------------------------------------------------------------

# A player name followed by 10 or 11 integers. Names never contain digits; every column in
# this table is an integer. Stops at two spaces or end of line, so the left and the right
# team's rows on the same line come out as two matches.
PLAYER_ROW = re.compile(r"(?P<name>[^\W\d_][^\d]*?)\s+(?P<numbers>(?:\d+\s+){9,10}\d+)(?=\s{2,}|\s*$)")


@dataclass
class PlayerRow:
    name: str
    values: dict[str, int]


def parse_defensive_table(pdf: Path, pages: list[Page], start_is_home: bool) -> tuple[Page, list[PlayerRow]]:
    rows_by_page: dict[int, list[PlayerRow]] = {}
    source_page: dict[int, Page] = {}

    for page in pages:
        lines = page.lines
        section = [i for i, line in enumerate(lines) if "Defensive Actions / Other" in line]
        if not section:
            continue

        header_index = next(
            (i for i in range(section[0], len(lines))
             if len(re.findall(r"\bPlayer\b", lines[i])) == 2 and "Drib" in lines[i]),
            None)
        if header_index is None:
            raise ExtractionError(f"{pdf.name}: page {page.number} has no two-team defensive header.")

        header = lines[header_index]
        right_start = [m.start() for m in re.finditer(r"\bPlayer\b", header)][1]
        side_header = header[:right_start] if start_is_home else header[right_start:]
        columns = side_header.split()[1:]
        if columns[:len(DEFENSIVE_COLUMNS)] != DEFENSIVE_COLUMNS:
            raise ExtractionError(
                f"{pdf.name}: page {page.number} defensive columns are {columns}, expected {DEFENSIVE_COLUMNS} (+ Disp).")

        rows: list[PlayerRow] = []
        for line in lines[header_index + 1:]:
            if "xG Chain" in line or "Statsbomb Match Report" in line or "Passing and Touches" in line:
                break
            for match in PLAYER_ROW.finditer(line):
                # Left rows start at the margin, right rows under the second "Player". Halfway
                # between the two is unambiguous.
                is_left = match.start("name") < right_start / 2
                if is_left != start_is_home:
                    continue
                numbers = [int(n) for n in match.group("numbers").split()]
                values = dict(zip(DEFENSIVE_COLUMNS, numbers))
                rows.append(PlayerRow(name=match.group("name").strip(), values=values))
                check_player_row(pdf, page, rows[-1])

        if rows:
            rows_by_page[page.number] = rows
            source_page[page.number] = page

    if len(rows_by_page) != 1:
        raise ExtractionError(
            f"{pdf.name}: Start's defensive table should be on exactly one page, found {sorted(rows_by_page)}.")

    number, rows = next(iter(rows_by_page.items()))
    return source_page[number], rows


def check_player_row(pdf: Path, page: Page, row: PlayerRow) -> None:
    v = row.values
    if v["T+I"] != v["T"] + v["I"]:
        raise ExtractionError(f"{pdf.name} p{page.number}: {row.name} has T+I {v['T+I']} != {v['T']} + {v['I']}.")

    challenges = v["T"] + v["DP"]
    expected = round(100 * v["T"] / challenges) if challenges else 0
    if abs(expected - v["T/DP%"]) > 1:
        raise ExtractionError(
            f"{pdf.name} p{page.number}: {row.name} has T/DP% {v['T/DP%']}, T={v['T']} DP={v['DP']} gives {expected}.")


# --------------------------------------------------------------------------------------------
# One report
# --------------------------------------------------------------------------------------------

def extract_match(pdf: Path, team: str) -> dict:
    pages = read_pages(pdf)
    stats_page_hint = find_page(pdf, pages, r"^MATCH STATISTICS\s*$")
    header = parse_header(pdf, stats_page_hint, team)
    stats = parse_match_statistics(pdf, pages, header)
    defensive_page, players = parse_defensive_table(pdf, pages, header.start_is_home)

    home = header.start_is_home
    start_name = stats.home_name if home else stats.away_name
    opponent_name = stats.away_name if home else stats.home_name

    possession = int(stats.start_value("Possession %", home))
    opponent_possession = int(stats.opponent_value("Possession %", home))
    if abs(possession + opponent_possession - 100) > 1:
        raise ExtractionError(f"{pdf.name}: possession {possession} + {opponent_possession} is not 100.")

    pass_completion = int(stats.start_value("Pass Completion %", home))
    total_passes, completed_passes = pair(stats.start_value("Total Passes (Completed)", home))
    if total_passes and abs(round(100 * completed_passes / total_passes) - pass_completion) > 1:
        raise ExtractionError(
            f"{pdf.name}: Pass Completion {pass_completion}% does not match {completed_passes}/{total_passes}.")

    tackles_won, tackle_attempts = pair(stats.start_value("Tackles Won (Attempts)", home))
    sum_t = sum(p.values["T"] for p in players)
    sum_dp = sum(p.values["DP"] for p in players)
    if sum_t != tackles_won or sum_t + sum_dp != tackle_attempts:
        raise ExtractionError(
            f"{pdf.name}: appendix gives T={sum_t}, T+DP={sum_t + sum_dp}; Match Statistics says "
            f"{tackles_won} ({tackle_attempts}). The player table is incomplete or not Start's.")

    def source(page: Page) -> dict:
        return {"file": nfc(pdf.name), "page": page.number}

    def metric(key: str, value: int, page: Page) -> dict:
        return {"value": value, "source": source(page), "formula": FORMULAS[key]}

    opponent_slug = slug(opponent_name)

    return {
        "id": f"{header.date}-{'home' if home else 'away'}-{opponent_slug}",
        "date": header.date,
        "competition": header.competition,
        "file": nfc(pdf.name),
        "homeTeam": stats.home_name,
        "awayTeam": stats.away_name,
        "startIsHome": home,
        "goals": {
            "start": int(stats.start_value("Goals", home)),
            "opponent": int(stats.opponent_value("Goals", home)),
            "source": source(stats.page),
        },
        "metrics": {
            "possessionPct": metric("possessionPct", possession, stats.page),
            "passCompletionPct": metric("passCompletionPct", pass_completion, stats.page),
            "successfulDribbles": metric("successfulDribbles", sum(p.values["Drib"] for p in players), defensive_page),
            "interceptions": metric("interceptions", sum(p.values["I"] for p in players), defensive_page),
        },
        "players": [
            {"name": p.name, "successfulDribbles": p.values["Drib"], "interceptions": p.values["I"]}
            for p in players
        ],
        "playersSource": source(defensive_page),
        "_checks": {
            "startColumn": "home" if home else "away",
            "startName": start_name,
            "tacklesWon": [sum_t, tackles_won],
            "tackleAttempts": [sum_t + sum_dp, tackle_attempts],
        },
    }


# --------------------------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------------------------

def print_table(team: str, matches: list[dict]) -> None:
    print(f"\n### {team}\n")
    print("| Date | Match | Start | Score | Poss % | Pass % | Dribbles | Interceptions | Sources |")
    print("|---|---|---|---|---|---|---|---|---|")
    for m in matches:
        metrics = m["metrics"]
        pages = f"p{metrics['possessionPct']['source']['page']} (poss, pass) · p{m['playersSource']['page']} (drib, I)"
        print(
            f"| {m['date']} | {m['homeTeam']} – {m['awayTeam']} | {'home' if m['startIsHome'] else 'away'} "
            f"| {m['goals']['start']}–{m['goals']['opponent']} "
            f"| {metrics['possessionPct']['value']} | {metrics['passCompletionPct']['value']} "
            f"| {metrics['successfulDribbles']['value']} | {metrics['interceptions']['value']} | {pages} |")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--team", required=True, help="U14, U15 or U17")
    parser.add_argument("--source", required=True, type=Path, help="Folder with the StatsBomb match report PDFs")
    parser.add_argument("--out", type=Path, help="Output file (default: Data/Identity/Matches/<team>.json)")
    parser.add_argument("--table", action="store_true", help="Also print a Markdown table for checking")
    args = parser.parse_args()

    team = args.team.upper()
    if not re.fullmatch(r"U\d{2}", team):
        parser.error("--team must look like U14")

    # Only match reports. The Gold Standard documents can sit in the same folder.
    pdfs = sorted(p for p in args.source.expanduser().glob("*.pdf") if p.name.startswith("OBOS "))
    if not pdfs:
        sys.exit(f"No match reports (OBOS *.pdf) in {args.source}")

    try:
        matches = [extract_match(pdf, team) for pdf in pdfs]
    except ExtractionError as error:
        sys.exit(f"Stopped: {error}")

    ids = [m["id"] for m in matches]
    if len(set(ids)) != len(ids):
        sys.exit(f"Stopped: two reports describe the same match: {ids}")

    matches.sort(key=lambda m: m["date"])

    document = {
        "schemaVersion": SCHEMA_VERSION,
        "team": team,
        "teamName": matches[0]["_checks"]["startName"],
        "generatedAt": datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
        "generatedBy": "scripts/identity/extract_stats.py",
        "sourceFormat": SOURCE_FORMAT,
        "matches": matches,
    }
    for match in matches:
        del match["_checks"]

    out = args.out or DEFAULT_OUTPUT_DIR / f"{team.lower()}.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    print(f"{team}: {len(matches)} matches -> {out}")
    if args.table:
        print_table(team, matches)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
