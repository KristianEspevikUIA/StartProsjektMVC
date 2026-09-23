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
The ten markers on the Identity page, each with the file and the page it came from. Four are
the club's own; the other six stand in for club markers the report has no number for (see
'replaces' in gold-standard.json).

  possessionPct       Start's "Possession %", Match Statistics page.
  passCompletionPct   Start's "Pass Completion %", Match Statistics page.
  totalPasses         Start's "Total Passes", Match Statistics page.
  pressures           Start's "Pressures", Match Statistics page.
  pressureRegains     Start's "Pressure Regains", Match Statistics page.
  tackleSuccessPct    Start's "Tackles Won" / "Attempts", Match Statistics page, as a whole percentage.
  successfulDribbles  Sum of "Drib" over Start's players, Appendix: Defensive Actions / Other.
  interceptions       Sum of "I" over Start's players, same table.
  finalThirdPasses    Sum of "OP F3 Pass" over Start's players, Appendix: Passing and Touches.
  counterpresses      Sum of "Pressures" over Start's players, Pressure, Gegenpressing page.

Per player it keeps the name, Drib and I -- what the Player Highlight shows -- and nothing
else. The passing and pressure tables are read player by player to check them; only Start's
totals are kept.

Why it refuses rather than guesses
----------------------------------
Page numbers move between reports (Match Statistics is on page 3 or 4, the defensive table
anywhere from 22 to 24), a column can be clipped off the page, and Start is sometimes the
left column and sometimes the right. Each of those is checked against something else in the
same report, and any mismatch stops the run with the file named:

  * home/away order in the page header == order in the file name
  * team names above the Match Statistics columns == the page header
  * each defensive row: T+I == T + I, and T/DP% == T / (T + DP)   (catches a shifted column)
  * sum of T for Start == "Tackles Won", sum of T + DP == "Attempts" on Match Statistics
    (proves the player table is complete and is Start's)
  * Possession % home + away == 100, and Pass Completion % == completed / total passes
  * the passing table has exactly the defensive table's players; in each row OP F3 Pass <=
    OP Pass and OP PintoB <= PintoB; and OP Pass adds up to no more than Total Passes
  * each pressure row: Total Duration / Pressures == Duration Per Pressure, to the printed
    decimal, and Pressured Action Fails <= Pressures
  * Start's pressures by player add up to "Pressures" on Match Statistics
  * every counterpressing player is in that table, with at least as many pressures there
  * Pressure Regains <= Pressures

scripts/identity/derive_targets.py reads the opponents' side of the same reports, through
the same functions and checks, for the provisional ranges of the six stand-in markers.
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

# 2: the six stand-in markers. The application refuses a file in any other version, so a
# version 1 file is re-extracted rather than shown with six markers missing.
SCHEMA_VERSION = 2
SOURCE_FORMAT = "Statsbomb Match Report Version 1.1"

# The formulas are written into the JSON next to every value, and the page shows them. One
# wording, here, so the file and the page cannot describe the same number differently. In
# the Gold Standard's order.
FORMULAS = {
    "possessionPct": "Start's Possession % from the Match Statistics page.",
    "finalThirdPasses": (
        "Sum of OP F3 Pass (open-play final-third passes) over every Start player in "
        "Appendix, Player Stats: Passing and Touches."
    ),
    "totalPasses": (
        "Start's Total Passes from the Match Statistics page: every pass attempted, "
        "completed or not."
    ),
    "passCompletionPct": "Start's Pass Completion % from the Match Statistics page.",
    "successfulDribbles": (
        "Sum of Drib (dribbles past an opponent) over every Start player in "
        "Appendix, Player Stats: Defensive Actions / Other."
    ),
    "pressures": "Start's Pressures from the Match Statistics page.",
    "counterpresses": (
        "Sum of Pressures over every Start player on the Pressure, Gegenpressing page: "
        "pressures within five seconds of Start losing the ball."
    ),
    "tackleSuccessPct": (
        "Start's Tackles Won divided by Attempts on the Match Statistics page, as a whole "
        "percentage. Attempts are tackles won plus times dribbled past (T + DP in the appendix)."
    ),
    "pressureRegains": (
        "Start's Pressure Regains from the Match Statistics page: the ball won back within "
        "five seconds of a pressure."
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

# The passing table's columns. Each header is printed over up to three lines -- "OP F3 Pass"
# is "OP" / "F3" / "Pass" -- so it is checked as its bottom line plus the line above that.
# On the left-hand team, Pass% is clipped in some reports, to "Pass" or to nothing; the only
# column read is OP F3 Pass, the ninth, so the tenth is allowed to be either.
PASSING_COLUMNS = ["TB", "PIB", "PintoB", "OP PintoB", "TIB", "LB", "LB%", "OP Pass", "OP F3 Pass", "Pass%"]
PASSING_HEADER = ["TB", "PIB", "PintoB", "PintoB", "TIB", "LB", "LB%", "Pass", "Pass", "Pass%"]
PASSING_HEADER_ABOVE = ["OP", "OP", "F3"]
PASSING_READ_THROUGH = PASSING_COLUMNS.index("OP F3 Pass") + 1

# The two pressure tables -- every pressure, and those within five seconds of losing the ball
# -- have the same columns: "Duration Per Pressure" and "Pressured Action Fails" are split over
# two lines, and this is the bottom one.
PRESSURE_HEADER = ["Pressures", "Total", "Duration", "Pressure", "Action", "Fails"]
ALL_PRESSURES_PAGE = r"^PRESSURES, ALL BY PLAYER\b"
GEGENPRESSING_PAGE = r"^PRESSURE, GEGENPRESSING\b"


def slug(text: str) -> str:
    """'Vålerenga U17' -> 'valerenga-u17'. Used in match ids, which end up in URLs."""
    text = text.casefold().replace("æ", "ae").replace("ø", "o").replace("å", "a")
    text = unicodedata.normalize("NFKD", text).encode("ascii", "ignore").decode("ascii")
    return re.sub(r"[^a-z0-9]+", "-", text).strip("-")


def nfc(text: str) -> str:
    """macOS stores file names decomposed (A + combining ring); the PDF text is composed (Å).
    Without this, "Vålerenga" in the file name never equals "VÅLERENGA" in the header."""
    return unicodedata.normalize("NFC", text)


def whole_percent(part: int, whole: int) -> int:
    """part / whole as a whole percentage, halves rounded up: 1 of 8 is 13, as a reader would
    round 12.5 -- Python's round() would make it 12. Integer arithmetic, so no 12.4999..."""
    return (200 * part + whole) // (2 * whole)


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


def two_team_header(pdf: Path, page: Page, start: int, marker: str, what: str) -> tuple[int, int]:
    """The line naming the columns of a two-team player table: two "Player" and `marker`.
    Returns its index and where the right-hand team's half of the page begins."""
    lines = page.lines
    index = next(
        (i for i in range(start, len(lines))
         if len(re.findall(r"\bPlayer\b", lines[i])) == 2 and marker in lines[i]),
        None)
    if index is None:
        raise ExtractionError(f"{pdf.name}: page {page.number} has no two-team {what} header.")

    right_start = [m.start() for m in re.finditer(r"\bPlayer\b", lines[index])][1]
    return index, right_start


def side_of(line: str, right_start: int, is_home: bool) -> str:
    """The home (left) or the away (right) team's half of a line."""
    return line[:right_start] if is_home else line[right_start:]


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

    def value(self, label: str, is_home: bool) -> str:
        """The home (left) or the away (right) team's value on a row."""
        home, away = self.rows[label]
        return home if is_home else away


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

    required = [
        "Goals", "Possession %", "Pass Completion %", "Total Passes (Completed)",
        "Pressures", "Pressure Regains", "Tackles Won (Attempts)",
    ]
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


def parse_defensive_table(
    pdf: Path, pages: list[Page], is_home: bool, through: str = "Drib",
) -> tuple[Page, list[PlayerRow]]:
    """One team's rows of Defensive Actions / Other: the home (left) team's or the away's.

    `through` is the last column the caller reads, and the header is checked up to it.
    Columns after it may be clipped off the page -- Disp often is, and on the left-hand team
    even Drib can lose letters -- and are not read."""
    needed = DEFENSIVE_COLUMNS[:DEFENSIVE_COLUMNS.index(through) + 1]
    rows_by_page: dict[int, list[PlayerRow]] = {}
    source_page: dict[int, Page] = {}

    for page in pages:
        lines = page.lines
        section = [i for i, line in enumerate(lines) if "Defensive Actions / Other" in line]
        if not section:
            continue

        header_index, right_start = two_team_header(pdf, page, section[0], "T/DP%", "defensive")
        columns = side_of(lines[header_index], right_start, is_home).split()[1:]
        if columns[:len(needed)] != needed:
            raise ExtractionError(
                f"{pdf.name}: page {page.number} defensive columns are {columns}, expected {needed} (+ more).")

        rows: list[PlayerRow] = []
        for line in lines[header_index + 1:]:
            if "xG Chain" in line or "Statsbomb Match Report" in line or "Passing and Touches" in line:
                break
            for match in PLAYER_ROW.finditer(line):
                # Left rows start at the margin, right rows under the second "Player". Halfway
                # between the two is unambiguous.
                is_left = match.start("name") < right_start / 2
                if is_left != is_home:
                    continue
                numbers = [int(n) for n in match.group("numbers").split()]
                values = dict(zip(needed, numbers))
                rows.append(PlayerRow(name=match.group("name").strip(), values=values))
                check_player_row(pdf, page, rows[-1])

        if rows:
            rows_by_page[page.number] = rows
            source_page[page.number] = page

    if len(rows_by_page) != 1:
        raise ExtractionError(
            f"{pdf.name}: the defensive table should be on exactly one page, found {sorted(rows_by_page)}.")

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
# Appendix: Passing and Touches
# --------------------------------------------------------------------------------------------

# A player name and 9 or 10 integers: ten with Pass%, nine where it is clipped off the page.
PASSING_ROW = re.compile(r"(?P<name>[^\W\d_][^\d]*?)\s+(?P<numbers>(?:\d+\s+){8,9}\d+)(?=\s{2,}|\s*$)")


def parse_passing_table(pdf: Path, pages: list[Page], is_home: bool) -> tuple[Page, list[PlayerRow]]:
    """One team's rows of Passing and Touches, read through OP F3 Pass."""
    rows_by_page: dict[int, list[PlayerRow]] = {}
    source_page: dict[int, Page] = {}

    for page in pages:
        lines = page.lines
        section = [i for i, line in enumerate(lines) if "Passing and Touches" in line]
        if not section:
            continue

        header_index, right_start = two_team_header(pdf, page, section[0], "TB", "passing")
        columns = side_of(lines[header_index], right_start, is_home).split()[1:]
        # Exact up to OP F3 Pass. After it only Pass% can follow, whole or clipped ("Pass").
        clipped = columns[PASSING_READ_THROUGH:]
        if (columns[:PASSING_READ_THROUGH] != PASSING_HEADER[:PASSING_READ_THROUGH]
                or len(clipped) > 1
                or (clipped and not PASSING_HEADER[-1].startswith(clipped[0]))):
            raise ExtractionError(
                f"{pdf.name}: page {page.number} passing columns are {columns}, expected {PASSING_HEADER}.")

        # "OP" over OP PintoB and OP Pass, "F3" over OP F3 Pass: without this line, two
        # columns both printed "Pass" could be any two passing columns.
        above = next((line for line in reversed(lines[:header_index]) if line.strip()), "")
        if side_of(above, right_start, is_home).split() != PASSING_HEADER_ABOVE:
            raise ExtractionError(
                f"{pdf.name}: page {page.number} passing header does not have OP / OP / F3 over its columns.")

        rows: list[PlayerRow] = []
        for line in lines[header_index + 1:]:
            if ("Defensive Actions" in line or "xG Chain" in line or "Shots and Key-Passes" in line
                    or "Statsbomb Match Report" in line):
                break
            for match in PASSING_ROW.finditer(line):
                is_left = match.start("name") < right_start / 2
                if is_left != is_home:
                    continue
                numbers = [int(n) for n in match.group("numbers").split()]
                rows.append(PlayerRow(name=match.group("name").strip(), values=dict(zip(PASSING_COLUMNS, numbers))))
                check_passing_row(pdf, page, rows[-1])

        if rows:
            rows_by_page[page.number] = rows
            source_page[page.number] = page

    if len(rows_by_page) != 1:
        raise ExtractionError(
            f"{pdf.name}: the passing table should be on exactly one page, found {sorted(rows_by_page)}.")

    number, rows = next(iter(rows_by_page.items()))
    return source_page[number], rows


def check_passing_row(pdf: Path, page: Page, row: PlayerRow) -> None:
    v = row.values
    if v["OP F3 Pass"] > v["OP Pass"] or v["OP PintoB"] > v["PintoB"]:
        raise ExtractionError(
            f"{pdf.name} p{page.number}: {row.name} has OP F3 Pass {v['OP F3 Pass']} of OP Pass {v['OP Pass']} "
            f"and OP PintoB {v['OP PintoB']} of PintoB {v['PintoB']}: a part larger than its whole.")

    if any(v[column] > 100 for column in ("LB%", "Pass%") if column in v):
        raise ExtractionError(f"{pdf.name} p{page.number}: {row.name} has a percentage above 100.")


# --------------------------------------------------------------------------------------------
# Pressures, All by Player -- and Pressure, Gegenpressing
# --------------------------------------------------------------------------------------------

PRESSURE_ROW = re.compile(
    r"(?P<name>[^\W\d_][^\d]*?)\s+(?P<pressures>\d+)\s+(?P<total>\d+\.\d)\s+(?P<per>\d+\.\d)\s+(?P<fails>\d+)"
    r"(?=\s{2,}|\s*$)")


@dataclass
class PressureRow:
    name: str
    pressures: int
    total_duration: float
    per_pressure: float
    fails: int


def parse_pressure_table(pdf: Path, pages: list[Page], heading: str, is_home: bool) -> tuple[Page, list[PressureRow]]:
    """One team's rows of a pressure table. Players without a pressure are not listed."""
    page = find_page(pdf, pages, heading)
    lines = page.lines

    header_index, right_start = two_team_header(pdf, page, 0, "Pressures", "pressure")
    columns = side_of(lines[header_index], right_start, is_home).split()[1:]
    if columns != PRESSURE_HEADER:
        raise ExtractionError(
            f"{pdf.name}: page {page.number} pressure columns are {columns}, expected {PRESSURE_HEADER}.")

    rows: list[PressureRow] = []
    for line in lines[header_index + 1:]:
        if "Statsbomb Match Report" in line:
            break
        for match in PRESSURE_ROW.finditer(line):
            is_left = match.start("name") < right_start / 2
            if is_left != is_home:
                continue
            rows.append(PressureRow(
                name=match.group("name").strip(),
                pressures=int(match.group("pressures")),
                total_duration=float(match.group("total")),
                per_pressure=float(match.group("per")),
                fails=int(match.group("fails"))))
            check_pressure_row(pdf, page, rows[-1])

    names = [row.name for row in rows]
    if len(set(names)) != len(names):
        raise ExtractionError(f"{pdf.name}: page {page.number} lists a player twice, so its rows cannot be matched by name.")

    return page, rows


def check_pressure_row(pdf: Path, page: Page, row: PressureRow) -> None:
    if row.fails > row.pressures:
        raise ExtractionError(
            f"{pdf.name} p{page.number}: {row.name} has {row.fails} pressured action fails from {row.pressures} pressures.")

    # Both durations are printed to one decimal, so the quotient of the printed numbers can
    # miss the printed average by the rounding of each: 0.05 on the average, and 0.05 on the
    # total spread over the pressures. A shifted column misses by far more.
    tolerance = 0.05 + 0.05 / max(row.pressures, 1) + 1e-9
    average = row.total_duration / row.pressures if row.pressures else 0.0
    if abs(average - row.per_pressure) > tolerance:
        raise ExtractionError(
            f"{pdf.name} p{page.number}: {row.name} has {row.pressures} pressures over {row.total_duration} s, "
            f"printed as {row.per_pressure} s each.")


# --------------------------------------------------------------------------------------------
# One team's side of one report
# --------------------------------------------------------------------------------------------

@dataclass
class TeamMeasures:
    """Every value the page shows, for one team in one report, and the page each was read from."""
    values: dict[str, int]
    pages: dict[str, Page]
    players: list[PlayerRow]
    defensive_page: Page
    checks: dict


def measure(pdf: Path, pages: list[Page], stats: MatchStatistics, is_home: bool, *, dribbles: bool = True) -> TeamMeasures:
    """The home (left) or away (right) team's values, after every check in the module docstring
    that is not about the match as a whole.

    dribbles=False reads the defensive table only through T/DP% and leaves successfulDribbles
    out. That is for derive_targets.py, which reads the opponents' side: on the left-hand team
    the Drib column can be clipped, and no stand-in marker needs it."""

    def stat(label: str) -> str:
        return stats.value(label, is_home)

    pass_completion = int(stat("Pass Completion %"))
    total_passes, completed_passes = pair(stat("Total Passes (Completed)"))
    if total_passes and abs(round(100 * completed_passes / total_passes) - pass_completion) > 1:
        raise ExtractionError(
            f"{pdf.name}: Pass Completion {pass_completion}% does not match {completed_passes}/{total_passes}.")

    defensive_page, players = parse_defensive_table(pdf, pages, is_home, through="Drib" if dribbles else "T/DP%")
    tackles_won, tackle_attempts = pair(stat("Tackles Won (Attempts)"))
    sum_t = sum(p.values["T"] for p in players)
    sum_dp = sum(p.values["DP"] for p in players)
    if sum_t != tackles_won or sum_t + sum_dp != tackle_attempts:
        raise ExtractionError(
            f"{pdf.name}: appendix gives T={sum_t}, T+DP={sum_t + sum_dp}; Match Statistics says "
            f"{tackles_won} ({tackle_attempts}). The player table is incomplete or not this team's.")
    if tackle_attempts == 0:
        raise ExtractionError(f"{pdf.name}: no tackle attempts, so Tackle Success % has nothing to divide by.")

    # The defensive table is now proven complete, so a passing table with the same players is too.
    passing_page, passing = parse_passing_table(pdf, pages, is_home)
    if sorted(p.name for p in passing) != sorted(p.name for p in players):
        raise ExtractionError(
            f"{pdf.name}: the passing table on page {passing_page.number} does not list the same players "
            f"as the defensive table on page {defensive_page.number}.")
    open_play_passes = sum(p.values["OP Pass"] for p in passing)
    if open_play_passes > total_passes:
        raise ExtractionError(
            f"{pdf.name}: {open_play_passes} open-play passes by player, but {total_passes} passes in total.")

    pressures = int(stat("Pressures"))
    pressures_page, by_player = parse_pressure_table(pdf, pages, ALL_PRESSURES_PAGE, is_home)
    if sum(row.pressures for row in by_player) != pressures:
        raise ExtractionError(
            f"{pdf.name}: pressures by player on page {pressures_page.number} add up to "
            f"{sum(row.pressures for row in by_player)}; Match Statistics says {pressures}. "
            "The table is incomplete or not this team's.")

    gegenpressing_page, counterpressing = parse_pressure_table(pdf, pages, GEGENPRESSING_PAGE, is_home)
    all_pressures = {row.name: row.pressures for row in by_player}
    for row in counterpressing:
        if row.pressures > all_pressures.get(row.name, 0):
            raise ExtractionError(
                f"{pdf.name}: {row.name} has {row.pressures} counterpresses on page {gegenpressing_page.number} "
                f"but {all_pressures.get(row.name, 0)} pressures in all on page {pressures_page.number}.")

    pressure_regains = int(stat("Pressure Regains"))
    if pressure_regains > pressures:
        raise ExtractionError(f"{pdf.name}: {pressure_regains} pressure regains from {pressures} pressures.")

    values = {
        "possessionPct": int(stat("Possession %")),
        "finalThirdPasses": sum(p.values["OP F3 Pass"] for p in passing),
        "totalPasses": total_passes,
        "passCompletionPct": pass_completion,
        "pressures": pressures,
        "counterpresses": sum(row.pressures for row in counterpressing),
        "tackleSuccessPct": whole_percent(tackles_won, tackle_attempts),
        "pressureRegains": pressure_regains,
        "interceptions": sum(p.values["I"] for p in players),
    }
    if dribbles:
        values["successfulDribbles"] = sum(p.values["Drib"] for p in players)

    source_pages = {
        "finalThirdPasses": passing_page,
        "successfulDribbles": defensive_page,
        "counterpresses": gegenpressing_page,
        "interceptions": defensive_page,
    }

    return TeamMeasures(
        values=values,
        pages={key: source_pages.get(key, stats.page) for key in values},
        players=players,
        defensive_page=defensive_page,
        checks={
            "tacklesWon": [sum_t, tackles_won],
            "tackleAttempts": [sum_t + sum_dp, tackle_attempts],
            "pressures": [sum(row.pressures for row in by_player), pressures],
        },
    )


# --------------------------------------------------------------------------------------------
# One report
# --------------------------------------------------------------------------------------------

def extract_match(pdf: Path, team: str) -> dict:
    pages = read_pages(pdf)
    stats_page_hint = find_page(pdf, pages, r"^MATCH STATISTICS\s*$")
    header = parse_header(pdf, stats_page_hint, team)
    stats = parse_match_statistics(pdf, pages, header)

    home = header.start_is_home
    start_name = stats.home_name if home else stats.away_name
    opponent_name = stats.away_name if home else stats.home_name

    possession = int(stats.value("Possession %", home))
    opponent_possession = int(stats.value("Possession %", not home))
    if abs(possession + opponent_possession - 100) > 1:
        raise ExtractionError(f"{pdf.name}: possession {possession} + {opponent_possession} is not 100.")

    start = measure(pdf, pages, stats, home)

    def source(page: Page) -> dict:
        return {"file": nfc(pdf.name), "page": page.number}

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
            "start": int(stats.value("Goals", home)),
            "opponent": int(stats.value("Goals", not home)),
            "source": source(stats.page),
        },
        "metrics": {
            key: {"value": start.values[key], "source": source(start.pages[key]), "formula": formula}
            for key, formula in FORMULAS.items()
        },
        "players": [
            {"name": p.name, "successfulDribbles": p.values["Drib"], "interceptions": p.values["I"]}
            for p in start.players
        ],
        "playersSource": source(start.defensive_page),
        "_checks": {
            "startColumn": "home" if home else "away",
            "startName": start_name,
            **start.checks,
        },
    }


# --------------------------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------------------------

TABLE_COLUMNS = [
    ("Poss %", "possessionPct"), ("F3 passes", "finalThirdPasses"), ("Passes", "totalPasses"),
    ("Pass %", "passCompletionPct"), ("Dribbles", "successfulDribbles"), ("Pressures", "pressures"),
    ("Counterpr.", "counterpresses"), ("Tackle %", "tackleSuccessPct"), ("Regains", "pressureRegains"),
    ("Interceptions", "interceptions"),
]


def print_table(team: str, matches: list[dict]) -> None:
    print(f"\n### {team}\n")
    print("| Date | Match | Start | Score | " + " | ".join(label for label, _ in TABLE_COLUMNS) + " | Pages |")
    print("|---|---|---|---|" + "---|" * len(TABLE_COLUMNS) + "---|")
    for m in matches:
        metrics = m["metrics"]
        pages = " · ".join(
            f"p{page} ({', '.join(label for label, key in TABLE_COLUMNS if metrics[key]['source']['page'] == page)})"
            for page in sorted({metric["source"]["page"] for metric in metrics.values()}))
        print(
            f"| {m['date']} | {m['homeTeam']} – {m['awayTeam']} | {'home' if m['startIsHome'] else 'away'} "
            f"| {m['goals']['start']}–{m['goals']['opponent']} | "
            + " | ".join(str(metrics[key]["value"]) for _, key in TABLE_COLUMNS)
            + f" | {pages} |")


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
