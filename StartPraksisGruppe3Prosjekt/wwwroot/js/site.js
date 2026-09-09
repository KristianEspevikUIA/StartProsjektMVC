// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

document.addEventListener("DOMContentLoaded", () => {
	const modal = document.getElementById("five-c-details");

	if (!modal) {
		return;
	}

	const title = modal.querySelector("#five-c-details-title");
	const subtitle = modal.querySelector("[data-five-c-subtitle]");
	const rule = modal.querySelector("[data-five-c-rule]");
	const contentPanels = modal.querySelectorAll("[data-five-c-content]");
	let trigger = null;

	modal.addEventListener("show.bs.modal", (event) => {
		trigger = event.relatedTarget;
		const key = trigger?.dataset.fiveCKey;
		const panel = modal.querySelector(`[data-five-c-content="${key}"]`);

		if (!panel) {
			return;
		}

		contentPanels.forEach((contentPanel) => {
			contentPanel.hidden = contentPanel !== panel;
		});

		// From the button's own data, not from the card around it. The heading used to be
		// read out of an <h3> inside the trigger -- there is none, the h3 is its sibling --
		// so this threw on every open and the dialog appeared with no heading at all.
		title.textContent = trigger.dataset.fiveCName || "";
		subtitle.textContent = panel.dataset.fiveCNorwegian || "";

		// The rule above the heading takes the C's own colour, the same one its statements
		// are marked with in the form. A class rather than a style attribute: the content
		// security policy has no unsafe-inline, and these classes already exist for the
		// markers -- see QuestionColors.
		rule.className = `sc-rule sc-modal__rule sc-qcolor--${trigger.dataset.fiveCColor || "slate"}`;
	});

	modal.addEventListener("hidden.bs.modal", () => {
		trigger?.focus();
		trigger = null;
	});
});
