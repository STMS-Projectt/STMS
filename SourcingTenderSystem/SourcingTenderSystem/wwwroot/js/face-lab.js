// Face recognition camera component (academic Face++ demo).
// Modes: "enroll" (Sign-in security) and "login" (sign-in page). All decisions are made on the server;
// the browser only captures frames and shows state. The camera is released whenever the flow ends.
(() => {
const STATES = ["idle", "starting", "ready", "checking", "captured", "saving", "success", "failed"];

function bind(root) {
    if (!root || root.dataset.ready) return;
    root.dataset.ready = "true";
    const mode = root.dataset.mode;
    const $ = selector => root.querySelector(selector);
    const stage = $("[data-face-stage]"), video = $("[data-face-video]"), status = $("[data-face-status]");
    const loader = $("[data-face-loader]"), loaderLabel = $("[data-face-loader-label]"), placeholder = $("[data-face-placeholder]");
    const password = $("[data-face-password]"), email = $("[data-face-email]");
    const start = $("[data-face-start]"), capture = $("[data-face-capture]"), save = $("[data-face-save]");
    const retry = $("[data-face-retry]"), cancel = $("[data-face-cancel]");
    const antiForgery = root.closest("[data-bio]")?.querySelector('input[name="__RequestVerificationToken"]')?.value ?? "";
    let stream = null, request = null, prompt = null;

    function setState(state, message, tone) {
        root.dataset.state = state;
        stage.dataset.state = state;
        root.querySelectorAll("[data-face-show]").forEach(el => { el.hidden = !el.dataset.faceShow.split(" ").includes(state); });
        const busy = state === "starting" || state === "checking" || state === "saving";
        loader.hidden = !busy;
        if (busy) loaderLabel.textContent = state === "starting" ? "Starting camera…" : state === "saving" ? "Saving…" : "Checking with Face++…";
        placeholder.hidden = !!stream;
        root.querySelectorAll("[data-face-step]").forEach(step => {
            const n = Number(step.dataset.faceStep), current = prompt?.step ?? 0;
            step.classList.toggle("is-current", (state === "ready" || state === "checking") && n === current);
            step.classList.toggle("is-done", state === "captured" || state === "success" || n < current && state !== "idle" && state !== "failed");
        });
        if (message !== undefined) {
            status.hidden = !message;
            status.textContent = message || "";
            status.dataset.tone = tone || "info";
        }
    }

    function release() {
        request?.abort();
        request = null;
        stream?.getTracks().forEach(track => track.stop());
        stream = null;
        video.srcObject = null;
    }
    function reset(message, tone) { release(); prompt = null; setState("idle", message, tone); }
    function fail(message, tone = "error") { release(); prompt = null; setState("failed", message, tone); }

    // Release the camera when the page changes, the component is removed (AJAX navigation) or a dialog closes.
    const lifecycle = new AbortController();
    const observer = new MutationObserver(() => {
        if (!root.isConnected || root.closest("[hidden]")) { if (stream) reset(""); }
        if (!root.isConnected) { lifecycle.abort(); observer.disconnect(); }
    });
    observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ["hidden", "open"] });
    window.addEventListener("pagehide", () => release(), { signal: lifecycle.signal });
    document.addEventListener("visibilitychange", () => { if (document.hidden && stream) reset("Camera stopped because the page was hidden. Start again when ready."); }, { signal: lifecycle.signal });

    function cameraError(error) {
        if (error.name === "NotAllowedError" || error.name === "SecurityError")
            return "Camera permission was denied. Allow camera access for this site in your browser settings, then try again.";
        if (error.name === "NotFoundError" || error.name === "OverconstrainedError") return "No camera was found. Connect a webcam and try again.";
        if (error.name === "NotReadableError") return "The camera is in use by another application. Close it and try again.";
        if (error.name === "NotSupportedError" || error.name === "AbortError" || error.name === "TypeError")
            return "The camera could not be opened in this browser. Allow camera access over HTTPS, or sign in with your password or passkey.";
        return error.message || "The camera could not be started.";
    }

    async function send(url, data, signal) {
        const response = await fetch(url, {
            method: "POST", credentials: "same-origin", signal,
            headers: { "Content-Type": "application/json", "RequestVerificationToken": antiForgery },
            body: JSON.stringify(data)
        });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result) {
            const error = new Error(result?.message || (response.status === 429
                ? "Too many attempts or the free Face++ quota was reached. Wait, or use your password or passkey."
                : response.status === 503 ? "Face recognition is unavailable right now. Use your password or passkey."
                : "Verification failed. Try again."));
            error.status = response.status;
            throw error;
        }
        return result;
    }
    const toneFor = error => error.status === 429 || error.status === 503 ? "warning" : "error";

    function next(value) {
        prompt = value;
        if (value.readyToSave) {
            release();
            setState("captured", "Your frontal face photo was captured. Save it to finish enrollment.", "success");
            return;
        }
        setState("ready", value.instruction, "info");
        capture.disabled = false;
        capture.focus({ preventScroll: true });
    }

    start?.addEventListener("click", async () => {
        if (password && !password.value) { setState(root.dataset.state || "idle", "Enter your current password first.", "error"); password.focus(); return; }
        if (email && !email.value.trim()) { setState(root.dataset.state || "idle", "Enter the email of the account to sign in to.", "error"); email.focus(); return; }
        release();
        prompt = null;
        const current = request = new AbortController();
        setState("starting", "Waiting for camera permission… Allow access in the browser prompt.", "info");
        try {
            if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) throw new Error("Camera access requires HTTPS (or localhost) and a current browser.");
            const opened = await navigator.mediaDevices.getUserMedia({ audio: false, video: { facingMode: "user", width: { ideal: 640 }, height: { ideal: 480 } } });
            if (current.signal.aborted) { opened.getTracks().forEach(track => track.stop()); return; }
            stream = opened;
            video.srcObject = stream;
            await video.play();
            setState("starting", "Camera ready. Preparing the challenge…", "info");
            const body = mode === "login" ? { email: email.value.trim(), returnUrl: root.dataset.returnUrl } : { enroll: true, password: password?.value };
            const result = await send(root.dataset.start, body, current.signal);
            if (password) password.value = "";
            if (request === current) next(result);
        } catch (error) {
            if (current.signal.aborted) return;
            fail(error.status ? error.message : cameraError(error), error.status ? toneFor(error) : "error");
        }
    });

    capture?.addEventListener("click", async () => {
        if (!prompt || !request || !video.videoWidth) return;
        const current = request;
        capture.disabled = true;
        setState("checking", "Checking the frame with Face++…", "info");
        try {
            const canvas = document.createElement("canvas");
            canvas.width = Math.min(640, video.videoWidth);
            canvas.height = Math.round(canvas.width * video.videoHeight / video.videoWidth);
            canvas.getContext("2d").drawImage(video, 0, 0, canvas.width, canvas.height);
            const frame = canvas.toDataURL("image/jpeg", .8);
            const result = await send(root.dataset.frame, { token: prompt.token, frame, returnUrl: root.dataset.returnUrl }, current.signal);
            if (request !== current) return;
            if (result.redirectUrl) {
                release();
                setState("success", "Face verified. Signing you in…", "success");
                window.location.assign(result.redirectUrl);
            } else if (result.complete) {
                release();
                setState("success", "Face matched.", "success");
            } else next(result);
        } catch (error) {
            if (current.signal.aborted) return;
            fail(error.message, toneFor(error));
        }
    });

    save?.addEventListener("click", async () => {
        if (!prompt?.readyToSave) return;
        const current = request = new AbortController();
        setState("saving", "Saving your encrypted face reference…", "info");
        try {
            const result = await send(root.dataset.confirm, { token: prompt.token }, current.signal);
            prompt = null;
            setState("success", result.message || "Face recognition is set up.", "success");
            const badge = document.querySelector("[data-face-badge]");
            if (badge) { badge.className = "status-pill status-positive"; badge.innerHTML = "<span></span>Enrolled"; }
            setTimeout(() => window.location.reload(), 1600);
        } catch (error) {
            if (!current.signal.aborted) fail(error.message, toneFor(error));
        }
    });

    retry?.addEventListener("click", () => { reset("Start again when you are ready.", "info"); start?.focus(); });
    cancel?.addEventListener("click", () => { reset("Cancelled. The camera is off.", "info"); start?.focus(); });
    setState("idle");
}

function initialize(root = document) { root.querySelectorAll?.("[data-face-lab]").forEach(bind); }
document.addEventListener("workspace:load", event => initialize(event.detail instanceof Element ? event.detail : document));
initialize();
})();

