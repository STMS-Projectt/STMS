(() => {
    const root = document.querySelector("[data-bio]");
    if (!root) return;
    const status = root.querySelector("[data-bio-status]");
    const token = root.querySelector('input[name="__RequestVerificationToken"]').value;
    const video = root.querySelector("[data-preview]");
    const open = root.querySelector("[data-open-camera]");
    const capture = root.querySelector("[data-capture]");
    const stop = root.querySelector("[data-stop-camera]");
    const phone = root.querySelector("[data-phone-button]");
    const password = root.querySelector("#bio-password");
    let stream;
    let busy = false;

    function closeCamera() {
        stream?.getTracks().forEach(track => track.stop());
        stream = null;
        if (video) {
            video.srcObject = null;
            video.hidden = true;
            capture.disabled = true;
            stop.hidden = true;
        }
    }
    window.addEventListener("pagehide", closeCamera);
    stop?.addEventListener("click", closeCamera);
    open?.addEventListener("click", async () => {
        if (busy || stream) return;
        try {
            open.disabled = true;
            if (!navigator.mediaDevices?.getUserMedia) throw new Error();
            stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "user", width: { ideal: 720 } }, audio: false });
            video.srcObject = stream;
            video.hidden = false;
            stop.hidden = false;
            await video.play();
            capture.disabled = false;
            status.textContent = "Camera ready. Look directly at the camera before continuing.";
        } catch {
            closeCamera();
            status.textContent = "Camera unavailable. Allow camera access and use HTTPS or localhost.";
        } finally { open.disabled = false; }
    });

    async function post(url, data) {
        const response = await fetch(url, { method: "POST", credentials: "same-origin",
            headers: { "Content-Type": "application/json", "RequestVerificationToken": token },
            body: JSON.stringify({ manage: root.dataset.manage === "true", password: password?.value, email: root.querySelector("[data-bio-email]")?.value, returnUrl: root.dataset.returnUrl, data }) });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result) throw new Error(result?.message || (response.status === 429 ? "Too many attempts. Wait a moment and try again." : "Could not complete verification. Try signing in again."));
        return result;
    }
    async function run(operation) {
        if (busy) return;
        if (password && !password.value) {
            status.textContent = "Enter your current password first.";
            password.focus();
            return;
        }
        busy = true;
        if (capture) capture.disabled = true;
        if (phone) phone.disabled = true;
        status.textContent = "Verifying…";
        try { window.location.assign((await operation()).redirectUrl); }
        catch (error) {
            status.textContent = error.name === "NotAllowedError" ? "Phone verification was cancelled or timed out. Try again." : error.message;
        } finally {
            if (password) password.value = "";
            busy = false;
            if (capture) capture.disabled = !stream;
            if (phone) phone.disabled = false;
        }
    }
    capture?.addEventListener("click", () => {
        if (!video.videoWidth) return;
        const email = root.querySelector("[data-bio-email]");
        if (email && root.dataset.knownFace !== "true" && !email.value.trim()) {
            status.textContent = "Enter your account email above to use face sign-in on this browser.";
            email.focus();
            return;
        }
        if (email?.value && !email.reportValidity()) return;
        run(async () => {
            const canvas = document.createElement("canvas");
            canvas.width = Math.min(video.videoWidth, 720);
            canvas.height = Math.round(video.videoHeight * canvas.width / video.videoWidth);
            canvas.getContext("2d").drawImage(video, 0, 0, canvas.width, canvas.height);
            const jpeg = canvas.toDataURL("image/jpeg", 0.8);
            closeCamera();
            return post(root.dataset.camera, jpeg);
        });
    });
    phone?.addEventListener("click", () => run(async () => {
        closeCamera();
        if (!window.isSecureContext || !window.PublicKeyCredential?.parseCreationOptionsFromJSON ||
            !PublicKeyCredential.parseRequestOptionsFromJSON || !PublicKeyCredential.prototype.toJSON)
            throw new Error("Use HTTPS and a current browser on a phone with screen unlock configured.");
        const options = await post(root.dataset.phoneOptions);
        const credential = phone.dataset.create === "true"
            ? await navigator.credentials.create({ publicKey: PublicKeyCredential.parseCreationOptionsFromJSON(options) })
            : await navigator.credentials.get({ publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(options) });
        return post(root.dataset.phone, JSON.stringify(credential));
    }));
})();
