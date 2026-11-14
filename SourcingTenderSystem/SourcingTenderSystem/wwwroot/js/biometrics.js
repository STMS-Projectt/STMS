(() => {
function bind(root) {
    if (!root || root.dataset.bioReady) return;
    root.dataset.bioReady = "true";
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
    tabs(root);
}

// Sign-in method tabs (WAI-ARIA tabs pattern with arrow-key navigation).
function tabs(root) {
    const list = root.querySelector("[data-auth-tabs]");
    if (!list) return;
    const all = [...list.querySelectorAll('[role="tab"]')];
    const select = (tab, focus) => {
        all.forEach(item => {
            const on = item === tab;
            item.setAttribute("aria-selected", on ? "true" : "false");
            item.tabIndex = on ? 0 : -1;
            document.getElementById(item.getAttribute("aria-controls")).hidden = !on;
        });
        if (focus) tab.focus();
        const email = root.querySelector("[data-bio-email]"), faceEmail = root.querySelector("[data-face-email]");
        if (tab.id === "tab-face" && faceEmail && !faceEmail.value && email?.value) faceEmail.value = email.value;
    };
    all.forEach((tab, index) => {
        tab.addEventListener("click", () => select(tab, false));
        tab.addEventListener("keydown", event => {
            const move = { ArrowRight: 1, ArrowLeft: -1, Home: -index, End: all.length - 1 - index }[event.key];
            if (move === undefined) return;
            event.preventDefault();
            select(all[(index + move + all.length) % all.length], true);
        });
    });
    root.querySelectorAll("[data-auth-switch]").forEach(button =>
        button.addEventListener("click", () => select(document.getElementById(button.dataset.authSwitch), true)));
}

function initialize() { document.querySelectorAll("[data-bio]").forEach(bind); }
document.addEventListener("workspace:load", initialize);
initialize();
})();
