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

    function setupFilters(main) {
        const directory = main.querySelector(".dept-directory");
        if (!directory) return;
        const dock = directory.querySelector(".dept-filter-dock");
        directory.append(dock);
        const updateDockVisibility = () => {
            if (!directory.isConnected) return;
            const heading = directory.querySelector(".dept-directory-heading").getBoundingClientRect();
            const section = directory.getBoundingClientRect();
            dock.classList.toggle("is-in-view", heading.bottom < innerHeight - 72 && section.bottom > 0);
        };
        window.addEventListener("scroll", updateDockVisibility, { passive: true });
        window.addEventListener("resize", updateDockVisibility);
        updateDockVisibility();
        const menus = [...directory.querySelectorAll("[data-dept-menu]")];
        const closeMenus = () => menus.forEach(menu => {
            menu.querySelector(".dept-filter-popover").hidden = true;
            menu.querySelector(".dept-dock-button").setAttribute("aria-expanded", "false");
        });
        const openMenu = menu => {
            closeMenus();
            menu.querySelector(".dept-filter-popover").hidden = false;
            menu.querySelector(".dept-dock-button").setAttribute("aria-expanded", "true");
        };
        menus.forEach(menu => {
            const trigger = menu.querySelector(".dept-dock-button");
            menu.addEventListener("pointerenter", event => { if (event.pointerType !== "touch") openMenu(menu); });
            trigger.addEventListener("click", () => openMenu(menu));
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
            directory.querySelector("[data-dept-search-open]").classList.toggle("is-active", !!directory.querySelector("[data-filter-search]").value);
        };
        directory.querySelector("[data-filter-reset]").addEventListener("click", () => { closeMenus(); queueMicrotask(sync); });
        const dialog = directory.querySelector("[data-dept-search-dialog]");
        const search = directory.querySelector("[data-filter-search]");
        directory.querySelector("[data-dept-search-open]").addEventListener("click", () => {
            closeMenus();
            dialog.showModal();
            search.focus();
        });
        directory.querySelector("[data-dept-search-close]").addEventListener("click", () => dialog.close());
        dialog.addEventListener("click", event => { if (event.target === dialog) dialog.close(); });
        search.addEventListener("input", sync);
        document.addEventListener("click", event => { if (!event.target.closest("[data-dept-menu]")) closeMenus(); });
        document.addEventListener("keydown", event => { if (event.key === "Escape") closeMenus(); });
        sync();
    }

    function initialize() {
        const main = document.querySelector(".dept-overview")?.closest("main");
        if (!main || main.dataset.deptMotionReady) return;
        main.dataset.deptMotionReady = "true";
        setupFilters(main);
        if (matchMedia("(prefers-reduced-motion: reduce)").matches || !window.IntersectionObserver) return;

        const cards = main.querySelectorAll(".stms-metrics .metric, .dept-directory .record-card");
        cards.forEach(card => card.addEventListener("pointermove", event => {
            if (event.pointerType === "touch") return;
            const bounds = card.getBoundingClientRect();
            card.style.setProperty("--glow-x", `${event.clientX - bounds.left}px`);
            card.style.setProperty("--glow-y", `${event.clientY - bounds.top}px`);
        }, { passive: true }));

        const targets = [...main.querySelectorAll(".dept-overview, .stms-metrics .metric, .dept-directory, .dept-directory .record-card")];
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
    }

    initialize();
    document.addEventListener("workspace:load", initialize);
})();
