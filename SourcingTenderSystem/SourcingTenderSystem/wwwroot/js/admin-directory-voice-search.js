(() => {
    let active;
    function initialize(root) {
        if (active && !active.button.isConnected) {
            active.recognition.abort();
            active = null;
        }
        root.querySelectorAll("[data-directory]").forEach(directory => {
            const input = directory.querySelector("[data-filter-search]");
            const button = directory.querySelector("[data-directory-voice-search]");
            const status = directory.querySelector("[data-voice-status]");
            if (!input || !button || !status || button.dataset.voiceReady) return;
            button.dataset.voiceReady = "true";
            button.hidden = false;
            const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
            if (!Recognition) {
                button.disabled = true;
                status.textContent = window.stmsT("Voice search is unavailable in this browser. You can type your search.");
                return;
            }
            let recognition;
            let listening = false;
            let outcome = false;
            const idle = () => {
                listening = false;
                button.classList.remove("is-listening");
                button.setAttribute("aria-label", window.stmsT("Search by voice"));
                button.title = window.stmsT("Search by voice");
            };
            button.addEventListener("click", () => {
                if (listening) { recognition.stop(); return; }
                if (active && active.button !== button) active.recognition.abort();
                try {
                    recognition ??= new Recognition();
                    recognition.lang = document.documentElement.lang || "en-US";
                    recognition.interimResults = false;
                    recognition.maxAlternatives = 1;
                    recognition.onresult = event => {
                        if (!input.isConnected) return;
                        outcome = true;
                        input.value = event.results[0][0].transcript.trim();
                        input.dispatchEvent(new Event("input", { bubbles: true }));
                        status.textContent = input.form
                            ? window.stmsT("Search terms added. Select Search to apply.")
                            : window.stmsT("Search updated.");
                    };
                    recognition.onerror = event => {
                        outcome = true;
                        status.textContent = ["not-allowed", "service-not-allowed"].includes(event.error)
                            ? window.stmsT("Microphone permission denied. You can type your search.")
                            : window.stmsT("Voice search could not capture speech. Try again or type your search.");
                    };
                    recognition.onend = () => {
                        idle();
                        if (!outcome) status.textContent = window.stmsT("No search terms captured.");
                        if (active?.button === button) active = null;
                    };
                    outcome = false;
                    recognition.start();
                    active = { button, recognition };
                    listening = true;
                    button.classList.add("is-listening");
                    button.setAttribute("aria-label", window.stmsT("Stop listening"));
                    button.title = window.stmsT("Stop listening");
                    status.textContent = window.stmsT("Listening…");
                } catch {
                    idle();
                    status.textContent = window.stmsT("Could not start voice search. Try again or type your search.");
                }
            });
        });
    }
    initialize(document);
    document.addEventListener("workspace:load", event => initialize(event.detail));
    document.querySelector("#workspace-dialog")?.addEventListener("close", event => {
        if (active && (!active.button.isConnected || event.currentTarget.contains(active.button))) {
            active.recognition.abort();
            active = null;
        }
    });
    window.addEventListener("pagehide", () => active?.recognition.abort());
})();
