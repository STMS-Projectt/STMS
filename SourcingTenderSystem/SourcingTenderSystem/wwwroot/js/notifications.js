(() => {
    let checking = false;
    async function refresh() {
        const badge = document.querySelector("[data-notification-count]");
        if (!badge || document.hidden || checking) return;
        checking = true;
        try {
            const response = await fetch(badge.dataset.notificationCount, { cache: "no-store" });
            if (!response.ok || response.redirected) return;
            const { count } = await response.json();
            if (!Number.isInteger(count) || count < 0) return;
            badge.textContent = count > 99 ? "99+" : String(count);
            badge.setAttribute("aria-label", count + " unread");
            badge.hidden = count === 0;
        } catch { /* A later refresh will retry. */ }
        finally { checking = false; }
    }
    window.setInterval(refresh, 30000);
    document.addEventListener("visibilitychange", refresh);
})();
