(() => {
    const frame = document.querySelector("[data-workspace-frame]");
    const dialog = document.querySelector("#workspace-dialog");
    if (!frame || !window.fetch || !dialog?.showModal) return;
    const content = dialog.querySelector("[data-dialog-content]");
    const loader = dialog.querySelector("[data-dialog-loader]");
    const loaderLabel = loader.querySelector("[data-dialog-loader-label]");
    let loadingDialog = false;
    const setDialogLoading = (active, label = "Loading…") => {
        loaderLabel.textContent = label;
        loader.hidden = !active;
        content.setAttribute("aria-busy", String(active));
        dialog.classList.toggle("is-saving", active && label === "Saving…");
    };
    const progress = document.querySelector(".workspace-progress");
    const status = document.querySelector("[data-workspace-status]");
    const shared = document.querySelector("[data-page-scripts]");
    const libraries = new Map([...document.scripts].filter(s => s.src).map(s => [s.src, Promise.resolve()]));
    let controller;
    let sending = false;
    let dialogUrl;
    let dialogTrigger;

    function supported(url) {
        return url.origin === location.origin && !/\/(Account|Home|Error)(\/|$)/i.test(url.pathname) &&
            !/\/(Download\w*|Print\w*|DecisionPdf|SignDocument|Photo|AiReport)(\/|$)/i.test(url.pathname);
    }
    async function load(url, signal, options = {}) {
        const res = await fetch(url, { ...options, signal, headers: { "X-STMS-Fragment": "workspace" } });
        if (!res.ok || !res.headers.get("content-type")?.includes("text/html")) return { res };
        const page = new DOMParser().parseFromString(await res.text(), "text/html");
        return { res, page, nextFrame: page.querySelector("[data-workspace-frame]"), main: page.querySelector("#main") };
    }
    async function scripts(page, root) {
        const all = [...page.querySelectorAll("[data-page-scripts] script")];
        for (const source of all) {
            const path = source.getAttribute("src");
            if (path) {
                const sourceUrl = new URL(path, location.href).href;
                if (new URL(sourceUrl).origin !== location.origin) continue;
                if (!libraries.has(sourceUrl)) {
                    const el = document.createElement("script");
                    el.src = sourceUrl;
                    libraries.set(sourceUrl, new Promise((resolve, reject) => {
                        el.onload = resolve;
                        el.onerror = () => { libraries.delete(sourceUrl); reject(new Error("Script did not load")); };
                        document.head.append(el);
                    }));
                }
                await libraries.get(sourceUrl);
            } else {
                const el = document.createElement("script");
                el.textContent = source.textContent;
                shared.append(el);
                el.remove();
            }
        }
        window.jQuery?.validator?.unobtrusive?.parse(root);
        document.dispatchEvent(new CustomEvent("workspace:load", { detail: root }));
    }
    function sameWorkspace(result) {
        return result.main && result.nextFrame?.dataset.workspaceFrame === frame.dataset.workspaceFrame;
    }
    async function install(result, mode, updateHistory = true) {
        if (mode === "dialog") {
            loadingDialog = false;
            setDialogLoading(false);
            dialogUrl = new URL(result.res.url);
            dialog.querySelector("#workspace-dialog-title").textContent = result.main.querySelector("h1")?.textContent || "Edit record";
            content.replaceChildren(...result.main.childNodes);
            if (!dialog.open) dialog.showModal();
            await scripts(result.page, content);
            content.querySelector("input:not([type=hidden]), select, textarea")?.focus();
            return;
        }
        if (dialog.open) dialog.close();
        const oldMain = document.querySelector("#main");
        oldMain.replaceWith(result.main);
        for (const selector of ["#workspace-nav .side-nav", "#workspace-nav .side-bottom", ".bar-context strong", ".bar-profile"]) {
            const current = document.querySelector(selector);
            const next = result.page.querySelector(selector);
            if (current && next) current.replaceWith(next);
        }
        if (updateHistory && result.res.url !== location.href) history.pushState({ workspace: true }, "", result.res.url);
        document.title = result.nextFrame.dataset.pageTitle;
        await scripts(result.page, result.main);
        window.scrollTo(0, 0);
        result.main.focus({ preventScroll: true });
    }
    async function navigate(url, mode = "page", updateHistory = true) {
        controller?.abort();
        const request = new AbortController();
        controller = request;
        progress.hidden = false;
        status.textContent = "Loading…";
        if (mode === "dialog") {
            loadingDialog = true;
            dialog.querySelector("#workspace-dialog-title").textContent = "One moment";
            content.replaceChildren();
            setDialogLoading(true);
            if (!dialog.open) dialog.showModal();
        }
        try {
            const result = await load(url, request.signal);
            if (request.signal.aborted) return;
            if (!sameWorkspace(result)) { location.assign(result.res.url || url); return; }
            await install(result, mode, updateHistory);
            status.textContent = "Content loaded";
        } catch (error) {
            if (error.name !== "AbortError" && !request.signal.aborted) location.assign(url);
        } finally {
            if (controller === request) progress.hidden = true;
        }
    }
    dialog.querySelector("[data-dialog-close]").addEventListener("click", () => { if (!sending) dialog.close(); });
    dialog.addEventListener("cancel", event => { if (sending) event.preventDefault(); });
    dialog.addEventListener("close", () => {
        content.replaceChildren();
        dialogUrl = null;
        if (dialogTrigger?.isConnected) dialogTrigger.focus();
        dialogTrigger = null;
    });
    document.addEventListener("click", event => {
        const link = event.target.closest("a[href]");
        if (!link || event.defaultPrevented || event.button || event.ctrlKey || event.metaKey ||
            event.shiftKey || event.altKey || link.target || link.hasAttribute("download") ||
            link.hasAttribute("data-full-page") || !link.closest("#main, #workspace-nav, #workspace-dialog")) return;
        if (link.closest("#workspace-dialog") && (link.hasAttribute("data-dialog-cancel") || /^(cancel|back(?: to .+)?)$/i.test(link.textContent.trim()))) {
            event.preventDefault(); if (!sending) dialog.close(); return;
        }
        const url = new URL(link.href);
        if (sending) { event.preventDefault(); return; }
        if (!supported(url) || url.hash) return;
        event.preventDefault();
        const mode = link.hasAttribute("data-workspace-dialog") ? "dialog" : "page";
        if (mode === "dialog") dialogTrigger = link;
        if (document.body.classList.contains("nav-open")) document.querySelector("[data-nav-close]")?.click();
        navigate(url, mode);
    });
    const confirmDialog = document.querySelector("#workspace-confirm");
    const confirmed = new WeakSet();
    let pendingConfirmation;
    window.stmsWorkspaceReady = true;
    confirmDialog.addEventListener("close", () => {
        const pending = pendingConfirmation;
        pendingConfirmation = null;
        if (!pending || confirmDialog.returnValue !== "confirm" || !pending.form.isConnected) return;
        confirmed.add(pending.form);
        try { pending.form.requestSubmit(pending.submit || undefined); }
        finally { confirmed.delete(pending.form); }
    });
    document.addEventListener("submit", async event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || event.defaultPrevented || !form.closest("#main, #workspace-dialog") ||
            form.hasAttribute("data-full-page") || form.target) return;
        const submit = event.submitter;
        const url = new URL(submit?.getAttribute("formaction") ?? form.getAttribute("action") ?? dialogUrl ?? location.href,
            dialogUrl ?? location.href);
        const method = (submit?.getAttribute("formmethod") ?? form.method).toLowerCase();
        if (!supported(url) || !["get", "post"].includes(method)) return;
        if (form.dataset.workspaceConfirm && !confirmed.has(form)) {
            event.preventDefault();
            if (sending || confirmDialog.open) return;
            pendingConfirmation = { form, submit };
            confirmDialog.returnValue = "";
            confirmDialog.querySelector("[data-confirm-question]").textContent = form.dataset.workspaceConfirm;
            confirmDialog.showModal();
            return;
        }
        const data = new FormData(form, submit);
        event.preventDefault();
        if (sending) return;
        if (method === "get") {
            url.search = new URLSearchParams(data);
            navigate(url);
            return;
        }
        const inDialog = dialog.contains(form);
        form.setAttribute("aria-busy", "true");
        controller?.abort();
        const request = new AbortController();
        controller = request;
        sending = true;
        if (inDialog) setDialogLoading(true, "Saving…");
        progress.hidden = false;
        if (submit) submit.disabled = true;
        const fail = message => {
            const alert = document.createElement("p");
            alert.className = "alert alert-warning mt-3";
            alert.setAttribute("role", "alert");
            alert.textContent = message;
            form.querySelector("[data-submit-error]")?.remove();
            alert.dataset.submitError = "";
            form.prepend(alert);
        };
        try {
            const enctype = submit?.getAttribute("formenctype") ?? form.enctype;
            const body = enctype === "multipart/form-data" ? data : new URLSearchParams(data);
            const result = await load(url, request.signal, { method: "POST", body });
            if (result.res.redirected && !sameWorkspace(result)) {
                location.assign(result.res.url);
                return;
            }
            if (!sameWorkspace(result)) { fail("The site did not return an editable page. Refresh to check the result before trying again."); return; }
            const redirectedToForm = result.res.redirected && /\/(Tenders\/Create|Tenders\/AssignCommittee)(\/|$)/i.test(new URL(result.res.url).pathname);
            if (redirectedToForm && !dialog.open) {
                // Refresh the originating page after the completed workflow action before opening its next form.
                const nextForm = result;
                const origin = await load(location.href, request.signal);
                if (sameWorkspace(origin)) await install(origin, "page", false);
                await install(nextForm, "dialog");
            } else {
                await install(result, inDialog && !result.res.redirected ? "dialog" : "page");
            }
        } catch {
            fail("The connection was interrupted. Refresh to check whether your changes were saved.");
        } finally {
            sending = false;
            form.removeAttribute("aria-busy");
            if (inDialog) setDialogLoading(false);
            if (submit) submit.disabled = false;
            progress.hidden = true;
        }
    });
    window.addEventListener("popstate", () => {
        if (sending) { location.reload(); return; }
        navigate(location.href, "page", false);
    });
})();
