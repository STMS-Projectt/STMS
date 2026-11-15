(() => {
    const root = document.documentElement;
    const media = matchMedia("(prefers-color-scheme: dark)");
    const catalog = JSON.parse(document.getElementById("stms-ui-text")?.textContent || "{}");
    const translate = window.stmsT = text => catalog[String(text).toLowerCase()] || text;
    window.stmsFormat = (format, ...args) => translate(format).replace(/\{(\d+)\}/g, (_, index) => args[index] ?? "");
    let choice;
    try { choice = localStorage.getItem("stms.theme"); } catch {}
    function apply() {
        const dark = choice === "dark" || choice !== "light" && media.matches;
        root.dataset.bsTheme = dark ? "dark" : "light";
        for (const button of document.querySelectorAll("[data-theme-toggle]")) {
            button.setAttribute("aria-pressed", String(dark));
            button.title = translate(dark ? "Switch to light mode" : "Switch to dark mode");
        }
    }
    apply();
    media.addEventListener("change", apply);
    document.addEventListener("DOMContentLoaded", apply);
    document.addEventListener("workspace:load", apply);
    document.addEventListener("submit", event => {
        const path = event.target.matches?.(".display-preferences form") && event.target.elements.returnUrl;
        if (path) path.value = location.pathname + location.search + location.hash;
    });
    document.addEventListener("click", event => {
        if (!(event.target instanceof Element) || !event.target.closest("[data-theme-toggle]")) return;
        choice = root.dataset.bsTheme === "dark" ? "light" : "dark";
        try { localStorage.setItem("stms.theme", choice); } catch {}
        apply();
    });
})();
