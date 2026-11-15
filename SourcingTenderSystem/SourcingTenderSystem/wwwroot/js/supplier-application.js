(() => {
    function initialize(root) {
        for (const fieldset of root.querySelectorAll("[data-category-selection]")) {
            const output = fieldset.querySelector("[data-category-count]");
            const sync = () => { output.textContent = `${fieldset.querySelectorAll("input:checked").length} selected`; };
            fieldset.addEventListener("change", sync);
            output.hidden = false;
            sync();
        }
        for (const input of root.querySelectorAll("[data-draft-document-file]")) {
            input.addEventListener("change", () => {
                if (!input.files?.length) return;
                input.classList.remove("input-validation-error");
                input.removeAttribute("aria-invalid");
                const message = input.form?.querySelector('[data-valmsg-for="File"]');
                if (!message) return;
                message.textContent = "";
                message.classList.remove("field-validation-error");
                message.classList.add("field-validation-valid");
            });
        }
    }
    initialize(document);
    document.addEventListener("workspace:load", event => initialize(event.detail));
})();
