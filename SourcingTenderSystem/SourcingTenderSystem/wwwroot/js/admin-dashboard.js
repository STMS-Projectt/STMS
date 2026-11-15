(() => {
    if (window.__stmsAdminMotionBound) return;
    window.__stmsAdminMotionBound = true;

    function animateCount(element) {
        const number = element.querySelector("strong[data-count]");
        if (!number) return;
        const target = Number(number.dataset.count);
        if (!Number.isFinite(target) || target <= 0) return;
        const formatter = new Intl.NumberFormat(document.documentElement.lang || "en");
        const duration = 1100;
        let start;
        function frame(now) {
            start ??= now;
            const progress = Math.min((now - start) / duration, 1);
            number.textContent = formatter.format(Math.round(target * (1 - Math.pow(1 - progress, 3))));
            if (progress < 1) requestAnimationFrame(frame);
        }
        requestAnimationFrame(frame);
    }

    function initialize() {
        const main = document.querySelector(".admin-overview")?.closest("main");
        if (!main || main.dataset.adminMotionReady) return;
        main.dataset.adminMotionReady = "true";
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
            ".admin-chart-grid .dashboard-card, .admin-secondary .dashboard-card, .admin-total-card"
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
                animateCount(entry.target);
                observer.unobserve(entry.target);
            }
        }, { threshold: 0.12, rootMargin: "0px 0px -5% 0px" });
        targets.forEach(element => observer.observe(element));
    }

    initialize();
    document.addEventListener("workspace:load", initialize);
})();
