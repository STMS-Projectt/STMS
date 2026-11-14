(() => {
    let checking = false;
    async function refresh() {
        const badges = document.querySelectorAll("[data-notification-count]");
        const badge = badges[0];
        if (!badge || document.hidden || checking) return;
        checking = true;
        try {
            const response = await fetch(badge.dataset.notificationCount, { cache: "no-store" });
            if (!response.ok || response.redirected) return;
            const { count } = await response.json();
            if (!Number.isInteger(count) || count < 0) return;
            badges.forEach(item => {
                item.textContent = count > 99 ? "99+" : String(count);
                item.setAttribute("aria-label", count + " unread");
                item.hidden = count === 0;
            });
        } catch { /* A later refresh will retry. */ }
        finally { checking = false; }
    }
    refresh();
    window.setInterval(refresh, 30000);
    document.addEventListener("visibilitychange", refresh);
})();
