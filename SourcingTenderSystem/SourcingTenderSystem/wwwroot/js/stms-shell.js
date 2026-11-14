(() => {
    const header = document.querySelector(".stms-topbar");
    const input = document.querySelector("#stms-workspace-search");
    const results = document.querySelector("#stms-search-results");
    if (!header || !input || !results) return;
    const navSelector = "#workspace-nav .side-link, #workspace-nav .side-bottom form button";
    function label(el) {
        if (el.getAttribute("aria-label")) return el.getAttribute("aria-label");
        return [...el.childNodes].filter(n => n.nodeType === Node.TEXT_NODE)
            .map(n => n.textContent.trim()).filter(Boolean).join(" ") || el.textContent.trim();
    }
    const voice = header.querySelector("[data-workspace-voice]");
    const voiceStatus = header.querySelector("[data-workspace-voice-status]");
    const voiceMessage = header.querySelector("[data-workspace-voice-message]");
    let recognition;
    function announceVoice(message, visible = false) {
        voiceStatus.textContent = message;
        voiceMessage.textContent = message;
        voiceMessage.hidden = !visible;
    }
    function stopVoice() {
        const running = recognition;
        recognition = null;
        running?.abort();
        voice?.setAttribute("aria-pressed", "false");
    }
    if (voice) {
        voice.hidden = false;
        voice.addEventListener("click", () => {
            if (recognition) { stopVoice(); announceVoice("Voice search stopped."); return; }
            const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
            if (!Recognition) {
                announceVoice("Voice search is unavailable in this browser. Type your search instead.", true);
                return;
            }
            let session;
            try {
                session = recognition = new Recognition();
                session.lang = document.documentElement.lang || "en-US";
                session.onstart = () => {
                    if (recognition !== session) return;
                    results.hidden = true;
                    voice.setAttribute("aria-pressed", "true");
                    announceVoice("Listening…", true);
                };
                session.onresult = event => {
                    if (recognition !== session) return;
                    input.value = event.results[0][0].transcript;
                    input.focus();
                    search();
                    announceVoice("Search results updated.");
                };
                session.onerror = event => {
                    if (recognition !== session) return;
                    announceVoice(event.error === "not-allowed" || event.error === "service-not-allowed"
                        ? "Microphone access was denied. Type your search instead."
                        : "Voice search could not hear a query. Try again or type your search.", true);
                };
                session.onend = () => {
                    if (recognition !== session) return;
                    recognition = null;
                    voice.setAttribute("aria-pressed", "false");
                    if (voiceStatus.textContent === "Listening…") announceVoice("Voice search ended.");
                };
                session.start();
            } catch {
                recognition = null;
                announceVoice("Could not start voice search. Type your search instead.", true);
            }
        });
        window.addEventListener("pagehide", stopVoice);
    }
    function positionRail() {
        document.body.style.setProperty("--stms-rail-top", Math.max(14, Math.ceil(header.getBoundingClientRect().bottom + 14)) + "px");
    }
    window.addEventListener("scroll", positionRail, { passive: true });
    window.addEventListener("resize", positionRail);
    positionRail();
    function setup() {
        if (voice) { stopVoice(); announceVoice(""); }
        document.querySelectorAll(navSelector).forEach(el => el.setAttribute("aria-label", label(el)));
        results.hidden = true;
        input.value = "";
    }
    function search() {
        const term = input.value.trim().toLowerCase();
        results.replaceChildren();
        results.hidden = !term;
        if (!term) return;
        const seen = new Set();
        for (const el of document.querySelectorAll("#workspace-nav a.side-link[href]")) {
            const name = label(el);
            if (!name.toLowerCase().includes(term) || seen.has(el.href)) continue;
            seen.add(el.href);
            const link = document.createElement("a");
            link.href = el.href;
            link.textContent = name;
            if (el.hasAttribute("data-workspace-dialog")) link.setAttribute("data-workspace-dialog", "");
            results.append(link);
        }
        if (!results.childElementCount) {
            const message = document.createElement("p");
            message.textContent = "No matching workspace pages.";
            results.append(message);
        }
    }
    input.addEventListener("input", () => { if (voice) announceVoice(""); search(); });
    input.addEventListener("focus", search);
    input.addEventListener("keydown", e => {
        const first = results.querySelector("a");
        if (!results.hidden && first && (e.key === "Enter" || e.key === "ArrowDown")) {
            e.preventDefault();
            if (e.key === "Enter") first.click(); else first.focus();
        }
    });
    document.addEventListener("click", e => {
        if (!e.target.closest(".stms-search") || e.target.closest("#stms-search-results a")) results.hidden = true;
    });
    document.addEventListener("keydown", e => {
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k" && !document.querySelector("dialog[open]")) {
            e.preventDefault(); input.focus(); input.select();
        }
        if (e.key === "Escape") {
            if (voice) { stopVoice(); announceVoice(""); }
            if (!results.hidden) { input.focus(); results.hidden = true; }
        }
    });
    document.addEventListener("workspace:load", setup);
    setup();
})();
