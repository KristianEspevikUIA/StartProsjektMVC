// Moving players about on the best eleven, the way Football Manager does it: drag a card onto
// another position to swap the two, drag a player in from the squad list beside the pitch, or
// drag a starter onto the list to take them off. Tapping works too -- tap a player, then where
// they should go -- which is also how it works on a tablet and from the keyboard.
//
// Nothing is saved. The eleven the page picked is the coaches' ratings, and it stays that way
// for everybody; what a coach builds here lives in the address bar (?lineup=...), so the link
// opens it again, or can be sent to another coach. "Back to the best eleven" drops it.
//
// Everything shown about a player -- the number, its colour, the words -- comes from the data
// block the page renders (SuccessionFormationViewModel.Editor). The only sum done here is how
// well a player fits a slot they have been moved to: overall minus the file's penalty for a
// 2nd or 3rd position, which is the rule the pick itself uses (SuccessionMath.PickEleven).
//
// In a file rather than a <script> block because the CSP has no unsafe-inline. Without it the
// page is the pick and the squad as a list, as it was before any of this existed.

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

        var pitch = root.querySelector("[data-lineup-pitch]");
        var benchList = root.querySelector("[data-lineup-bench]");
        var benchEmpty = root.querySelector("[data-lineup-bench-empty]");
        var benchCount = root.querySelector("[data-lineup-bench-count]");
        var focusPanel = root.querySelector("[data-lineup-focus]");
        var bar = root.querySelector("[data-lineup-bar]");
        var status = root.querySelector("[data-lineup-status]");
        var reset = root.querySelector("[data-lineup-reset]");
        var announcer = root.querySelector("[data-lineup-announce]");

        if (!pitch || !benchList) {
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

        // What is picked up: { kind: "slot", index } for a card on the pitch, { kind: "player",
        // id } for somebody on the bench. Selected by a tap; dragging is the same, held down.
        var selected = null;
        var dragging = null;
        var dropTarget = null;

        // The stat notes say what the pick found ("has nobody named for it"). Once the eleven
        // is the coach's own, a gap is one they made, and the note says so instead.
        var filledNotes = Array.prototype.map.call(
            document.querySelectorAll("[data-lineup-stat='filled-note']"),
            function (node) { return { node: node, text: node.textContent }; });

        // -------------------------------------------------------------------------------
        // The rules
        // -------------------------------------------------------------------------------

        function rankFor(player, position) {
            return player && player.positions ? player.positions[position] || null : null;
        }

        function penalty(rank) {
            var list = data.rankPenalty || [];
            if (list.length === 0) {
                return 0;
            }
            return list[Math.min(Math.max(rank - 1, 0), list.length - 1)] || 0;
        }

        function fitFor(player, position) {
            var rank = rankFor(player, position);
            return rank ? player.overall - penalty(rank) : null;
        }

        function playerIn(index) {
            var id = lineup[index];
            return id ? players[id] : null;
        }

        function onPitch(id) {
            return lineup.indexOf(id) !== -1;
        }

        function isPick() {
            for (var i = 0; i < lineup.length; i++) {
                if (lineup[i] !== pick[i]) {
                    return false;
                }
            }
            return true;
        }

        function teamNote(player) {
            return player.team ? " · " + player.team : "";
        }

        function positionList(player) {
            return Object.keys(player.positions || {}).slice(0, 3).join(" · ");
        }

        // Whose fit the pitch lights up, and which position the bench is sorted for, given
        // what is picked up.
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

        // Source and target are each a slot, a bench player, or (target only) the bench as a
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

        function sameAs(other) {
            for (var i = 0; i < lineup.length; i++) {
                if (lineup[i] !== other[i]) {
                    return false;
                }
            }
            return true;
        }

        // "TS-08-16 to LWB. TS-08-11 to the bench." -- what a screen reader hears after a move.
        function describe(before) {
            var said = [];

            lineup.forEach(function (id, index) {
                if (id && before[index] !== id) {
                    said.push(players[id].code + " to " + data.slots[index].position +
                        (rankFor(players[id], data.slots[index].position) ? "" : ", out of position"));
                }
            });

            before.forEach(function (id) {
                if (id && !onPitch(id)) {
                    said.push(players[id].code + " to the bench");
                }
            });

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
                // An empty slot can be picked up too: it is where the next tap on the bench goes.
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

        // Focus stays on the pitch, on the position that changed: a bench card that has just
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

        function slotCard(index) {
            var slot = data.slots[index];
            var player = playerIn(index);
            var card = element("div", "sc-slot");

            card.setAttribute("data-lineup-item", "slot:" + index);
            card.setAttribute("role", "button");
            card.tabIndex = 0;
            card.appendChild(element("span", "sc-slot__pos", slot.position));

            if (player) {
                var rank = rankFor(player, slot.position);

                card.classList.add(player.slot);
                card.draggable = true;

                if (!rank) {
                    card.classList.add("sc-slot--out");
                }

                card.appendChild(element("span", "sc-slot__code", player.code));

                var value = element("span", "sc-slot__value");
                value.appendChild(element("span", player.mean, player.number));
                card.appendChild(value);

                card.appendChild(element("span", "sc-slot__meta",
                    (rank ? ordinal(rank) + " position" : "Out of position") + teamNote(player)));

                card.setAttribute("aria-label", slot.name + ": " + player.code + ", " + player.number + ", " +
                    player.level + ", " + (rank ? ordinal(rank) + " position" : "out of position") +
                    (player.earlier ? ", from an earlier cycle" : ""));
            } else {
                card.classList.add("sc-slot--empty");
                card.appendChild(element("span", "sc-slot__code", "Nobody"));
                card.appendChild(element("span", "sc-slot__meta",
                    pick[index] ? "Empty. Pick somebody for " + slot.position + "." : "Nobody rated has " + slot.position + " among their positions."));
                card.setAttribute("aria-label", slot.name + ": empty");
            }

            return card;
        }

        function benchCard(player, position) {
            // The list item stays a list item; the card inside it is what is picked up.
            var item = element("li", "sc-bench__row");
            var card = element("div", "sc-bench__item sc-bench__card");
            var who = element("span", "sc-bench__who");
            var rank = position ? rankFor(player, position) : null;
            var about;

            if (position) {
                about = rank ? ordinal(rank) + " position for " + position : "Not named for " + position;
            } else {
                about = positionList(player) || "No position named";
            }

            card.setAttribute("data-lineup-item", "player:" + player.id);
            card.setAttribute("role", "button");
            card.tabIndex = 0;
            card.draggable = true;

            if (position && rank) {
                card.classList.add("sc-bench__card--fit");
            }

            who.appendChild(element("span", "sc-bench__code", player.code));
            who.appendChild(element("span", "sc-bench__pos",
                about + teamNote(player) + (player.earlier ? " · earlier cycle" : "")));
            card.appendChild(who);
            card.appendChild(element("span", player.mean, player.number));

            card.setAttribute("aria-label", player.code + ", " + player.number + ", " + player.level + ", " + about);

            item.appendChild(card);
            return item;
        }

        function benchOrder() {
            var position = activePosition();
            var rest = data.players.filter(function (player) {
                return !onPitch(player.id);
            });

            if (!position) {
                return rest;
            }

            // The players named for the position first, best fit first -- the list a manager
            // wants when they have just clicked on a hole in the team.
            return rest
                .map(function (player, order) {
                    return { player: player, fit: fitFor(player, position), order: order };
                })
                .sort(function (a, b) {
                    if ((a.fit === null) !== (b.fit === null)) {
                        return a.fit === null ? 1 : -1;
                    }
                    if (a.fit !== null && a.fit !== b.fit) {
                        return b.fit - a.fit;
                    }
                    return a.order - b.order;
                })
                .map(function (entry) { return entry.player; });
        }

        function render(focusKey) {
            var lines = document.createDocumentFragment();
            var index = 0;

            (data.lines || [data.slots.length]).forEach(function (count) {
                var line = element("div", "sc-pitch__line");
                for (var i = 0; i < count && index < data.slots.length; i++, index++) {
                    line.appendChild(slotCard(index));
                }
                lines.appendChild(line);
            });

            pitch.replaceChildren(lines);

            var position = activePosition();
            var bench = benchOrder();
            var items = document.createDocumentFragment();
            bench.forEach(function (player) {
                items.appendChild(benchCard(player, position));
            });
            benchList.replaceChildren(items);

            if (benchCount) {
                benchCount.textContent = String(bench.length);
            }

            if (benchEmpty) {
                benchEmpty.hidden = bench.length > 0;
            }

            paint();
            renderFocus();
            renderStats();
            renderBar();

            if (focusKey) {
                var target = root.querySelector("[data-lineup-item='" + focusKey + "']");
                if (target) {
                    target.focus({ preventScroll: true });
                }
            }
        }

        // Selection and fit, on the cards already drawn. Separate from render() because it
        // also runs mid-drag, when redrawing would take the card being dragged out from under
        // the pointer.
        function paint() {
            var player = activePlayer();
            var pickedKey = keyOf(dragging || selected);

            root.classList.toggle("sc-lineup--holding", !!(dragging || selected));
            root.classList.toggle("sc-lineup--dragging", !!dragging);

            Array.prototype.forEach.call(root.querySelectorAll("[data-lineup-item]"), function (card) {
                var key = card.getAttribute("data-lineup-item");
                var isPicked = key === pickedKey;

                card.classList.toggle(card.classList.contains("sc-slot") ? "sc-slot--picked" : "sc-bench__card--picked", isPicked);
                card.setAttribute("aria-pressed", isPicked ? "true" : "false");

                if (!card.classList.contains("sc-slot")) {
                    return;
                }

                card.classList.remove("sc-slot--fit-1", "sc-slot--fit-2", "sc-slot--fit-3");
                card.removeAttribute("data-fit");

                if (player && !isPicked) {
                    var slot = data.slots[parseInt(key.split(":")[1], 10)];
                    var rank = rankFor(player, slot.position);
                    if (rank) {
                        card.classList.add("sc-slot--fit-" + Math.min(rank, 3));
                        card.setAttribute("data-fit", ordinal(rank));
                    }
                }
            });
        }

        // What is picked up, and what to do with it -- the panel at the top of the bench.
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
            var head = element("p", "sc-bench__focus-head");
            var actions = element("div", "sc-bench__focus-actions");

            if (player) {
                head.appendChild(element("strong", "sc-bench__focus-code", player.code));
                head.appendChild(document.createTextNode(" "));
                head.appendChild(element("span", player.mean, player.number));
                focusPanel.appendChild(head);
                focusPanel.appendChild(element("p", "sc-bench__focus-line",
                    player.level + (slot ? " · playing " + slot.position : " · on the bench")));

                var named = Object.keys(player.positions || {}).map(function (key) {
                    return key + " " + ordinal(player.positions[key]);
                });
                focusPanel.appendChild(element("p", "sc-bench__focus-line",
                    (named.length ? "Named for " + named.join(", ") : "No position named") + teamNote(player) +
                    (player.earlier ? " · rated in an earlier cycle" : "")));

                focusPanel.appendChild(element("p", "sc-bench__focus-line sc-bench__focus-line--hint", slot
                    ? "Tap another position to swap, or a player in the list to bring them on for " + player.code + "."
                    : "Tap a position on the pitch to put " + player.code + " there. The outlined ones are positions a coach named."));

                if (slot) {
                    var off = element("button", "sc-btn--link", "Take off");
                    off.type = "button";
                    off.setAttribute("data-lineup-takeoff", "");
                    actions.appendChild(off);
                }

                var open = element("a", "sc-bench__focus-open", "Open player page");
                open.href = player.url;
                actions.appendChild(open);
            } else {
                head.appendChild(element("strong", "sc-bench__focus-code", slot.position));
                head.appendChild(document.createTextNode(" · " + slot.name + " is empty"));
                focusPanel.appendChild(head);
                focusPanel.appendChild(element("p", "sc-bench__focus-line sc-bench__focus-line--hint",
                    "Tap a player in the list to put them here. The ones named for " + slot.position + " are at the top."));
            }

            var cancel = element("button", "sc-btn--link", "Cancel");
            cancel.type = "button";
            cancel.setAttribute("data-lineup-cancel", "");
            actions.appendChild(cancel);

            focusPanel.appendChild(actions);
            focusPanel.hidden = false;
        }

        function renderStats() {
            var starters = lineup.filter(Boolean).map(function (id) { return players[id]; });
            var ready = starters.filter(function (player) { return player.overall >= data.readyAt; }).length;
            var average = starters.length
                ? starters.reduce(function (sum, player) { return sum + player.overall; }, 0) / starters.length
                : null;

            setAll("[data-lineup-stat='filled']", String(starters.length));
            setAll("[data-lineup-stat='ready']", String(ready));

            Array.prototype.forEach.call(document.querySelectorAll("[data-lineup-stat='average']"), function (node) {
                node.textContent = average === null ? "–" : average.toFixed(1);
                // The workbook's 1-10 colour scale, one step per whole point -- as
                // SuccessionFormat.RatingClass does it.
                node.className = average === null
                    ? "sc-rate sc-rate--none"
                    : "sc-rate sc-rate--" + Math.min(10, Math.max(1, Math.round(average)));
            });

            var empty = data.slots.length - starters.length;
            filledNotes.forEach(function (note) {
                if (isPick()) {
                    note.node.textContent = note.text;
                } else {
                    note.node.textContent = empty === 0
                        ? "Every position has a player in it."
                        : empty + " position" + (empty === 1 ? " is" : "s are") + " empty.";
                }
            });
        }

        function setAll(selector, text) {
            Array.prototype.forEach.call(document.querySelectorAll(selector), function (node) {
                node.textContent = text;
            });
        }

        function renderBar() {
            if (!bar || !status) {
                return;
            }

            bar.hidden = false;

            if (isPick()) {
                status.textContent = "The best eleven, as picked. Drag a player to move them, or tap one and then where they should go.";
                bar.classList.remove("sc-lineup__bar--changed");
            } else {
                var changed = 0;
                var out = 0;

                lineup.forEach(function (id, index) {
                    if (id !== pick[index]) {
                        changed++;
                    }
                    if (id && !rankFor(players[id], data.slots[index].position)) {
                        out++;
                    }
                });

                status.textContent = "Your own eleven: " + changed + " position" + (changed === 1 ? "" : "s") +
                    " changed from the pick" + (out ? ", " + out + " player" + (out === 1 ? "" : "s") + " out of position" : "") +
                    ". Nothing is saved – the link in the address bar opens this eleven again.";
                bar.classList.add("sc-lineup__bar--changed");
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
                announce("Back to the best eleven.");
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
        // not root.contains(): a tap on a card redraws the pitch, and by the time the click
        // reaches the document the card it started on is no longer in it.
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
            // Firefox starts no drag without data. The code is what a drop elsewhere would get.
            var player = item.kind === "slot" ? playerIn(item.index) : players[item.id];
            event.dataTransfer.setData("text/plain", player ? player.code : "");

            renderFocus();
            // After the browser has taken its picture of the card, so the picture is not the
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
            if (target.kind === "bench") {
                return dragging.kind === "slot";
            }
            if (target.kind === "player") {
                return dragging.kind === "slot";
            }
            return keyOf(target) !== keyOf(dragging);
        }

        function markTarget(node) {
            if (dropTarget === node) {
                return;
            }
            if (dropTarget) {
                dropTarget.classList.remove("sc-lineup__target");
            }
            dropTarget = node;
            if (dropTarget) {
                dropTarget.classList.add("sc-lineup__target");
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
