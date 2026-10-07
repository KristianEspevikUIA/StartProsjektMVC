#!/usr/bin/env python3
"""
Fetches IK Start's U14, U15 and U17 squads from the club's own player pages on ikstart.no.

    python3 scripts/squads/fetch_squads.py
    python3 scripts/squads/fetch_squads.py --table

Writes StartPraksisGruppe3Prosjekt/Data/Squads/squads.json and one photo per player in
StartPraksisGruppe3Prosjekt/Data/Squads/photos/. The application reads them when it seeds in
Development (Data/SeedSquads.cs). That folder is git-ignored ON PURPOSE: it names every
player, all of them minors, with a photo and a date of birth, and the repository is public.
IK Start has given permission for the names and photos to be used in the app -- not for them
to be published anywhere else. See docs/player-welcome.md.

Needs Python 3.9+. Nothing else.

What it takes from each player, and nothing more
------------------------------------------------
  name        As the club writes it, with runs of spaces collapsed.
  position    The line the club lists the player under, in the app's words: Goalkeeper,
              Defender, Midfielder or Forward. The pages say no more than that.
  birthDate   The club's "Født". Two players on U17 have none; they get 1 January of their age
              group's year, and birthDateEstimated says so.
  photo       The club's squad photo, at 400 pixels wide -- a welcome circle needs no more.

Nationality and shirt number are on some of the cards and are left out: nothing in the app
uses them.

Why it refuses rather than guesses
----------------------------------
A page that lists no players, a player without a name, a position the mapping does not know,
a date it cannot read, or a photo that is not a JPEG, PNG or WebP stops the run with the page
named. An empty or half-read squad would otherwise replace the real one the next time the
application seeds.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import re
import sys
import time
import unicodedata
import urllib.request
from html.parser import HTMLParser
from pathlib import Path

BASE = "https://www.ikstart.no"

# The app's team name, the club's page, and the birth year the age group is named after this
# season (U17 in 2026 is born 2009). The year is only for a player the club gives no date for.
TEAMS = [
    ("U14", "/lag/start-g14/spillere", 14),
    ("U15", "/lag/start-g15-nasjonal/spillere", 15),
    ("U17", "/lag/start-g17-nasjonal/spillere", 17),
]

POSITIONS = {
    "Keeper": "Goalkeeper",
    "Forsvarsspiller": "Defender",
    "Midtbanespiller": "Midfielder",
    "Angrepsspiller": "Forward",
}

# The headings the players stand under. Anything else on the page -- "Støtteapparat", the
# coaches -- is not a player and is skipped.
PLAYER_SECTIONS = {"Keepere", "Forsvarsspillere", "Midtbanespillere", "Angrepsspillere"}

MONTHS = {
    "jan": 1, "feb": 2, "mar": 3, "apr": 4, "mai": 5, "jun": 6,
    "jul": 7, "aug": 8, "sep": 9, "okt": 10, "nov": 11, "des": 12,
}

PHOTO_WIDTH = 400

USER_AGENT = "StartCompass squad import (IS-302, UiA; with IK Start's permission)"

REPO = Path(__file__).resolve().parents[2]
DEFAULT_OUT = REPO / "StartPraksisGruppe3Prosjekt" / "Data" / "Squads"


class Refused(Exception):
    pass


class SquadPage(HTMLParser):
    """
    Collects the cards: <a class="players__player"> and what is inside it, with the section
    heading it stands under ("Keepere", "Støtteapparat"). The coaches are cards too.
    """

    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.players: list[dict] = []
        self._card: dict | None = None
        self._depth = 0
        self._field: str | None = None
        self._last_property: str | None = None
        self._in_heading = False
        self._section = ""

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        classes = (attrs.get("class") or "").split()

        if "players__header" in classes:
            self._in_heading = True
            self._section = ""

        if tag == "a" and "players__player" in classes:
            self._card = {"href": attrs.get("href"), "section": self._section, "text": {}, "details": {}}
            self._depth = 0
            self.players.append(self._card)

        if self._card is None:
            return

        self._depth += 1

        if "player__image" in classes:
            match = re.search(r"url\(['\"]?([^'\")]+)['\"]?\)", attrs.get("style") or "")
            self._card["image"] = match.group(1) if match else None
        elif "player__name" in classes:
            self._field = "name"
        elif "player__position" in classes:
            self._field = "position"
        elif "player__details__property" in classes:
            self._field = "property"
        elif "player__details__value" in classes:
            self._field = "value"

    def handle_endtag(self, tag):
        self._in_heading = False

        if self._card is None:
            return

        self._field = None
        self._depth -= 1

        if tag == "a" and self._depth <= 0:
            self._card = None

    def handle_data(self, data):
        text = " ".join(data.split())

        if self._in_heading:
            self._section = f"{self._section} {text}".strip()
            return

        if self._card is None or self._field is None or not text:
            return

        if self._field == "property":
            self._last_property = text
        elif self._field == "value" and self._last_property:
            self._card["details"][self._last_property] = text
        else:
            existing = self._card["text"].get(self._field, "")
            self._card["text"][self._field] = f"{existing} {text}".strip()


def fetch(url: str) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=30) as response:
        return response.read()


def parse_date(text: str, where: str) -> str:
    match = re.fullmatch(r"(\d{1,2})\.\s*([a-zæøå]+)\.?\s*(\d{4})", text.strip().lower())
    if not match or match.group(2)[:3] not in MONTHS:
        raise Refused(f"{where}: cannot read the date '{text}'.")

    day, month, year = int(match.group(1)), MONTHS[match.group(2)[:3]], int(match.group(3))
    return dt.date(year, month, day).isoformat()


def slug(name: str) -> str:
    ascii_name = (
        name.lower()
        .replace("æ", "ae").replace("ø", "o").replace("å", "aa")
    )
    ascii_name = unicodedata.normalize("NFKD", ascii_name).encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9]+", "-", ascii_name).strip("-")


def photo_kind(data: bytes) -> str | None:
    if data[:3] == b"\xff\xd8\xff":
        return "jpg"
    if data[:8] == b"\x89PNG\r\n\x1a\n":
        return "png"
    if data[:4] == b"RIFF" and data[8:12] == b"WEBP":
        return "webp"
    return None


def read_team(team: str, path: str, age: int, season: int, out: Path) -> list[dict]:
    page_url = BASE + path
    parser = SquadPage()
    parser.feed(fetch(page_url).decode("utf-8"))

    cards = [card for card in parser.players if card["section"] in PLAYER_SECTIONS]

    if not cards:
        raise Refused(f"{page_url}: no players found. Has the page changed?")

    players = []

    for card in cards:
        name = " ".join(card["text"].get("name", "").split())
        if not name:
            raise Refused(f"{page_url}: a player card without a name ({card['href']}).")

        where = f"{page_url}, {name}"

        position = POSITIONS.get(card["text"].get("position", ""))
        if position is None:
            raise Refused(f"{where}: unknown position '{card['text'].get('position')}'.")

        born = card["details"].get("Født")
        birth_date = parse_date(born, where) if born else dt.date(season - age, 1, 1).isoformat()

        photo = None
        photo_url = card.get("image")
        if photo_url:
            photo_url = re.sub(r"/width-\d+/", f"/width-{PHOTO_WIDTH}/", photo_url)
            data = fetch(photo_url)
            kind = photo_kind(data)
            if kind is None:
                raise Refused(f"{where}: the photo is not a JPEG, PNG or WebP ({photo_url}).")

            photo = f"photos/{team.lower()}-{slug(name)}.{kind}"
            (out / photo).write_bytes(data)
            time.sleep(0.2)

        players.append({
            "name": name,
            "position": position,
            "birthDate": birth_date,
            "birthDateEstimated": born is None,
            "photo": photo,
            "profileUrl": BASE + card["href"] if card.get("href", "").startswith("/") else card.get("href"),
        })

    return players


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT, help="folder to write to")
    parser.add_argument("--table", action="store_true", help="print the squads when done")
    args = parser.parse_args()

    out: Path = args.out
    (out / "photos").mkdir(parents=True, exist_ok=True)

    # Last run's photos go first, so a player who has left the squad does not leave a photo.
    for old in (out / "photos").iterdir():
        old.unlink()

    today = dt.date.today()

    try:
        teams = [
            {"team": team, "page": BASE + path, "players": read_team(team, path, age, today.year, out)}
            for team, path, age in TEAMS
        ]
    except Refused as refusal:
        print(f"Refused: {refusal}", file=sys.stderr)
        return 1

    squads = {
        "schemaVersion": 1,
        "source": "ikstart.no, spillersidene",
        "fetchedOn": today.isoformat(),
        "teams": teams,
    }

    (out / "squads.json").write_text(json.dumps(squads, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    for team in teams:
        with_photo = sum(1 for p in team["players"] if p["photo"])
        print(f"{team['team']}: {len(team['players'])} players, {with_photo} with a photo")

        if args.table:
            for p in team["players"]:
                estimated = " (estimated)" if p["birthDateEstimated"] else ""
                print(f"  {p['name']:<28} {p['position']:<11} {p['birthDate']}{estimated}  {p['photo'] or '-'}")

    print(f"Wrote {out / 'squads.json'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
