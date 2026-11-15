(() => {
    if (window.__stmsDepartmentsMotionBound) return;
    window.__stmsDepartmentsMotionBound = true;

    function animateNumber(number) {
        const target = Number(number.textContent.replace(/[^\d.-]/g, ""));
        if (!Number.isFinite(target) || target <= 0) return;
        const formatter = new Intl.NumberFormat(document.documentElement.lang || "en");
        let start;
        const tick = now => {
            start ??= now;
            const progress = Math.min((now - start) / 950, 1);
            number.textContent = formatter.format(Math.round(target * (1 - (1 - progress) ** 3)));
            if (progress < 1) requestAnimationFrame(tick);
        };
        requestAnimationFrame(tick);
    }

    function upgradeDock(directory) {
        const panel = directory.querySelector(".directory-filter-panel");
        if (!panel) return;
        const dock = document.createElement("div");
        dock.className = "directory-filter-panel dept-filter-dock";
        dock.setAttribute("aria-label", "Record filters");
        const paths = {
            Status: "M4 7h16 M7 12h10 M10 17h4",
            Sort: "M5 7h14 M5 12h10 M5 17h6",
            Search: "M16 16l4 4 M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0z",
            Pages: "M14 2H6v20h12V6z M14 2v5h4 M8 13h8m-8 4h8",
            Reset: "M20 7v5h-5 M4 17v-5h5 M5.5 9a7 7 0 0 1 12-2L20 12 M4 12l2.5 5a7 7 0 0 0 12-2"
        };
        const decorate = (button, label) => {
            button.className = "dept-dock-button";
            button.setAttribute("aria-label", label);
            const ns = "http://www.w3.org/2000/svg";
            const icon = document.createElementNS(ns, "svg");
            icon.setAttribute("class", "icon");
            icon.setAttribute("aria-hidden", "true");
            icon.setAttribute("viewBox", "0 0 24 24");
            const path = document.createElementNS(ns, "path");
            path.setAttribute("d", paths[label]);
            icon.append(path);
            const caption = document.createElement("span");
            caption.textContent = label;
            button.replaceChildren(icon, caption);
        };
        const menu = (label, node) => {
            if (!node) return;
            const group = document.createElement("div");
            group.className = "dept-filter-menu";
            group.dataset.deptMenu = "";
            const button = document.createElement("button");
            button.type = "button";
            decorate(button, label);
            button.setAttribute("aria-expanded", "false");
            const popover = document.createElement("div");
            popover.className = "dept-filter-popover admin-dock-panel";
            popover.hidden = true;
            popover.append(node);
            group.append(button, popover);
            dock.append(group);
        };
        menu("Status", panel.querySelector(".directory-status-options"));
        menu("Sort", panel.querySelector("label:has([data-filter-sort])"));
        const search = panel.querySelector("form, .directory-search");
        menu("Search", search?.hidden ? null : search);
        menu("Pages", directory.querySelector("[data-register-pages]"));
        const pages = directory.querySelector("[data-dept-pagination]");
        if (pages) dock.append(pages);
        const reset = panel.querySelector("[data-filter-reset]");
        if (reset) { decorate(reset, "Reset"); dock.append(reset); }
        const clear = panel.querySelector("[data-directory-clear]");
        if (clear) dock.append(clear);
        if (!dock.children.length) return;
        panel.hidden = true;
        directory.append(dock);
        return dock;
    }

    function setupFilters(main, signal) {
        const directory = main.querySelector(".dept-directory[data-directory]");
        if (!directory) return;
        const dock = directory.querySelector(".dept-filter-dock") || upgradeDock(directory);
        if (!dock) return;
        directory.append(dock);
        const updateDockVisibility = () => {
            if (!directory.isConnected) return;
            const heading = (directory.querySelector(".dept-directory-heading") || main.querySelector(".page-heading") || directory).getBoundingClientRect();
            const section = directory.getBoundingClientRect();
            dock.classList.toggle("is-in-view", heading.bottom < innerHeight - 72 && section.bottom > 0);
        };
        window.addEventListener("scroll", updateDockVisibility, { passive: true, signal });
        window.addEventListener("resize", updateDockVisibility, { signal });
        updateDockVisibility();
        const menus = [...directory.querySelectorAll("[data-dept-menu]")];
        const closeMenus = () => menus.forEach(menu => {
            menu.querySelector(".dept-filter-popover").hidden = true;
            menu.querySelector(".dept-dock-button").setAttribute("aria-expanded", "false");
        });
        const openMenu = menu => {
            closeMenus();
            const popover = menu.querySelector(".dept-filter-popover");
            popover.hidden = false;
            const center = menu.getBoundingClientRect().left + menu.offsetWidth / 2;
            const half = popover.offsetWidth / 2;
            const edge = document.documentElement.clientWidth;
            popover.style.translate = `${Math.max(12 + half, Math.min(center, edge - half - 12)) - center}px 0`;
            menu.querySelector(".dept-dock-button").setAttribute("aria-expanded", "true");
        };
        menus.forEach(menu => {
            const trigger = menu.querySelector(".dept-dock-button");
            menu.addEventListener("pointerenter", event => { if (event.pointerType !== "touch") openMenu(menu); });
            trigger.addEventListener("click", () => {
                openMenu(menu);
                menu.querySelector("input[type=search]")?.focus();
            });
            menu.querySelectorAll("[data-dept-select]").forEach(option => option.addEventListener("click", () => {
                const select = directory.querySelector(`[data-filter-${option.dataset.deptSelect}]`);
                select.value = option.dataset.value;
                select.dispatchEvent(new Event("change", { bubbles: true }));
                sync();
                closeMenus();
            }));
            menu.querySelectorAll("[data-status-choice]").forEach(option => option.addEventListener("click", () => {
                queueMicrotask(sync);
                closeMenus();
            }));
        });
        const sync = () => {
            directory.querySelectorAll("[data-dept-select]").forEach(option => {
                const select = directory.querySelector(`[data-filter-${option.dataset.deptSelect}]`);
                option.setAttribute("aria-pressed", String(select.value === option.dataset.value));
            });
            menus.forEach(menu => menu.querySelector(".dept-dock-button").classList.toggle("is-active", !!menu.querySelector('[aria-pressed="true"][data-status-choice]:not([data-status-choice=""]), [aria-pressed="true"][data-dept-select]:not([data-value=""])')));
            directory.querySelector("[data-dept-search-open]")?.classList.toggle("is-active", !!directory.querySelector("[data-filter-search]")?.value);
        };
        directory.querySelector("[data-filter-reset]")?.addEventListener("click", () => { closeMenus(); queueMicrotask(sync); });
        const dialog = directory.querySelector("[data-dept-search-dialog]");
        const search = directory.querySelector("[data-filter-search]");
        directory.querySelector("[data-dept-search-open]")?.addEventListener("click", () => {
            closeMenus();
            dialog.showModal();
            search.focus();
        });
        directory.querySelector("[data-dept-search-close]")?.addEventListener("click", () => dialog.close());
        dialog?.addEventListener("click", event => { if (event.target === dialog) dialog.close(); });
        search?.addEventListener("input", sync);
        document.addEventListener("click", event => { if (!event.target.closest("[data-dept-menu]")) closeMenus(); }, { signal });
        document.addEventListener("keydown", event => { if (event.key === "Escape") closeMenus(); }, { signal });
        sync();
    }

    let pageEvents;
    function initialize(event) {
        const workspace = document.querySelector("[data-workspace-frame]")?.dataset.workspaceFrame;
        if (event?.detail?.closest?.("dialog")) return;
        const main = document.querySelector("main#main");
        if (workspace !== "admin" && workspace !== "department") return;
        if (workspace === "department" && main?.querySelector(".department-analytics")) {
            pageEvents?.abort();
            return;
        }
        if (!main || main.dataset.deptMotionReady) return;
        main.dataset.deptMotionReady = "true";
        pageEvents?.abort();
        pageEvents = new AbortController();
        setupFilters(main, pageEvents.signal);
        if (matchMedia("(prefers-reduced-motion: reduce)").matches || !window.IntersectionObserver) return;

        const cards = main.querySelectorAll(".stms-metrics .metric, .dept-directory .record-card");
        cards.forEach(card => card.addEventListener("pointermove", event => {
            if (event.pointerType === "touch") return;
            const bounds = card.getBoundingClientRect();
            card.style.setProperty("--glow-x", `${event.clientX - bounds.left}px`);
            card.style.setProperty("--glow-y", `${event.clientY - bounds.top}px`);
        }, { passive: true }));

        const targets = [...main.querySelectorAll(".page-heading, .stms-metrics .metric, .admin-total-card, .dashboard-card, .detail-card, .form-card, .record-card, .account-hero")];
        targets.forEach((element, index) => {
            element.classList.add("dept-reveal");
            element.style.setProperty("--dept-delay", `${(index % 4) * 70}ms`);
        });
        main.classList.add("dept-motion-ready");
        const observer = new IntersectionObserver(entries => {
            for (const entry of entries) {
                if (!entry.isIntersecting) continue;
                entry.target.classList.add("is-visible");
                if (entry.target.matches(".stms-metrics .metric")) animateNumber(entry.target.querySelector("strong"));
                if (entry.target.matches(".record-card")) {
                    const count = entry.target.querySelector(".dept-staff-count");
                    if (count) animateNumber(count);
                }
                observer.unobserve(entry.target);
            }
        }, { threshold: 0.08, rootMargin: "0px 0px -4% 0px" });
        targets.forEach(element => observer.observe(element));
        pageEvents.signal.addEventListener("abort", () => observer.disconnect());
    }

    initialize();
    document.addEventListener("workspace:load", initialize);
})();
