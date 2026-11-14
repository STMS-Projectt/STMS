(() => {
    const nav = document.querySelector("#workspace-nav");
    const toggle = document.querySelector("[data-nav-toggle]");
    const scrim = document.querySelector("[data-nav-close]");
    const site = document.querySelector(".site-body");
    if (nav && toggle && scrim && site) {
        const compact = matchMedia("(max-width: 1000px)");
        document.body.classList.add("has-nav");
        const setOpen = (open, focus = true) => {
            nav.classList.toggle("is-open", open);
            nav.inert = compact.matches && !open;
            site.inert = open;
            scrim.hidden = !open;
            document.body.classList.toggle("nav-open", open);
            toggle.setAttribute("aria-expanded", String(open));
            toggle.setAttribute("aria-label", open ? "Close navigation" : "Open navigation");
            if (focus) (open ? nav.querySelector(".side-link") : toggle)?.focus();
        };
        setOpen(false, false);
        compact.addEventListener("change", () => setOpen(false, false));
        toggle.addEventListener("click", () => setOpen(!nav.classList.contains("is-open")));
        scrim.addEventListener("click", () => setOpen(false));
        document.addEventListener("keydown", event => {
            if (!nav.classList.contains("is-open")) return;
            if (event.key === "Escape") setOpen(false);
            if (event.key === "Tab") {
                const links = [...nav.querySelectorAll("a,button")].filter(el => el.getClientRects().length && !el.disabled);
                const first = links[0], last = links.at(-1);
                if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
                else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
            }
        });
    }
    const initialize = root => {
    root.querySelectorAll("[data-dismiss-notice]").forEach(button =>
        button.addEventListener("click", () => { button.closest(".notice").remove(); document.querySelector("main").focus(); }));

    root.querySelectorAll("[data-directory]").forEach(directory => {
        const search = directory.querySelector("[data-filter-search]");
        if (!search) return;
        const cards = directory.querySelector("[data-record-list]");
        const rows = [...(cards ? cards.querySelectorAll("[data-record]") : directory.querySelectorAll("tbody tr[data-status]"))];
        const count = directory.querySelector("[data-result-count]");
        const empty = directory.querySelector("[data-filter-empty]");
        const results = cards || directory.querySelector(".table-responsive, .directory-table");
        if (!empty || !results || !count) return;
        const choices = [...directory.querySelectorAll("[data-status-choice]")];
        let status = "";
        const selectStatus = value => {
            status = value;
            choices.forEach(button => button.setAttribute("aria-pressed", String(button.dataset.statusChoice === status)));
        };
        const department = directory.querySelector("[data-filter-department]");
        const staff = directory.querySelector("[data-filter-staff]");
        const sort = directory.querySelector("[data-filter-sort]");
        const apply = () => {
            if (sort) {
                const ordered = sort.value === "name" ? [...rows].sort((a, b) => (a.dataset.sortName || "").localeCompare(b.dataset.sortName || ""))
                    : sort.value === "recent" ? [...rows].sort((a, b) => (b.dataset.sortDate || "").localeCompare(a.dataset.sortDate || ""))
                    : sort.value === "desc" ? [...rows].reverse() : sort.value === "staff"
                    ? [...rows].sort((a, b) => Number(b.dataset.staff) - Number(a.dataset.staff)) : rows;
                (cards || rows[0]?.parentElement)?.append(...ordered);
            }
            const words = search.value.toLocaleLowerCase().trim().split(/\s+/).filter(Boolean);
            let shown = 0;
            for (const row of rows) {
                const content = row.textContent.toLocaleLowerCase();
                const visible = words.every(word => content.includes(word)) &&
                    (!status || status === row.dataset.status) &&
                    (!department?.value || department.value === row.dataset.department) &&
                    (!staff?.value || (staff.value === "staffed") === (Number(row.dataset.staff) > 0));
                row.hidden = !visible;
                if (visible) shown++;
            }
            count.textContent = shown + (shown === 1 ? " record" : " records");
            empty.hidden = shown !== 0;
            results.hidden = shown === 0;
        };
        search.addEventListener("input", apply);
        const reset = () => {
            search.value = "";
            selectStatus("");
            for (const select of [department, staff, sort]) if (select) select.value = "";
            apply();
            search.focus();
        };
        choices.forEach(button => button.addEventListener("click", () => {
            selectStatus(button.dataset.statusChoice);
            apply();
        }));
        for (const select of [department, staff, sort]) select?.addEventListener("change", apply);
        empty.querySelector("button").addEventListener("click", reset);
        directory.querySelector("[data-filter-reset]")?.addEventListener("click", reset);
        apply();
    });

    root.querySelectorAll("[data-password-toggle]").forEach(button => {
        const input = document.getElementById(button.getAttribute("aria-controls"));
        if (!input) return;
        button.hidden = false;
        button.addEventListener("click", () => {
            const show = input.type === "password";
            input.type = show ? "text" : "password";
            button.textContent = show ? "Hide" : "Show";
            button.setAttribute("aria-pressed", String(show));
        });
    });

    root.querySelectorAll("[data-upload-zone]").forEach(zone => {
        const input = zone.querySelector('input[type="file"]');
        const status = zone.querySelector("[data-file-status]");
        if (!input || !status) return;
        const sync = () => {
            const selected = Array.from(input.files);
            const file = selected[0];
            const error = (input.multiple && selected.length > 5) || selected.some(file => file.size === 0 || file.size > 10485760 || !/\.pdf$/i.test(file.name));
            input.setCustomValidity(error ? "Choose a nonempty PDF no larger than 10 MB." : "");
            status.textContent = error ? input.validationMessage : selected.length ? selected.map(file => file.name).join(", ") : "No file selected.";
        };
        input.addEventListener("change", sync);
        zone.addEventListener("dragover", event => { event.preventDefault(); zone.classList.add("is-dragging"); });
        zone.addEventListener("dragleave", () => zone.classList.remove("is-dragging"));
        zone.addEventListener("drop", event => {
            event.preventDefault();
            zone.classList.remove("is-dragging");
            if (event.dataTransfer.files.length === 1 || (input.multiple && event.dataTransfer.files.length <= 5)) { input.files = event.dataTransfer.files; sync(); }
            else { status.textContent = "Drop one PDF at a time."; }
        });
    });
    };
    initialize(document);
    document.addEventListener("workspace:load", event => initialize(event.detail));
})();
