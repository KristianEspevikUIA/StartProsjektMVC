// The best eleven as a game board, the way Football Manager does it: drag a substitute onto a
// player to bring them on, drag players between positions to swap them, or drag a starter onto
// the substitutes to take them off. Tapping works too -- tap a player, then where they should
// go -- which is how it works on a tablet and from the keyboard.
//
// Every shirt shows the player's readiness IN THAT POSITION, and the team rating over the pitch
// is the average of those, so it moves with who is brought on where: a natural in the position
// counts in full, a 2nd or 3rd position less, and a position no coach named much less. That is
// SuccessionMath.PositionFit, and the one sum done here.
//
// Nothing is saved. The eleven the page picked is the coaches' ratings, and it stays that way
// for everybody; what a coach builds here lives in the address bar (?lineup=...), so the link
// opens it again, or can be sent to another coach. "Back to the best eleven" drops it.
//
// Everything else shown about a player -- the name, the overall -- comes from the data block the
// page renders (SuccessionFormationViewModel.Editor). In a file rather than a <script> block
// because the CSP has no unsafe-inline. Without it the page is the pick and the substitutes as a
// list.

(function () {
    "use strict";

    var ORDINALS = ["1st", "2nd", "3rd"];

    function ordinal(rank) {
        return ORDINALS[rank - 1] || rank + "th";
    }

    function element(tag, className, text) {
        var node = document.createElement(tag);
        if (className) {
            node.className = className;
        }
        if (text !== undefined && text !== null) {
            node.textContent = text;
        }
        return node;
    }

    function format(value) {
        return value === null || value === undefined ? "–" : value.toFixed(1);
    }

    function average(values) {
        var present = values.filter(function (value) { return value !== null; });
        if (present.length === 0) {
            return null;
        }
        return present.reduce(function (sum, value) { return sum + value; }, 0) / present.length;
    }

    function init() {
        var root = document.querySelector("[data-lineup]");
        var block = document.getElementById("lineup-data");
        if (!root || !block) {
            return;
        }

        var data;
        try {
            data = JSON.parse(block.textContent);
        } catch (e) {
            return;
        }

        if (!data || !data.slots || !data.players || !data.pick || data.pick.length !== data.slots.length) {
            return;
        }

        var lines = root.querySelector("[data-lineup-lines]");
        var benchList = root.querySelector("[data-lineup-bench]");
        var benchEmpty = root.querySelector("[data-lineup-bench-empty]");
        var benchCount = root.querySelector("[data-lineup-bench-count]");
        var focusPanel = root.querySelector("[data-lineup-focus]");
        var bar = root.querySelector("[data-lineup-bar]");
        var status = root.querySelector("[data-lineup-status]");
        var reset = root.querySelector("[data-lineup-reset]");
        var announcer = root.querySelector("[data-lineup-announce]");

        if (!lines || !benchList) {
            return;
        }

        var players = {};
        data.players.forEach(function (player) {
            players[player.id] = player;
        });

        var pick = data.pick.map(function (id) {
            return id && players[id] ? id : null;
        });

        var lineup = fromAddress() || pick.slice();
        var drawn = null;        // the lineup as last drawn, to tell which shirts are new
        var started = false;     // no flashing numbers on the first draw

        // What is picked up: { kind: "slot", index } for a shirt on the pitch, { kind: "player",
        // id } for a substitute. Selected by a tap; dragging is the same, held down.
        var selected = null;
        var dragging = null;
        var dropTarget = null;

        // What the pick found, as the page says it. Once the eleven is the coach's own, a gap is
        // one they made, and the note says so instead.
        var filledNotes = Array.prototype.map.call(
            root.querySelectorAll("[data-lineup-stat='filled-note']"),
            function (node) { return { node: node, text: node.textContent.trim() }; });

        // -------------------------------------------------------------------------------
        // The rules
        // -------------------------------------------------------------------------------

        function rankFor(player, position) {
            return player && player.positions ? player.positions[position] || null : null;
        }

        // SuccessionMath.PositionFit.
        function fitFor(player, position) {
            var rank = rankFor(player, position);
            if (!rank) {
                return player.overall - (data.outOfPositionPenalty || 0);
            }
            var list = data.rankPenalty || [];
            return list.length === 0
                ? player.overall
                : player.overall - (list[Math.min(Math.max(rank - 1, 0), list.length - 1)] || 0);
        }

        // SuccessionFormat.RatingTone.
        function tone(value) {
            if (value === null || value === undefined) {
                return "sc-light sc-light--none";
            }
            if (value >= data.readyAt) {
                return "sc-light sc-light--ready";
            }
            return value >= data.developingAt ? "sc-light sc-light--developing" : "sc-light sc-light--notyet";
        }

        // "Viljar (TS-08-11)": for what is read out, where a second line is not an option.
        function fullName(player) {
            return player.tag ? player.name + " (" + player.tag + ")" : player.name;
        }

        // The name, and the code under it when another player on the page has the same one.
        function nameNode(className, player) {
            var node = element("span", className, player.name);
            if (player.tag) {
                node.appendChild(element("span", className.replace("__name", "__tag"), player.tag));
            }
            return node;
        }

        function playerIn(index) {
            var id = lineup[index];
            return id ? players[id] : null;
        }

        function onPitch(id) {
            return lineup.indexOf(id) !== -1;
        }

        function isPick() {
            return sameAs(pick);
        }

        function sameAs(other) {
            for (var i = 0; i < lineup.length; i++) {
                if (lineup[i] !== other[i]) {
                    return false;
                }
            }
            return true;
        }

        function ratingOf(eleven) {
            return average(eleven.map(function (id, index) {
                return id ? fitFor(players[id], data.slots[index].position) : null;
            }));
        }

        var pickRating = ratingOf(pick);

        // Whose fit the pitch lights up, and which position the substitutes are sorted for,
        // given what is picked up.
        function activePlayer() {
            var source = dragging || selected;
            if (!source) {
                return null;
            }
            return source.kind === "player" ? players[source.id] : playerIn(source.index);
        }

        function activePosition() {
            var source = dragging || selected;
            return source && source.kind === "slot" ? data.slots[source.index].position : null;
        }

        // -------------------------------------------------------------------------------
        // The address bar
        // -------------------------------------------------------------------------------

        // "12.5.0.7...": a player id per slot in the page's order, 0 for an empty one. Only
        // players already on the page count -- an id that is not in the data block is not
        // somebody the page may show, and the whole value is ignored rather than half-used.
        function fromAddress() {
            var value;
            try {
                value = new URLSearchParams(window.location.search).get("lineup");
            } catch (e) {
                return null;
            }

            if (!value) {
                return null;
            }

            var parts = value.split(".");
            if (parts.length !== data.slots.length) {
                return null;
            }

            var seen = {};
            var result = [];

            for (var i = 0; i < parts.length; i++) {
                if (!/^\d+$/.test(parts[i])) {
                    return null;
                }

                var id = parseInt(parts[i], 10);
                if (id === 0) {
                    result.push(null);
                    continue;
                }

                if (!players[id] || seen[id]) {
                    return null;
                }

                seen[id] = true;
                result.push(id);
            }

            return result;
        }

        function toAddress() {
            if (!window.history || !window.history.replaceState) {
                return;
            }

            var url = new URL(window.location.href);

            if (isPick()) {
                url.searchParams.delete("lineup");
            } else {
                url.searchParams.set("lineup", lineup.map(function (id) { return id || 0; }).join("."));
            }

            window.history.replaceState(window.history.state, "", url.toString());
        }

        // -------------------------------------------------------------------------------
        // Moving
        // -------------------------------------------------------------------------------

        // Source and target are each a slot, a substitute, or (target only) the bench as a
        // whole. Returns whether anything moved.
        function move(source, target) {
            var before = lineup.slice();

            if (source.kind === "slot" && target.kind === "slot") {
                if (source.index === target.index) {
                    return false;
                }
                var held = lineup[source.index];
                lineup[source.index] = lineup[target.index];
                lineup[target.index] = held;
            } else if (source.kind === "slot" && target.kind === "player") {
                // A starter onto a substitute: they change places.
                lineup[source.index] = target.id;
            } else if (source.kind === "player" && target.kind === "slot") {
                lineup[target.index] = source.id;
            } else if (source.kind === "slot" && target.kind === "bench") {
                lineup[source.index] = null;
            } else {
                return false;
            }

            if (sameAs(before)) {
                return false;
            }

            announce(describe(before));
            return true;
        }

        // "Alex on for Sam at LST. Kim to RST." -- what a screen reader hears after a move.
        function describe(before) {
            var said = [];

            lineup.forEach(function (id, index) {
                if (!id || before[index] === id) {
                    return;
                }
                var position = data.slots[index].position;
                var out = rankFor(players[id], position) ? "" : ", out of position";

                if (before.indexOf(id) === -1) {
                    var replaced = before[index] && !onPitch(before[index]) ? " for " + fullName(players[before[index]]) : "";
                    said.push(fullName(players[id]) + " on" + replaced + " at " + position + out);
                } else {
                    said.push(fullName(players[id]) + " to " + position + out);
                }
            });

            before.forEach(function (id, index) {
                if (id && !onPitch(id) && lineup[index] === null) {
                    said.push(fullName(players[id]) + " off");
                }
            });

            var rating = ratingOf(lineup);
            said.push("Team rating " + format(rating));

            return said.join(". ") + ".";
        }

        function commit(focusKey) {
            selected = null;
            render(focusKey);
            toAddress();
        }

        // What a tap does, given what is already picked up.
        function tap(target) {
            if (!selected) {
                // An empty position can be picked up too: it is where the next tap on a
                // substitute goes.
                selected = target;
                render(keyOf(target));
                return;
            }

            if (keyOf(selected) === keyOf(target)) {
                selected = null;
                render(keyOf(target));
                return;
            }

            if (selected.kind === "player" && target.kind === "player") {
                selected = target;
                render(keyOf(target));
                return;
            }

            var source = selected;
            move(source, target);
            commit(focusAfter(source, target));
        }

        // Focus stays on the pitch, on the position that changed: a substitute who has just
        // come on is not in the list any more to be focused.
        function focusAfter(source, target) {
            if (target.kind === "slot") {
                return keyOf(target);
            }
            return source.kind === "slot" ? keyOf(source) : null;
        }

        function keyOf(item) {
            if (!item) {
                return null;
            }
            return item.kind === "slot" ? "slot:" + item.index : item.kind === "player" ? "player:" + item.id : "bench";
        }

        function itemOf(node) {
            var card = node && node.closest ? node.closest("[data-lineup-item]") : null;
            if (!card || !root.contains(card)) {
                return null;
            }

            var key = card.getAttribute("data-lineup-item").split(":");
            return key[0] === "slot"
                ? { kind: "slot", index: parseInt(key[1], 10) }
                : { kind: "player", id: parseInt(key[1], 10) };
        }

        // -------------------------------------------------------------------------------
        // Drawing
        // -------------------------------------------------------------------------------

        function token(index) {
            var slot = data.slots[index];
            var player = playerIn(index);
            var card = element("div", "sc-token");
            var shirt = element("span", "sc-shirt");

            card.setAttribute("data-lineup-item", "slot:" + index);
            card.setAttribute("role", "button");
            card.tabIndex = 0;
            shirt.appendChild(element("span", "sc-shirt__body", slot.position));
            card.appendChild(shirt);

            if (!player) {
                card.classList.add("sc-token--empty");
                card.appendChild(element("span", "sc-token__name", pick[index] ? "Empty" : "Nobody"));
                card.setAttribute("aria-label", slot.name + ": empty");
                return card;
            }

            var rank = rankFor(player, slot.position);
            var fit = fitFor(player, slot.position);

            card.draggable = true;
            shirt.appendChild(element("span", "sc-shirt__rating " + tone(fit), format(fit)));

            if (!rank) {
                card.classList.add("sc-token--out");
                shirt.appendChild(element("span", "sc-shirt__warn", "!"));
            }

            if (drawn && drawn[index] !== player.id && started) {
                card.classList.add("sc-token--arrived");
            }

            card.appendChild(nameNode("sc-token__name", player));
            card.setAttribute("aria-label", slot.name + ": " + fullName(player) + ", " + format(fit) + " in this position, " +
                (rank ? ordinal(rank) + " position" : "out of position"));

            return card;
        }

        function substitute(player, position) {
            var row = element("li", "sc-bench__row");
            var card = element("div", "sc-sub");
            var rank = position ? rankFor(player, position) : null;
            var value = position ? fitFor(player, position) : player.overall;
            var first = Object.keys(player.positions || {})[0] || "–";

            card.setAttribute("data-lineup-item", "player:" + player.id);
            card.setAttribute("role", "button");
            card.tabIndex = 0;
            card.draggable = true;

            if (position) {
                card.classList.add(rank ? "sc-sub--fit" : "sc-sub--nofit");
            }

            card.appendChild(element("span", "sc-sub__pos", position ? (rank ? ordinal(rank) : "–") : first));
            card.appendChild(nameNode("sc-sub__name", player));
            card.appendChild(element("span", "sc-sub__rating " + tone(value), format(value)));

            card.setAttribute("aria-label", fullName(player) + ", " + format(value) +
                (position ? (rank ? " as a " + ordinal(rank) + " position " : " out of position ") + "at " + position : " overall") +
                ", plays " + (Object.keys(player.positions || {}).join(", ") || "no named position"));

            row.appendChild(card);
            return row;
        }

        function benchOrder() {
            var position = activePosition();
            var rest = data.players.filter(function (player) {
                return !onPitch(player.id);
            });

            if (!position) {
                return rest;
            }

            // Named for the position first, best fit first -- the list a manager wants when they
            // have just clicked on a hole in the team.
            return rest
                .map(function (player, order) {
                    return { player: player, named: !!rankFor(player, position), fit: fitFor(player, position), order: order };
                })
                .sort(function (a, b) {
                    if (a.named !== b.named) {
                        return a.named ? -1 : 1;
                    }
                    if (a.fit !== b.fit) {
                        return b.fit - a.fit;
                    }
                    return a.order - b.order;
                })
                .map(function (entry) { return entry.player; });
        }

        function render(focusKey) {
            var rows = document.createDocumentFragment();
            var index = 0;

            (data.lines || [data.slots.length]).forEach(function (count) {
                var line = element("div", "sc-pitch__line");
                for (var i = 0; i < count && index < data.slots.length; i++, index++) {
                    line.appendChild(token(index));
                }
                rows.appendChild(line);
            });

            lines.replaceChildren(rows);

            var position = activePosition();
            var bench = benchOrder();
            var items = document.createDocumentFragment();
            bench.forEach(function (player) {
                items.appendChild(substitute(player, position));
            });
            benchList.replaceChildren(items);

            if (benchCount) {
                benchCount.textContent = String(bench.length);
            }

            if (benchEmpty) {
                benchEmpty.hidden = bench.length > 0;
            }

            drawn = lineup.slice();

            paint();
            renderFocus();
            renderStats();
            renderBar();
            started = true;

            if (focusKey) {
                var target = root.querySelector("[data-lineup-item='" + focusKey + "']");
                if (target) {
                    target.focus({ preventScroll: true });
                }
            }
        }

        // Selection and fit, on the shirts already drawn. Separate from render() because it
        // also runs mid-drag, when redrawing would take the shirt being dragged out from under
        // the pointer.
        function paint() {
            var player = activePlayer();
            var pickedKey = keyOf(dragging || selected);

            root.classList.toggle("sc-board--holding", !!(dragging || selected));
            root.classList.toggle("sc-board--dragging", !!dragging);

            Array.prototype.forEach.call(root.querySelectorAll("[data-lineup-item]"), function (card) {
                var key = card.getAttribute("data-lineup-item");
                var isPicked = key === pickedKey;

                card.classList.toggle("sc-picked", isPicked);
                card.setAttribute("aria-pressed", isPicked ? "true" : "false");

                if (!card.classList.contains("sc-token")) {
                    return;
                }

                var shirt = card.querySelector(".sc-shirt");
                card.classList.remove("sc-token--fit");
                shirt.removeAttribute("data-fit");

                if (player && !isPicked) {
                    var slot = data.slots[parseInt(key.split(":")[1], 10)];
                    var rank = rankFor(player, slot.position);
                    if (rank) {
                        card.classList.add("sc-token--fit");
                        shirt.setAttribute("data-fit", ordinal(rank));
                    }
                }
            });
        }

        // What is picked up, and what can be done with it -- at the top of the substitutes.
        function renderFocus() {
            if (!focusPanel) {
                return;
            }

            focusPanel.replaceChildren();

            if (!selected) {
                focusPanel.hidden = true;
                return;
            }

            var player = activePlayer();
            var slot = selected.kind === "slot" ? data.slots[selected.index] : null;
            var head = element("p", "sc-bench__focus-name");
            var actions = element("div", "sc-bench__focus-actions");

            if (player) {
                var value = slot ? fitFor(player, slot.position) : player.overall;
                head.appendChild(document.createTextNode(fullName(player) + " "));
                head.appendChild(element("span", "sc-sub__rating " + tone(value), format(value)));
                focusPanel.appendChild(head);

                var named = Object.keys(player.positions || {}).map(function (key) {
                    return key + " " + ordinal(player.positions[key]);
                });
                focusPanel.appendChild(element("p", "sc-bench__focus-line",
                    [player.code, player.team, named.join(", ") || "no position named"]
                        .filter(Boolean).join(" · ") + (player.earlier ? " · earlier cycle" : "")));

                focusPanel.appendChild(element("p", "sc-bench__focus-line sc-bench__focus-line--hint", slot
                    ? "Now tap a substitute to bring on, or another shirt to swap."
                    : "Now tap the shirt to replace. The lit-up shirts are positions a coach named."));

                if (slot) {
                    var off = element("button", "sc-bench__action", "Take off");
                    off.type = "button";
                    off.setAttribute("data-lineup-takeoff", "");
                    actions.appendChild(off);
                }

                var open = element("a", "sc-bench__action", "Player page");
                open.href = player.url;
                actions.appendChild(open);
            } else {
                head.appendChild(document.createTextNode(slot.position + " is empty"));
                focusPanel.appendChild(head);
                focusPanel.appendChild(element("p", "sc-bench__focus-line sc-bench__focus-line--hint",
                    slot.name + ". Tap a substitute to bring them on – the ones named for " + slot.position + " are at the top."));
            }

            var cancel = element("button", "sc-bench__action", "Cancel");
            cancel.type = "button";
            cancel.setAttribute("data-lineup-cancel", "");
            actions.appendChild(cancel);

            focusPanel.appendChild(actions);
            focusPanel.hidden = false;
        }

        // A number that changes lights up for a moment, so the coach sees what the change did.
        function show(node, text, className) {
            var changed = node.textContent !== text;
            node.textContent = text;
            if (className !== undefined) {
                node.className = className;
            }
            if (changed && started) {
                node.classList.remove("sc-flash");
                void node.offsetWidth;
                node.classList.add("sc-flash");
            }
        }

        function renderStats() {
            var fits = lineup.map(function (id, index) {
                return id ? fitFor(players[id], data.slots[index].position) : null;
            });
            var starters = lineup.filter(Boolean).map(function (id) { return players[id]; });
            var rating = average(fits);
            var out = 0;

            lineup.forEach(function (id, index) {
                if (id && !rankFor(players[id], data.slots[index].position)) {
                    out++;
                }
            });

            each("[data-lineup-stat='rating']", function (node) {
                show(node, format(rating), "sc-board__value " + tone(rating));
            });

            each("[data-lineup-delta]", function (node) {
                var delta = rating !== null && pickRating !== null ? rating - pickRating : 0;
                var visible = !isPick() && Math.abs(delta) >= 0.05;
                node.hidden = !visible;
                if (visible) {
                    show(node, (delta > 0 ? "+" : "−") + Math.abs(delta).toFixed(1) + " on the best eleven",
                        "sc-board__delta " + (delta > 0 ? "sc-board__delta--up" : "sc-board__delta--down"));
                }
            });

            each("[data-lineup-unit]", function (node) {
                var unit = node.getAttribute("data-lineup-unit");
                var value = average(fits.filter(function (_, index) { return data.slots[index].unit === unit; }));
                var step = value === null ? 0 : Math.min(10, Math.max(0, Math.round(value)));
                var fill = node.querySelector("[data-lineup-unit-fill]");
                var number = node.querySelector("[data-lineup-unit-value]");

                if (fill) {
                    fill.className = "sc-unit__fill sc-unit__fill--" + step + " " + tone(value);
                }
                if (number) {
                    show(number, format(value));
                }
            });

            each("[data-lineup-stat='ready']", function (node) {
                show(node, String(starters.filter(function (player) { return player.overall >= data.readyAt; }).length));
            });
            each("[data-lineup-stat='filled']", function (node) {
                show(node, String(starters.length));
            });
            each("[data-lineup-stat='out']", function (node) {
                show(node, String(out));
            });
            each("[data-lineup-out]", function (node) {
                node.hidden = out === 0;
            });

            var empty = data.slots.length - starters.length;
            filledNotes.forEach(function (note) {
                if (isPick()) {
                    note.node.textContent = note.text;
                } else {
                    note.node.textContent = empty === 0
                        ? "All " + data.slots.length + " positions filled."
                        : empty + " position" + (empty === 1 ? " is" : "s are") + " empty.";
                }
            });
        }

        function each(selector, action) {
            Array.prototype.forEach.call(root.querySelectorAll(selector), action);
        }

        function renderBar() {
            if (!bar || !status) {
                return;
            }

            bar.hidden = false;

            if (isPick()) {
                status.textContent = "The best eleven, as picked. Drag a substitute onto a player, or tap one and then the other.";
                bar.classList.remove("sc-board__bar--changed");
            } else {
                var changed = 0;
                lineup.forEach(function (id, index) {
                    if (id !== pick[index]) {
                        changed++;
                    }
                });

                status.textContent = "Your lineup: " + changed + " position" + (changed === 1 ? "" : "s") +
                    " changed. Not saved – the link in the address bar keeps it.";
                bar.classList.add("sc-board__bar--changed");
            }

            if (reset) {
                reset.disabled = isPick();
            }
        }

        var announceTimer = null;

        function announce(text) {
            if (!announcer) {
                return;
            }
            window.clearTimeout(announceTimer);
            announcer.textContent = "";
            announceTimer = window.setTimeout(function () {
                announcer.textContent = text;
            }, 50);
        }

        // -------------------------------------------------------------------------------
        // Events
        // -------------------------------------------------------------------------------

        root.addEventListener("click", function (event) {
            if (event.target.closest("[data-lineup-cancel]")) {
                var was = keyOf(selected);
                selected = null;
                render(was);
                return;
            }

            if (event.target.closest("[data-lineup-takeoff]")) {
                if (selected && selected.kind === "slot") {
                    var index = selected.index;
                    move(selected, { kind: "bench" });
                    commit("slot:" + index);
                }
                return;
            }

            if (event.target.closest("[data-lineup-reset]")) {
                lineup = pick.slice();
                announce("Back to the best eleven. Team rating " + format(pickRating) + ".");
                commit(null);
                return;
            }

            var item = itemOf(event.target);
            if (item) {
                tap(item);
            }
        });

        root.addEventListener("keydown", function (event) {
            if (event.key === "Escape" && selected) {
                var was = keyOf(selected);
                selected = null;
                render(was);
                return;
            }

            if ((event.key === "Enter" || event.key === " ") && event.target.hasAttribute &&
                event.target.hasAttribute("data-lineup-item")) {
                event.preventDefault();
                tap(itemOf(event.target));
            }
        });

        // A tap anywhere else on the page puts down what was picked up. By the event's path,
        // not root.contains(): a tap on a shirt redraws the pitch, and by the time the click
        // reaches the document the shirt it started on is no longer in it.
        document.addEventListener("click", function (event) {
            if (!selected) {
                return;
            }

            var path = event.composedPath ? event.composedPath() : [];
            var inside = path.length ? path.indexOf(root) !== -1 : root.contains(event.target);

            if (!inside) {
                selected = null;
                render(null);
            }
        });

        root.addEventListener("dragstart", function (event) {
            var item = itemOf(event.target);
            if (!item || (item.kind === "slot" && !lineup[item.index])) {
                event.preventDefault();
                return;
            }

            dragging = item;
            selected = null;

            event.dataTransfer.effectAllowed = "move";
            // Firefox starts no drag without data. The name is what a drop elsewhere would get.
            var player = item.kind === "slot" ? playerIn(item.index) : players[item.id];
            event.dataTransfer.setData("text/plain", player ? fullName(player) : "");

            renderFocus();
            // After the browser has taken its picture of the shirt, so the picture is not the
            // picked-up style.
            window.setTimeout(paint, 0);
        });

        function dropOn(node) {
            var item = itemOf(node);
            if (item) {
                return item;
            }
            return node && node.closest && node.closest(".sc-bench") ? { kind: "bench" } : null;
        }

        function canDrop(target) {
            if (!dragging || !target) {
                return false;
            }
            if (target.kind === "bench" || target.kind === "player") {
                return dragging.kind === "slot";
            }
            return keyOf(target) !== keyOf(dragging);
        }

        function markTarget(node) {
            if (dropTarget === node) {
                return;
            }
            if (dropTarget) {
                dropTarget.classList.remove("sc-drop-target");
            }
            dropTarget = node;
            if (dropTarget) {
                dropTarget.classList.add("sc-drop-target");
            }
        }

        root.addEventListener("dragover", function (event) {
            var target = dropOn(event.target);
            if (!canDrop(target)) {
                markTarget(null);
                return;
            }

            event.preventDefault();
            event.dataTransfer.dropEffect = "move";
            markTarget(target.kind === "bench"
                ? root.querySelector(".sc-bench")
                : event.target.closest("[data-lineup-item]"));
        });

        root.addEventListener("drop", function (event) {
            var target = dropOn(event.target);
            if (!canDrop(target)) {
                return;
            }

            event.preventDefault();
            var source = dragging;
            dragging = null;
            markTarget(null);

            move(source, target);
            commit(focusAfter(source, target));
        });

        root.addEventListener("dragend", function () {
            if (!dragging) {
                return;
            }
            dragging = null;
            markTarget(null);
            paint();
        });

        render(null);
        toAddress();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
