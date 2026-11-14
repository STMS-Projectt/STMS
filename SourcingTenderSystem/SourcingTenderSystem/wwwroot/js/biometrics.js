(() => {
    const root = document.querySelector("[data-bio]");
    if (!root) return;
    const status = root.querySelector("[data-bio-status]");
    const token = root.querySelector('input[name="__RequestVerificationToken"]').value;
    const phone = root.querySelector("[data-phone-button]");
    const password = root.querySelector("#bio-password");
    let busy = false;

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
        if (phone) phone.disabled = true;
        status.textContent = "Verifying…";
        try { window.location.assign((await operation()).redirectUrl); }
        catch (error) {
            status.textContent = error.name === "NotAllowedError" ? "Phone verification was cancelled or timed out. Try again." : error.message;
        } finally {
            if (password) password.value = "";
            busy = false;
            if (phone) phone.disabled = false;
        }
    }
    phone?.addEventListener("click", () => run(async () => {
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
