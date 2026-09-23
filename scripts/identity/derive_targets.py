#!/usr/bin/env python3
"""
Derives the provisional ranges of the stand-in markers from Start's opponents.

    python3 scripts/identity/derive_targets.py --source "<folder>" --source "<folder>" ...

The club's Gold Standard has no range for the markers that stand in for the ones a StatsBomb
match report cannot measure ('replaces' in gold-standard.json). Until the club sets its own,
their ranges follow the Gold Standard's own method -- "consistent team averages (the floor of
performance) with peak match data (the ceiling)" -- applied to the teams in the reports that
are not Start:

  floor    the opponents' average per match, rounded half up to a whole number
  ceiling  the best single match by any opponent

Every folder given is read together, so one run over the U14, U15 and U17 folders gives one
range for all three, as the club's own ranges are. The per-age averages are printed as well,
for whoever sets the real ones.

The opponents' side of each report is read by extract_stats.measure(), with the same checks as
Start's side. Nothing is written: it prints each range and the values to put in
gold-standard.json. See docs/identity-benchmarking.md.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import defaultdict
from dataclasses import dataclass
from datetime import date
from pathlib import Path

import extract_stats as ex

GOLD_STANDARD = ex.REPO_ROOT / "StartPraksisGruppe3Prosjekt" / "Data" / "Identity" / "gold-standard.json"


@dataclass
class Observation:
    """One opponent's value in one report."""
    value: int
    team: str
    age: str
    date: str
    file: str
    page: int


def start_age(pdf: Path, page: ex.Page) -> str:
    """'U14', from whichever side of the header is 'START U14'."""
    header = re.search(r"MATCH REPORT\s+(.+? U\d{2})\s+V\s+(.+? U\d{2})\s*$", page.text, re.M)
    for name in header.groups() if header else ():
        found = re.fullmatch(r"START (U\d{2})", name.strip(), re.I)
        if found:
            return found.group(1).upper()
    raise ex.ExtractionError(f"{pdf.name}: no 'START Uxx' in the header on page {page.number}.")


def opponent(pdf: Path) -> tuple[str, str, str, ex.TeamMeasures]:
    """The opponent's name, Start's age group, the date, and the opponent's side of the report."""
    pages = ex.read_pages(pdf)
    stats_page = ex.find_page(pdf, pages, r"^MATCH STATISTICS\s*$")
    age = start_age(pdf, stats_page)
    header = ex.parse_header(pdf, stats_page, age)
    stats = ex.parse_match_statistics(pdf, pages, header)

    opponent_is_home = not header.start_is_home
    name = stats.home_name if opponent_is_home else stats.away_name
    return name, age, header.date, ex.measure(pdf, pages, stats, opponent_is_home, dribbles=False)


def day(iso: str) -> str:
    """'2026-08-23' -> '23 Aug 2026', as the Identity page prints dates."""
    parsed = date.fromisoformat(iso)
    return f"{parsed.day} {parsed.strftime('%b %Y')}"


def number(value: float, unit: str) -> str:
    return f"{value:g}{unit}"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--source", required=True, action="append", type=Path,
                        help="Folder with StatsBomb match report PDFs; give it once per age group")
    parser.add_argument("--gold-standard", type=Path, default=GOLD_STANDARD, help="gold-standard.json to read the stand-ins from")
    args = parser.parse_args()

    standard = json.loads(args.gold_standard.read_text(encoding="utf-8"))
    stand_ins = [m for phase in standard["phases"] for m in phase["markers"] if m.get("replaces")]
    if not stand_ins:
        sys.exit(f"No marker in {args.gold_standard} has 'replaces'; there is nothing to derive.")

    pdfs = sorted(p for folder in args.source for p in folder.expanduser().glob("*.pdf") if p.name.startswith("OBOS "))
    if not pdfs:
        sys.exit("No match reports (OBOS *.pdf) in the folders given.")

    observations: dict[str, list[Observation]] = defaultdict(list)
    seen: set[tuple[str, str]] = set()
    try:
        for pdf in pdfs:
            name, age, played, side = opponent(pdf)
            if (played, name) in seen:
                sys.exit(f"Stopped: {pdf.name} is a second report of {name} on {played}.")
            seen.add((played, name))
            for marker in stand_ins:
                metric = marker["measurement"]["metric"]
                observations[metric].append(Observation(
                    side.values[metric], name, age, played, ex.nfc(pdf.name), side.pages[metric].number))
    except ex.ExtractionError as error:
        sys.exit(f"Stopped: {error}")

    dates = sorted(played for played, _ in seen)
    ages = sorted({o.age for values in observations.values() for o in values})
    first, last = day(dates[0]), day(dates[-1])
    if first[-4:] == last[-4:]:
        first = first[:-5]                                  # "1 Apr – 29 Aug 2026"
    scope = f"{len(pdfs)} StatsBomb reports ({', '.join(ages)}; {first} – {last})"
    print(f"Provisional ranges from Start's opponents in {scope}.\n")

    for marker in stand_ins:
        metric = marker["measurement"]["metric"]
        unit = marker["target"].get("unit", "")
        values = observations[metric]

        total, count = sum(o.value for o in values), len(values)
        floor = (2 * total + count) // (2 * count)          # the average, halves rounded up
        ceiling = max(o.value for o in values)
        best = sorted((o for o in values if o.value == ceiling), key=lambda o: o.date)

        by_age = " · ".join(
            f"{age} {sum(o.value for o in values if o.age == age) / sum(1 for o in values if o.age == age):.1f}"
            f" ({sum(1 for o in values if o.age == age)})"
            for age in ages)

        print(f"{marker['name']} ({metric}), standing in for {marker['replaces']['marker']['name']}")
        print(f"  opponents' average {total / count:.1f} over {count} matches -> floor {number(floor, unit)}")
        for o in best:
            print(f"  best single match {number(ceiling, unit)}: {o.team}, {o.date} ({o.file}, page {o.page})")
        print(f"  opponents' average by age group: {by_age}")

        if marker["target"]["direction"] != "higher-is-better":
            print("  !! lower is better here: an average floor and a peak ceiling do not apply. Set it by hand.\n")
            continue
        if floor >= ceiling:
            print("  !! the floor is not below the ceiling; the catalog would refuse this range.\n")
            continue

        who = " / ".join(dict.fromkeys(o.team for o in best))
        when = " and ".join(day(o.date) for o in best)
        range_text = f"{floor}% – {ceiling}%" if unit == "%" else f"{floor} – {ceiling} / match"
        snippet = {
            "eliteRange": range_text,
            "bestAtIt": f"{who} (peak {number(ceiling, unit)})",
            "target": {
                "direction": "higher-is-better",
                "min": floor,
                "max": ceiling,
                **({"unit": unit} if unit else {}),
                "display": f"{floor}–{ceiling}{unit}",
                "provisional": True,
                "basis": (
                    f"Start's opponents in {scope}: their average, {number(round(total / count, 1), unit)}, "
                    f"up to their best single match, {number(ceiling, unit)} by {who} on {when}."
                ),
            },
        }
        for key, value in snippet.items():
            print(f"    {json.dumps(key)}: {json.dumps(value, ensure_ascii=False)},")
        print()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
