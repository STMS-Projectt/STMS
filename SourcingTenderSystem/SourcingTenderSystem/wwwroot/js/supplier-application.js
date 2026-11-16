(() => {
    function initialize(root) {
        for (const fieldset of root.querySelectorAll("[data-category-selection]")) {
            const output = fieldset.querySelector("[data-category-count]");
            const search = fieldset.querySelector("[data-category-search]");
            const options = [...fieldset.querySelectorAll(".supplier-category-option")];
            const query = search?.querySelector("input");
            const empty = fieldset.querySelector("[data-category-empty]");
            const sync = () => {
                output.textContent = window.stmsFormat("{0} selected", fieldset.querySelectorAll('input[name="CategoryIds"]:checked').length);
                const term = query?.value.trim().toLocaleLowerCase() || "";
                let matches = 0;
                for (const option of options) {
                    const selected = option.querySelector("input").checked;
                    const match = option.textContent.trim().toLocaleLowerCase().includes(term);
                    option.hidden = !selected && !match;
                    if (match) matches++;
                }
                if (empty) empty.hidden = matches > 0;
            };
            fieldset.addEventListener("change", sync);
            if (search && options.length > 1) {
                search.hidden = false;
                query.addEventListener("input", sync);
            }
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
