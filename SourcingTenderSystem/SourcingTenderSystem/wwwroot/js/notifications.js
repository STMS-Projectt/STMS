(() => {
    let checking = false;
    const menus = () => [...document.querySelectorAll("[data-header-menu-toggle]")].map(button => ({
        button,
        panel: document.getElementById(button.getAttribute("aria-controls"))
    })).filter(menu => menu.panel);
    const closeMenus = (restoreFocus = false) => {
        for (const menu of menus()) {
            if (!menu.panel.hidden && restoreFocus) menu.button.focus();
            menu.panel.hidden = true;
            menu.button.setAttribute("aria-expanded", "false");
        }
    };
    async function loadRecent(panel) {
        const list = panel.querySelector("[data-recent-notifications]");
        if (!list) return;
        list.textContent = window.stmsT("Loading notifications…");
        try {
            const response = await fetch(panel.dataset.recentUrl, { cache: "no-store" });
            if (!response.ok || response.redirected) throw new Error(window.stmsT("Unavailable"));
            const { items } = await response.json();
            if (panel.hidden) return;
            list.replaceChildren();
            if (!Array.isArray(items) || items.length === 0) {
                const empty = document.createElement("p");
                empty.className = "stms-notification-empty";
                empty.textContent = window.stmsT("No notifications yet.");
                list.append(empty);
                return;
            }
            for (const item of items.slice(0, 5)) {
                const row = document.createElement("article");
                row.className = "stms-notification-item" + (item.isRead ? "" : " is-unread");
                const title = document.createElement("strong");
                title.textContent = item.title || window.stmsT("Notification");
                const message = document.createElement("p");
                message.textContent = item.message || "";
                const time = document.createElement("time");
                const date = new Date(item.createdAt);
                time.textContent = Number.isNaN(date.valueOf()) ? "" : date.toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });
                row.append(title, message, time);
                list.append(row);
            }
        } catch {
            if (!panel.hidden) list.textContent = window.stmsT("Notifications could not be loaded. Open the full list to try again.");
        }
    }
    function openMenu(menu) {
        if (menu.panel.hidden) {
            closeMenus();
            menu.panel.hidden = false;
            menu.button.setAttribute("aria-expanded", "true");
            if (menu.button.dataset.headerMenuToggle === "notifications") loadRecent(menu.panel);
        }
    }
    document.addEventListener("click", event => {
        const trigger = event.target.closest("[data-header-menu-toggle]");
        const menu = menus().find(item => item.button === trigger);
        if (menu) {
            if (menu.panel.hidden) openMenu(menu);
            else closeMenus();
        } else if (!event.target.closest(".stms-header-menu")) closeMenus();
    });
    document.addEventListener("keydown", event => { if (event.key === "Escape") closeMenus(true); });
    document.addEventListener("workspace:load", () => closeMenus());
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
