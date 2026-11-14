(() => {
function bind(root) {
    const start = root?.querySelector("[data-face-start]");
    if (!start || start.dataset.ready) return;
    start.dataset.ready = "true";
    const capture = root.querySelector("[data-face-capture]");
    const stop = root.querySelector("[data-face-stop]");
    const guide = root.querySelector("[data-face-guide]");
    const video = root.querySelector("[data-face-video]");
    const status = root.querySelector("[data-face-status]");
    const password = root.querySelector("#face-lab-password");
    const antiForgery = root.closest("[data-bio]").querySelector('input[name="__RequestVerificationToken"]').value;
    let stream, request, prompt;

    function clean() {
        request?.abort();
        request = null;
        prompt = null;
        stream?.getTracks().forEach(track => track.stop());
        stream = null;
        video.srcObject = null;
        capture.hidden = guide.hidden = stop.hidden = true;
        if (password) password.value = "";
        start.disabled = false;
    }
    const lifecycle = new AbortController();
    const observer = new MutationObserver(() => {
        if (!root.isConnected) { clean(); lifecycle.abort(); observer.disconnect(); }
    });
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener("pagehide", clean, { signal: lifecycle.signal });
    const fail = error => {
        clean();
        status.textContent = error.name === "NotAllowedError" ? "Camera access was denied. Allow it in your browser settings and retry." :
            error.name === "NotFoundError" ? "No camera was found. Connect a camera and retry." :
            error.name === "NotReadableError" ? "The camera is busy or unavailable. Close other camera apps and retry." : error.message;
    };
    stop.addEventListener("click", () => { clean(); status.textContent = "Camera stopped. You can start again."; });

    async function send(url, data, signal) {
        const response = await fetch(url, {
            method: "POST", credentials: "same-origin", signal,
            headers: { "Content-Type": "application/json", "RequestVerificationToken": antiForgery },
            body: JSON.stringify(data)
        });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result) throw new Error(result?.message ||
            (response.status === 429 ? "Too many requests. Wait before retrying." : "Verification unavailable. Use a password or passkey."));
        return result;
    }
    function next(value) {
        prompt = value;
        capture.hidden = false;
        capture.disabled = false;
        status.textContent = "Step " + value.step + " of 3: " + value.instruction;
    }
    start.addEventListener("click", async () => {
        if (password && !password.value) {
            password.focus();
            status.textContent = "Enter your current password to enroll.";
            return;
        }
        start.disabled = true;
        stop.hidden = false;
        const current = request = new AbortController();
        try {
            if (!navigator.mediaDevices?.getUserMedia) throw new Error("Camera access requires HTTPS or localhost.");
            status.textContent = "Waiting for camera permission…";
            const opened = await navigator.mediaDevices.getUserMedia({ audio: false, video: { facingMode: "user" } });
            if (current.signal.aborted) { opened.getTracks().forEach(track => track.stop()); return; }
            stream = opened;
            video.srcObject = stream;
            guide.hidden = false;
            await video.play();
            const result = await send(root.dataset.start, { enroll: start.dataset.enroll === "true", password: password?.value }, current.signal);
            if (request !== current) return;
            if (password) password.value = "";
            next(result);
        } catch (error) { if (!current.signal.aborted) fail(error); }
    });
    capture.addEventListener("click", async () => {
        if (!prompt || !video.videoWidth || !request) return;
        const current = request;
        capture.disabled = true;
        status.textContent = "Checking the frame with Face++…";
        try {
            const canvas = document.createElement("canvas");
            canvas.width = Math.min(640, video.videoWidth);
            canvas.height = Math.round(canvas.width * video.videoHeight / video.videoWidth);
            canvas.getContext("2d").drawImage(video, 0, 0, canvas.width, canvas.height);
            const result = await send(root.dataset.frame, { token: prompt.token, frame: canvas.toDataURL("image/jpeg", .78) }, current.signal);
            if (request !== current) return;
            if (result.complete) {
                clean();
                if (start.dataset.enroll === "true") {
                    status.textContent = "Reference saved. Reopen Sign-in security to try a comparison or remove it.";
                    start.hidden = true;
                    if (password) password.hidden = true;
                } else status.textContent = "Face matched the reference. Your sign-in session was unchanged.";
            } else next(result);
        } catch (error) { if (!current.signal.aborted) fail(error); }
    });
}
function initialize() { document.querySelectorAll("[data-face-lab]").forEach(bind); }
document.addEventListener("workspace:load", initialize);
initialize();
})();
