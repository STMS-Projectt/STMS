// Supplier "Add price" / "Edit price" form (BidItems/Create + BidItems/Edit).
// Live line total, notes counter, and the sample photo picker: several photos, previews, remove before saving,
// and the BidItem.MaxImages limit (the server checks the limit again).
(() => {
    const t = text => window.stmsT ? window.stmsT(text) : text;

    function init(root) {
        root.querySelectorAll("[data-bi]:not([data-bi-ready])").forEach(form => {
            form.dataset.biReady = "1";
            const quantity = Number(form.dataset.biQuantity) || 0;
            const max = Number(form.dataset.biMax) || 6;

            // Line total = unit price × requested quantity
            const price = form.querySelector("[data-bi-price]");
            const total = form.querySelector("[data-bi-total]");
            const money = new Intl.NumberFormat(document.documentElement.lang || undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            const showTotal = () => {
                const value = parseFloat(price.value);
                total.textContent = money.format(value > 0 ? value * quantity : 0);
                total.closest(".bi-total")?.classList.toggle("has-value", value > 0);
            };
            price?.addEventListener("input", showTotal);
            if (price && total) showTotal();

            // Notes counter
            const notes = form.querySelector("[data-bi-notes]");
            const count = form.querySelector("[data-bi-count]");
            notes?.addEventListener("input", () => { count.textContent = notes.value.length; });

            // Photos
            const input = form.querySelector("[data-bi-files]");
            const gallery = form.querySelector("[data-bi-gallery]");
            const add = form.querySelector("[data-bi-add]");
            const counter = form.querySelector("[data-bi-photo-count]");
            const limit = form.querySelector("[data-bi-limit]");
            if (!input || !gallery || typeof DataTransfer === "undefined") return;
            const chosen = new DataTransfer();
            const kept = () => gallery.querySelectorAll("[data-bi-remove]:not(:checked)").length;

            const refresh = () => {
                gallery.querySelectorAll(".bi-tile.is-new").forEach(tile => { URL.revokeObjectURL(tile.querySelector("img").src); tile.remove(); });
                [...chosen.files].forEach((file, index) => {
                    const tile = document.createElement("div");
                    tile.className = "bi-tile is-new";
                    tile.title = file.name;
                    const img = document.createElement("img");
                    img.src = URL.createObjectURL(file);
                    img.alt = file.name;
                    const badge = document.createElement("span");
                    badge.className = "bi-new-badge";
                    badge.textContent = t("New");
                    const remove = document.createElement("button");
                    remove.type = "button";
                    remove.className = "bi-tile-x";
                    remove.setAttribute("aria-label", window.stmsFormat ? window.stmsFormat("Remove {0}", file.name) : "Remove " + file.name);
                    remove.innerHTML = '<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" aria-hidden="true"><path d="m6 6 12 12 M18 6 6 18"/></svg>';
                    remove.addEventListener("click", () => {
                        const rest = new DataTransfer();
                        [...chosen.files].forEach((f, i) => { if (i !== index) rest.items.add(f); });
                        chosen.items.clear();
                        [...rest.files].forEach(f => chosen.items.add(f));
                        input.files = chosen.files;
                        refresh();
                    });
                    tile.append(img, badge, remove);
                    gallery.insertBefore(tile, add);
                });
                const used = kept() + chosen.files.length;
                counter.textContent = used;
                add.hidden = used >= max;
            };

            input.addEventListener("change", () => {
                let dropped = false;
                for (const file of input.files) {
                    if (!/\.(jpe?g|png)$/i.test(file.name)) continue; // the server would refuse it anyway
                    if (kept() + chosen.files.length >= max) { dropped = true; break; }
                    chosen.items.add(file);
                }
                input.files = chosen.files;
                limit.hidden = !dropped;
                refresh();
            });
            gallery.addEventListener("change", event => {
                if (!event.target.matches("[data-bi-remove]")) return;
                // Un-ticking "remove" must not push the item over the limit
                if (!event.target.checked && kept() + chosen.files.length > max) { event.target.checked = true; limit.hidden = false; return; }
                limit.hidden = true;
                refresh();
            });
            ["dragenter", "dragover"].forEach(name => add.addEventListener(name, () => add.classList.add("is-over")));
            ["dragleave", "drop"].forEach(name => add.addEventListener(name, () => add.classList.remove("is-over")));
            refresh();
        });
    }

    document.addEventListener("workspace:load", event => init(event.detail || document));
    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", () => init(document));
    else init(document);
})();
