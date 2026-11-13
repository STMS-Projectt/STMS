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
            if (focus) (open ? nav.querySelector("a") : toggle).focus();
        };
        setOpen(false, false);
        compact.addEventListener("change", () => setOpen(false, false));
        toggle.addEventListener("click", () => setOpen(!nav.classList.contains("is-open")));
        scrim.addEventListener("click", () => setOpen(false));
        document.addEventListener("keydown", event => {
            if (!nav.classList.contains("is-open")) return;
            if (event.key === "Escape") setOpen(false);
            if (event.key === "Tab") {
                const links = [...nav.querySelectorAll("a,button")];
                const first = links[0], last = links.at(-1);
                if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
                else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
            }
        });
    }
    document.querySelectorAll("[data-dismiss-notice]").forEach(button =>
        button.addEventListener("click", () => { button.closest(".notice").remove(); document.querySelector("main").focus(); }));

    document.querySelectorAll("[data-directory]").forEach(directory => {
        const search = directory.querySelector("[data-filter-search]");
        if (!search) return;
        const rows = [...directory.querySelectorAll("tbody tr")];
        const count = directory.querySelector("[data-result-count]");
        const empty = directory.querySelector("[data-filter-empty]");
        const table = directory.querySelector(".directory-table");
        if (!empty || !table || !count) return;
        const apply = () => {
            const words = search.value.toLocaleLowerCase().trim().split(/\s+/).filter(Boolean);
            let shown = 0;
            for (const row of rows) {
                const content = row.textContent.toLocaleLowerCase();
                const visible = words.every(word => content.includes(word));
                row.hidden = !visible;
                if (visible) shown++;
            }
            count.textContent = shown + (shown === 1 ? " record" : " records");
            empty.hidden = shown !== 0;
            table.hidden = shown === 0;
        };
        search.addEventListener("input", apply);
        empty.querySelector("button").addEventListener("click", () => { search.value = ""; apply(); search.focus(); });
        apply();
    });

    document.querySelectorAll("[data-password-toggle]").forEach(button => {
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

    document.querySelectorAll("[data-upload-zone]").forEach(zone => {
        const input = zone.querySelector('input[type="file"]');
        const status = zone.querySelector("[data-file-status]");
        if (!input || !status) return;
        const sync = () => {
            const file = input.files[0];
            const error = file && (file.size === 0 || file.size > 10485760 || !/\.pdf$/i.test(file.name));
            input.setCustomValidity(error ? "Choose a nonempty PDF no larger than 10 MB." : "");
            status.textContent = error ? input.validationMessage : file ? file.name + " · " + Math.max(1, Math.round(file.size / 1024)) + " KB" : "No file selected.";
        };
        input.addEventListener("change", sync);
        zone.addEventListener("dragover", event => { event.preventDefault(); zone.classList.add("is-dragging"); });
        zone.addEventListener("dragleave", () => zone.classList.remove("is-dragging"));
        zone.addEventListener("drop", event => {
            event.preventDefault();
            zone.classList.remove("is-dragging");
            if (event.dataTransfer.files.length === 1) { input.files = event.dataTransfer.files; sync(); }
            else { status.textContent = "Drop one PDF at a time."; }
        });
    });
})();
