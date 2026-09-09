// Progress counter for the 5C form, plus the copy-link button, the squad filter and the
// section tabs in the coach overview.
//
// This lives in a file rather than in a <script> block because the CSP has no
// unsafe-inline. Setting element.style from JavaScript is still allowed, which is how the
// progress bar gets its width -- a style="" attribute in the markup would be blocked.
//
// The form works without any of this. The counter is an aid, not a requirement: with
// JavaScript off you get a form that still submits and still validates on the server --
// and a coach gets the team page as one long column of sections instead of tabs, which is
// what it was before the tabs existed.

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

    // Turns the marked-up sections of the coach team page into tabs.
    //
    // The tab strip is BUILT HERE and is not in the markup, for the same reason the filter
    // above is hidden until this file runs: with JavaScript off the sections are simply
    // stacked down the page, which is what the page was before, and there is no strip
    // promising a switch that nothing would perform.
    //
    // The strip is built from whatever panels the server actually rendered, so a page
    // without a trend, or without an aggregate to break down, gets a strip with only the
    // tabs it has. Nothing here knows how many sections a page is meant to have -- which
    // is what lets the coach team page, the coach player page, the feedback page and the
    // form all share one component and look the same.
    //
    // Returns a small handle so a page with live state -- the form, counting its own
    // answers -- can keep the strip in step. Null when there was nothing to build.
    function initSectionTabs() {
        var panels = Array.prototype.slice.call(
            document.querySelectorAll("[data-tab-panel]"));

        // One panel is not a set of tabs, it is a page. Two is the least that can switch.
        if (panels.length < 2) {
            return null;
        }

        var strip = document.createElement("div");
        strip.className = "sc-tabs";
        strip.setAttribute("role", "tablist");
        strip.setAttribute("aria-label", "Sections");

        var tabs = [];
        var badges = [];
        var dots = [];

        panels.forEach(function (panel, index) {
            var label = panel.getAttribute("data-tab-label") || "Section " + (index + 1);
            var id = "sc-panel-" + index;
            var tabId = "sc-tab-" + index;

            panel.id = id;
            panel.classList.add("sc-tabpanel");
            panel.setAttribute("role", "tabpanel");
            panel.setAttribute("aria-labelledby", tabId);

            // Focusable so that Tab out of the strip lands inside the panel that was just
            // opened, rather than skipping past everything it contains.
            panel.setAttribute("tabindex", "0");

            var tab = document.createElement("button");
            tab.type = "button";
            tab.className = "sc-tabs__tab";
            tab.id = tabId;
            tab.setAttribute("role", "tab");
            tab.setAttribute("aria-controls", id);
            tab.appendChild(document.createTextNode(label));

            // Count and dot are created empty and stay in the DOM. A page that never uses
            // them shows nothing -- .sc-tabs__count:empty is display:none -- and a page that
            // updates them while somebody works does not have to build elements to do it.
            var badge = document.createElement("span");
            badge.className = "sc-tabs__count";
            badge.textContent = panel.getAttribute("data-tab-count") || "";
            tab.appendChild(badge);
            badges.push(badge);

            // A section wanting attention says so on its tab, because the reader is by
            // definition looking at a different one.
            var dot = document.createElement("span");
            dot.className = "sc-tabs__dot";
            dot.setAttribute("role", "img");
            dot.setAttribute("aria-label", "needs attention");
            dot.hidden = panel.getAttribute("data-tab-flag") !== "true";
            tab.appendChild(dot);
            dots.push(dot);

            tab.addEventListener("click", function () {
                select(index, true);
            });

            strip.appendChild(tab);
            tabs.push(tab);
        });

        // Left/right walk the strip, home/end jump to its ends -- what a tablist is
        // expected to do from the keyboard.
        strip.addEventListener("keydown", function (event) {
            var current = tabs.indexOf(document.activeElement);
            if (current === -1) {
                return;
            }

            var next = null;

            if (event.key === "ArrowRight") {
                next = (current + 1) % tabs.length;
            } else if (event.key === "ArrowLeft") {
                next = (current - 1 + tabs.length) % tabs.length;
            } else if (event.key === "Home") {
                next = 0;
            } else if (event.key === "End") {
                next = tabs.length - 1;
            }

            if (next !== null) {
                event.preventDefault();
                select(next, false);
                tabs[next].focus();
            }
        });

        function select(index, fromClick) {
            panels.forEach(function (panel, i) {
                var isCurrent = i === index;

                panel.hidden = !isCurrent;
                tabs[i].setAttribute("aria-selected", isCurrent ? "true" : "false");

                // Only the selected tab is in the tab order; the arrow keys reach the rest.
                tabs[i].tabIndex = isCurrent ? 0 : -1;
            });

            // Remembered per team and round, so that following a player and coming back
            // returns to the section that was open rather than to the first one.
            try {
                window.sessionStorage.setItem(storageKey(), String(index));
            } catch (e) {
                // Private mode, or storage turned off. The tabs still work.
            }

            // On a phone the strip scrolls sideways, and the selected tab can start off
            // screen -- on the first paint, or after the arrow keys walk past the edge.
            // Only the strip is moved, never the page.
            var tab = tabs[index];
            var left = tab.offsetLeft;
            var right = left + tab.offsetWidth;

            if (left < strip.scrollLeft) {
                strip.scrollLeft = left;
            } else if (right > strip.scrollLeft + strip.clientWidth) {
                strip.scrollLeft = right - strip.clientWidth;
            }

            if (fromClick) {
                // Keeps the strip in view when a tall panel is replaced by a short one and
                // the page shrinks under the reader.
                var top = strip.getBoundingClientRect().top + window.pageYOffset;
                if (window.pageYOffset > top) {
                    window.scrollTo(0, top - 12);
                }
            }
        }

        function storageKey() {
            return "sc-tab:" + window.location.pathname + window.location.search;
        }

        function initialIndex() {
            // A link to #sc-panel-N wins: it is the most deliberate of the three.
            var hash = window.location.hash.replace("#", "");
            for (var i = 0; i < panels.length; i++) {
                if (panels[i].id === hash) {
                    return i;
                }
            }

            // Then whatever the server asked for. The form uses it to open the section
            // holding the first statement that came back unanswered -- otherwise that is
            // an error message sitting behind a tab nobody was told to press.
            for (var j = 0; j < panels.length; j++) {
                if (panels[j].getAttribute("data-tab-open") === "true") {
                    return j;
                }
            }

            try {
                var saved = parseInt(window.sessionStorage.getItem(storageKey()), 10);
                if (!isNaN(saved) && saved >= 0 && saved < panels.length) {
                    return saved;
                }
            } catch (e) {
                // Same as above.
            }

            return 0;
        }

        // The heading now says what the selected tab already says. It stays in the markup
        // for the no-JavaScript page and for the panel's accessible name.
        panels.forEach(function (panel) {
            var heading = panel.querySelector("[data-tab-heading]");
            if (heading) {
                heading.hidden = true;
            }
        });

        panels[0].parentNode.insertBefore(strip, panels[0]);
        select(initialIndex(), false);

        return {
            panels: panels,
            strip: strip,
            select: select,
            setCount: function (index, text) {
                badges[index].textContent = text;
            },
            setFlag: function (index, on) {
                dots[index].hidden = !on;
            }
        };
    }

    // The form, on top of the tabs above.
    //
    // Same strip, same look as the coach pages -- a respondent who has seen one of these
    // pages has seen all of them. What a form needs on top of a set of tabs is forward
    // motion and a truthful count: five statements at a time is the point, but only if you
    // can see how far you have got and get to the end without hunting for it.
    //
    // Hidden panels still post. A radio in a section nobody opened submits exactly as it
    // would have done in the long column, so the tabs change what is on screen and nothing
    // about what is saved.
    function initFormSteps(tabs) {
        var form = document.querySelector("[data-survey-form]");
        if (!form || !tabs) {
            return;
        }

        var panels = tabs.panels;

        // Answered means any radio in the group is checked -- including "Do not know",
        // which is an answer, just not a number. Same rule as the bar at the top.
        function countIn(panel) {
            var groups = panel.querySelectorAll("[role=radiogroup]");
            var done = 0;

            for (var i = 0; i < groups.length; i++) {
                if (groups[i].querySelector("input[type=radio]:checked")) {
                    done++;
                }
            }

            return { done: done, total: groups.length };
        }

        function refresh() {
            panels.forEach(function (panel, index) {
                var count = countIn(panel);
                if (count.total === 0) {
                    return;
                }

                tabs.setCount(index, count.done + "/" + count.total);

                // A section with something left in it is marked, so the reader does not
                // have to open all five to find the one they skipped.
                tabs.setFlag(index, count.done < count.total);
            });
        }

        // Back and Next, built here and appended to each panel. A form is walked through
        // in order; the strip is for jumping about once you know where you are going.
        panels.forEach(function (panel, index) {
            if (countIn(panel).total === 0) {
                return;
            }

            var nav = document.createElement("div");
            nav.className = "sc-stepnav";

            if (index > 0) {
                var back = document.createElement("button");
                back.type = "button";
                back.className = "sc-btn sc-btn--secondary";
                back.textContent = "Back";
                back.addEventListener("click", function () {
                    tabs.select(index - 1, true);
                });
                nav.appendChild(back);
            }

            if (index < panels.length - 1) {
                var next = document.createElement("button");
                next.type = "button";
                next.className = "sc-btn";
                next.textContent = "Next";
                next.addEventListener("click", function () {
                    tabs.select(index + 1, true);
                });
                nav.appendChild(next);
            }

            if (nav.childNodes.length > 0) {
                panel.appendChild(nav);
            }
        });

        form.addEventListener("change", refresh);
        refresh();
    }

    document.addEventListener("DOMContentLoaded", function () {
        initProgress();
        initCopyLinks();
        initPlayerFilter();
        initFormSteps(initSectionTabs());
    });
})();
