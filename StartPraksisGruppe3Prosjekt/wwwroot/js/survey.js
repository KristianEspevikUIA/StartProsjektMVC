// Progress counter for the 5C form, plus the copy-link button in the coach overview.
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

    document.addEventListener("DOMContentLoaded", function () {
        initProgress();
        initCopyLinks();
    });
})();
