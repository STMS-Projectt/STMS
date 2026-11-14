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
                status.textContent = "Voice search is unavailable in this browser. You can type your search.";
                return;
            }
            let recognition;
            let listening = false;
            let outcome = false;
            const idle = () => {
                listening = false;
                button.classList.remove("is-listening");
                button.setAttribute("aria-label", "Search by voice");
                button.title = "Search by voice";
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
                            ? "Search terms added. Select Search to apply."
                            : "Search updated.";
                    };
                    recognition.onerror = event => {
                        outcome = true;
                        status.textContent = ["not-allowed", "service-not-allowed"].includes(event.error)
                            ? "Microphone permission denied. You can type your search."
                            : "Voice search could not capture speech. Try again or type your search.";
                    };
                    recognition.onend = () => {
                        idle();
                        if (!outcome) status.textContent = "No search terms captured.";
                        if (active?.button === button) active = null;
                    };
                    outcome = false;
                    recognition.start();
                    active = { button, recognition };
                    listening = true;
                    button.classList.add("is-listening");
                    button.setAttribute("aria-label", "Stop listening");
                    button.title = "Stop listening";
                    status.textContent = "Listening…";
                } catch {
                    idle();
                    status.textContent = "Could not start voice search. Try again or type your search.";
                }
            });
        });
    }
    initialize(document);
    document.addEventListener("workspace:load", event => initialize(event.detail));
    window.addEventListener("pagehide", () => active?.recognition.abort());
})();
