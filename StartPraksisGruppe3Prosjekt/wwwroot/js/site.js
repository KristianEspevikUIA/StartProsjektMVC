// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

document.addEventListener("DOMContentLoaded", () => {
	const modal = document.getElementById("five-c-details");

	if (!modal) {
		return;
	}

	const title = modal.querySelector("#five-c-details-title");
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
		title.textContent = `${trigger.querySelector("h3").textContent} (${panel.dataset.fiveCNorwegian})`;
	});

	modal.addEventListener("hidden.bs.modal", () => {
		trigger?.focus();
		trigger = null;
	});
});
