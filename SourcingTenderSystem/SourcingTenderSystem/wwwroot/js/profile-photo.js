(() => {
    function initialize(root) {
        const panel = root.querySelector?.("[data-photo-upload]");
        if (!panel || panel.dataset.ready) return;
        panel.dataset.ready = "true";
        const input = panel.querySelector('input[type="file"]');
        const preview = panel.querySelector("[data-photo-preview]");
        const placeholder = panel.querySelector("[data-photo-placeholder]");
        const status = panel.querySelector("[data-photo-status]");
        const cancel = panel.querySelector("[data-photo-cancel]");
        const save = panel.querySelector("[data-photo-save]");
        const original = preview.getAttribute("src");
        let objectUrl;
        save.disabled = true;
        function reset() {
            if (objectUrl) URL.revokeObjectURL(objectUrl);
            objectUrl = null;
            input.value = "";
            input.setCustomValidity("");
            if (original) preview.src = original;
            else preview.removeAttribute("src");
            preview.hidden = !original;
            if (placeholder) placeholder.hidden = !!original;
            status.textContent = "No photo selected.";
            status.classList.remove("text-danger");
            cancel.hidden = true;
            save.disabled = true;
        }
        input.addEventListener("change", () => {
            const file = input.files?.[0];
            if (!file) { reset(); return; }
            if (!(["image/png", "image/jpeg"].includes(file.type)) || file.size === 0 || file.size > 1024 * 1024) {
                reset();
                status.textContent = "Choose a JPEG or PNG image up to 1 MB.";
                status.classList.add("text-danger");
                return;
            }
            if (objectUrl) URL.revokeObjectURL(objectUrl);
            objectUrl = URL.createObjectURL(file);
            preview.src = objectUrl;
            preview.hidden = false;
            if (placeholder) placeholder.hidden = true;
            status.textContent = file.name;
            status.classList.remove("text-danger");
            cancel.hidden = false;
            save.disabled = false;
        });
        cancel.addEventListener("click", reset);
        window.addEventListener("pagehide", () => { if (objectUrl) URL.revokeObjectURL(objectUrl); }, { once: true });
    }
    initialize(document);
    document.addEventListener("workspace:load", event => initialize(event.detail));
})();
