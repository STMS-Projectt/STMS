(() => {
    if (window.__stmsAdminMotionBound) return;
    window.__stmsAdminMotionBound = true;

    function initialize() {
        const main = document.querySelector(".admin-overview")?.closest("main");
        if (!main || main.dataset.adminMotionReady) return;
        main.dataset.adminMotionReady = "true";
        const chart = main.querySelector("[data-procurement-chart]");
        chart?.addEventListener("click", event => {
            const point = event.target.closest("[data-month]");
            if (!point) return;
            chart.querySelector('[data-month][aria-pressed="true"]')?.setAttribute("aria-pressed", "false");
            point.setAttribute("aria-pressed", "true");
            chart.querySelector("[data-chart-selection]").textContent =
                window.stmsFormat("{0} · {1} requests · {2} tenders", point.dataset.month, point.dataset.requests, point.dataset.tenders);
        });
        const cards = main.querySelectorAll(".admin-metric-link, .admin-chart-grid .dashboard-card, .admin-secondary .dashboard-card, .admin-total-card");
        cards.forEach(card => {
            card.addEventListener("pointermove", event => {
                if (event.pointerType === "touch") return;
                const bounds = card.getBoundingClientRect();
                card.style.setProperty("--glow-x", `${event.clientX - bounds.left}px`);
                card.style.setProperty("--glow-y", `${event.clientY - bounds.top}px`);
            }, { passive: true });
        });
        if (matchMedia("(prefers-reduced-motion: reduce)").matches || !window.IntersectionObserver) return;

        const targets = [...main.querySelectorAll(
            ".admin-overview, .admin-analytics .section-heading, .admin-analytics .metric, " +
            ".admin-chart-grid .dashboard-card, .admin-secondary .dashboard-card, .admin-total-card, " +
            ".department-recent .record-card, .department-status-row, .sourcing-queue-row"
        )];
        targets.forEach((element, index) => {
            element.classList.add("admin-reveal");
            element.style.setProperty("--reveal-delay", `${(index % 4) * 75}ms`);
        });
        main.classList.add("admin-motion-ready");
        const observer = new IntersectionObserver(entries => {
            for (const entry of entries) {
                if (!entry.isIntersecting) continue;
                entry.target.classList.add("is-visible");
                observer.unobserve(entry.target);
            }
        }, { threshold: 0.12, rootMargin: "0px 0px -5% 0px" });
        targets.forEach(element => observer.observe(element));
    }

    initialize();
    document.addEventListener("workspace:load", initialize);
})();
