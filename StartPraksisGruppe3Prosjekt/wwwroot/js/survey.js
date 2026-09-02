// Progress counter for the 5C form, plus the copy-link button and the squad filter in
// the coach overview.
//
// This lives in a file rather than in a <script> block because the CSP has no
// unsafe-inline. Setting element.style from JavaScript is still allowed, which is how the
// progress bar gets its width -- a style="" attribute in the markup would be blocked.
//
// The form works without any of this. The counter is an aid, not a requirement: with
// JavaScript off you get a form that still submits and still validates on the server.

(function () {
    "use strict";

    function initProgress() {
        var form = document.querySelector("[data-survey-form]");
        if (!form) {
            return;
        }

        var counter = form.querySelector("[data-survey-counter]");
        var bar = form.querySelector("[data-survey-bar]");
        var total = parseInt(form.getAttribute("data-question-count"), 10);

        if (!counter || isNaN(total) || total <= 0) {
            return;
        }

        function answeredCount() {
            // One group per question. A group counts as answered when any radio in it is
            // checked -- including "Don't know", which is an answer, just not a number.
            var groups = {};

            var checked = form.querySelectorAll("input[type=radio]:checked");
            for (var i = 0; i < checked.length; i++) {
                groups[checked[i].name] = true;
            }

            return Object.keys(groups).length;
        }

        function update() {
            var answered = answeredCount();

            counter.textContent = answered + " of " + total + " answered";

            if (bar) {
                bar.style.width = Math.round((answered / total) * 100) + "%";
            }
        }

        form.addEventListener("change", function (event) {
            if (event.target && event.target.type === "radio") {
                update();
            }
        });

        update();
    }

    function initCopyLinks() {
        var buttons = document.querySelectorAll("[data-copy-link]");

        for (var i = 0; i < buttons.length; i++) {
            buttons[i].addEventListener("click", function (event) {
                var button = event.currentTarget;
                var link = button.getAttribute("data-copy-link");

                if (!link || !navigator.clipboard) {
                    return;
                }

                var original = button.textContent;

                navigator.clipboard.writeText(link).then(function () {
                    button.textContent = "Link copied";
                    window.setTimeout(function () {
                        button.textContent = original;
                    }, 2000);
                });
            });
        }
    }

    // Live filtering of the player table on a team page.
    //
    // Every row is already in the document -- this only decides which of them are shown, so
    // nothing is fetched and no player code leaves the page. It is scoped to the one table
    // it is pointed at, which is the squad the coach opened.
    //
    // The control is markup-hidden until this runs. Without JavaScript the table is complete
    // and the box never appears, rather than sitting there doing nothing.
    function initPlayerFilter() {
        var panel = document.querySelector("[data-player-filter]");
        if (!panel) {
            return;
        }

        var input = panel.querySelector("input[type=search]");
        var summary = panel.querySelector("[data-player-filter-summary]");
        var clear = panel.querySelector("[data-player-filter-clear]");
        var rows = document.querySelectorAll("[data-player-row]");
        var empty = document.querySelector("[data-player-filter-empty]");

        if (!input || rows.length === 0) {
            return;
        }

        function apply() {
            // Case-folded and trimmed, so "ts-08" finds TS-08-16 and a stray space does not
            // empty the table.
            var query = input.value.trim().toLowerCase();
            var shown = 0;

            for (var i = 0; i < rows.length; i++) {
                var haystack = (rows[i].getAttribute("data-player-search") || "").toLowerCase();
                var matches = query === "" || haystack.indexOf(query) !== -1;

                rows[i].hidden = !matches;

                if (matches) {
                    shown++;
                }
            }

            if (empty) {
                empty.hidden = shown !== 0;
            }

            if (summary) {
                summary.textContent = query === ""
                    ? ""
                    : "Showing " + shown + " of " + rows.length;
            }

            if (clear) {
                clear.hidden = query === "";
            }
        }

        input.addEventListener("input", apply);

        if (clear) {
            clear.addEventListener("click", function () {
                input.value = "";
                apply();
                input.focus();
            });
        }

        panel.hidden = false;
        apply();
    }

    document.addEventListener("DOMContentLoaded", function () {
        initProgress();
        initCopyLinks();
        initPlayerFilter();
    });
})();
