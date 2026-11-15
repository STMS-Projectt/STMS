(() => {
    function initialize(root) {
        for (const form of root.querySelectorAll("[data-bid-checklist]")) {
            const total = form.closest(".bid-checklist").querySelector("[data-checklist-total]");
            const groups = [...form.querySelectorAll("[data-checklist-group]")];
            const format = (points, possible) => `${points / 100}% / ${possible / 100}%`;
            form.addEventListener("change", event => {
                if (!event.target.matches("[data-checklist-choice]")) return;
                let points = 0, possible = 0;
                for (const group of groups) {
                    let groupPoints = 0, groupPossible = 0;
                    for (const input of group.querySelectorAll("[data-checklist-choice]")) {
                        const weight = Math.round(Number(input.dataset.weight) * 100);
                        groupPossible += weight;
                        if (input.checked) groupPoints += weight;
                    }
                    group.querySelector("[data-checklist-subtotal]").textContent = format(groupPoints, groupPossible);
                    points += groupPoints; possible += groupPossible;
                }
                total.textContent = format(points, possible);
            });
        }
        for (const form of root.querySelectorAll("[data-weight-plan]")) {
            const rows = [...form.querySelectorAll("[data-weight-row]")].map(row => ({
                row, id: row.dataset.criterion, proposed: row.dataset.proposed,
                selected: row.querySelector("[data-weight-choice]"), weight: row.querySelector("[data-weight-value]")
            }));
            const total = form.querySelector("[data-weight-total]");
            const save = form.querySelector("button[type=submit]");
            const suggestion = form.querySelector("[data-suggest-weights]");
            const status = form.querySelector("[data-weight-status]");
            let busy = false, revision = 0;
            const chosen = () => rows.filter(r => r.selected.checked);
            function sync() {
                const active = chosen();
                const sum = active.reduce((value, row) => value + Math.round(Number(row.weight.value) * 100), 0);
                const valid = active.length > 0 && sum === 10000 && active.every(r => r.weight.value && r.weight.validity.valid);
                total.textContent = `${sum / 100} / 100%`;
                total.classList.toggle("is-complete", valid);
                save.disabled = !valid || busy;
                suggestion.disabled = active.length === 0 || active.length > 40 || busy;
                for (const row of rows) {
                    row.weight.disabled = !row.selected.checked;
                    row.row.classList.toggle("is-selected", row.selected.checked);
                }
            }
            form.addEventListener("input", () => { revision++; sync(); });
            form.querySelector("[data-reset-weights]").addEventListener("click", () => {
                for (const row of rows) { row.selected.checked = true; row.weight.value = row.proposed; }
                revision++;
                status.textContent = "Officer weights restored. Save to apply them.";
                sync();
            });
            suggestion.addEventListener("click", async () => {
                const version = revision;
                const data = new URLSearchParams();
                data.append("__RequestVerificationToken", form.elements.__RequestVerificationToken.value);
                for (const row of chosen()) data.append("criterionIds", row.id);
                busy = true;
                status.textContent = "Generating a suggestion…";
                sync();
                try {
                    const res = await fetch(form.dataset.aiUrl, { method: "POST", body: data, signal: AbortSignal.timeout(35000) });
                    const result = await res.json().catch(() => ({}));
                    if (!res.ok) throw new Error(result.message || (res.status === 429 ? "Too many requests. Try again in a moment." : "AI weights are unavailable. Set the weights manually."));
                    if (version !== revision) { status.textContent = "Your selections changed. Request a fresh suggestion to replace the current values."; return; }
                    for (const row of chosen()) row.weight.value = result[row.id];
                    status.textContent = "Suggestion ready. Review or edit the weights, then save.";
                } catch (error) {
                    status.textContent = error.name === "TimeoutError" ? "The AI request timed out. You can enter weights manually." : error.message;
                } finally {
                    busy = false;
                    sync();
                }
            });
            form.querySelector("[data-weight-tools]").hidden = false;
            sync();
        }
    }
    initialize(document);
    document.addEventListener("workspace:load", event => initialize(event.detail));
})();
