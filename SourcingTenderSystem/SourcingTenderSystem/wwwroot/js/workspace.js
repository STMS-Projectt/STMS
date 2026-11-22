(() => {
    const frame = document.querySelector("[data-workspace-frame]");
    const dialog = document.querySelector("#workspace-dialog");
    if (!frame || !window.fetch || !dialog?.showModal) return;
    const content = dialog.querySelector("[data-dialog-content]");
    const loader = dialog.querySelector("[data-dialog-loader]");
    const loaderLabel = loader.querySelector("[data-dialog-loader-label]");
    let loadingDialog = false;
    const setDialogLoading = (active, label = window.stmsT("Loading…")) => {
        loaderLabel.textContent = window.stmsT(label);
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
    const admin = frame.dataset.workspaceFrame === "admin";
    const department = frame.dataset.workspaceFrame === "department";
    const sourcing = frame.dataset.workspaceFrame === "sourcing";
    const supplier = frame.dataset.workspaceFrame === "supplier";
    const committee = frame.dataset.workspaceFrame === "committee";
    const back = dialog.querySelector("[data-dialog-back]");
    const dialogTitle = dialog.querySelector("#workspace-dialog-title");
    const dialogContext = dialog.querySelector("[data-dialog-context]");
    const previousDialogs = [];
    const dialogPositions = new WeakMap();
    let refreshOnClose = false;
    const showPrevious = () => {
        if (sending || loadingDialog || !previousDialogs.length) return;
        const previous = previousDialogs.pop();
        navigate(previous, "dialog", true, dialogPositions.get(previous));
    };
    back.addEventListener("click", showPrevious);

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
    async function install(result, mode, updateHistory = true, restore) {
        if (mode === "dialog") {
            loadingDialog = false;
            setDialogLoading(false);
            dialogUrl = new URL(result.res.url);
            const heading = result.main.querySelector("h1");
            const details = !!result.main.querySelector(".admin-record-hero, .pr-hero");
            dialog.classList.toggle("is-detail", details);
            dialogTitle.textContent = heading?.textContent || window.stmsT("Record");
            if (dialogContext) dialogContext.textContent = previousDialogs.length ? window.stmsT("Related action") : dialogContext.dataset.rootContext;
            back.hidden = previousDialogs.length === 0;
            dialog.scrollTop = 0;
            content.replaceChildren(...result.main.childNodes);
            if (!dialog.open) dialog.showModal();
            if (!matchMedia("(prefers-reduced-motion: reduce)").matches)
                content.animate?.([{ opacity: .45 }, { opacity: 1 }], { duration: 160, easing: "ease-out" });
            await scripts(result.page, content);
            const disclosures = content.querySelectorAll("details");
            restore?.open.forEach(index => { if (disclosures[index]) disclosures[index].open = true; });
            if (restore) dialog.scrollTop = restore.scroll;
            const restoredLink = restore && [...content.querySelectorAll("a[href]")].find(link => link.href === restore.href);
            const focusTarget = restoredLink || (details ? dialogTitle : content.querySelector("input:not([type=hidden]), select, textarea") || dialogTitle);
            focusTarget.focus({ preventScroll: true });
            return;
        }
        refreshOnClose = false;
        if (dialog.open) dialog.close();
        const oldMain = document.querySelector("#main");
        oldMain.replaceWith(result.main);
        for (const selector of ["#workspace-nav .side-nav", "#workspace-nav .side-bottom", ".bar-profile"]) {
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
    async function navigate(url, mode = "page", updateHistory = true, restore) {
        controller?.abort();
        const request = new AbortController();
        controller = request;
        progress.hidden = false;
        status.textContent = window.stmsT("Loading…");
        function showDialogError(message) {
            loadingDialog = false;
            setDialogLoading(false);
            dialogTitle.textContent = window.stmsT("Could not open this record");
            back.hidden = previousDialogs.length === 0;
            const alert = document.createElement("p");
            alert.className = "alert alert-warning mb-0";
            alert.setAttribute("role", "alert");
            alert.textContent = message;
            content.replaceChildren(alert);
            dialogTitle.focus({ preventScroll: true });
        }
        if (mode === "dialog") {
            loadingDialog = true;
            dialogTitle.textContent = window.stmsT("Loading…");
            content.replaceChildren();
            setDialogLoading(true);
            if (!dialog.open) dialog.showModal();
        }
        try {
            const result = await load(url, request.signal);
            if (request.signal.aborted) return;
            if (!sameWorkspace(result)) {
                if (mode === "dialog" && !/\/Account\//i.test(new URL(result.res.url || url).pathname))
                    showDialogError(window.stmsT("This record is unavailable or you no longer have permission to open it."));
                else location.assign(result.res.url || url);
                return;
            }
            await install(result, mode, updateHistory, restore);
            status.textContent = window.stmsT("Content loaded");
        } catch (error) {
            if (error.name !== "AbortError" && !request.signal.aborted) {
                if (mode === "dialog") showDialogError(window.stmsT("The record could not be loaded. Go back or close this window and try again."));
                else location.assign(url);
            }
        } finally {
            if (controller === request) progress.hidden = true;
        }
    }
    dialog.querySelector("[data-dialog-close]").addEventListener("click", () => { if (!sending) dialog.close(); });
    dialog.addEventListener("cancel", event => { if (sending) event.preventDefault(); });
    dialog.addEventListener("close", () => {
        if (dialog.open) return;
        if (loadingDialog) { controller?.abort(); progress.hidden = true; loadingDialog = false; }
        setDialogLoading(false);
        content.replaceChildren();
        dialogUrl = null;
        previousDialogs.length = 0;
        back.hidden = true;
        dialog.classList.remove("is-detail");
        if (dialogTrigger?.isConnected) dialogTrigger.focus();
        dialogTrigger = null;
        if (refreshOnClose) {
            refreshOnClose = false;
            navigate(location.href, "page", false);
        }
    });
    document.addEventListener("click", event => {
        const link = event.target.closest("a[href]");
        if (!link || event.defaultPrevented || event.button || event.ctrlKey || event.metaKey ||
            event.shiftKey || event.altKey || link.target || link.hasAttribute("download") ||
            link.hasAttribute("data-full-page") || !link.closest("#main, #workspace-nav, #workspace-dialog, .stms-topbar")) return;
        const insideDialog = dialog.contains(link);
        if (insideDialog && (link.hasAttribute("data-dialog-cancel") || link.classList.contains("back-link") || /^(cancel|back(?: to .+)?)$/i.test(link.textContent.trim()))) {
            event.preventDefault();
            if (!sending) previousDialogs.length ? showPrevious() : dialog.close();
            return;
        }
        const url = new URL(link.href);
        if (sending) { event.preventDefault(); return; }
        if (!supported(url) || url.hash) return;
        event.preventDefault();
        const sideNavigation = link.closest("#workspace-nav");
        const mode = !sideNavigation && !link.hasAttribute("data-workspace-page") &&
            (link.hasAttribute("data-workspace-dialog") || insideDialog) ? "dialog" : "page";
        if (mode === "dialog") {
            if (!dialog.open) { previousDialogs.length = 0; dialogUrl = null; }
            if (dialog.open && dialogUrl && dialogUrl.href !== url.href) {
                dialogPositions.set(dialogUrl, {
                    scroll: dialog.scrollTop, href: link.href,
                    open: [...content.querySelectorAll("details")].flatMap((item, index) => item.open ? [index] : [])
                });
                previousDialogs.push(dialogUrl);
            }
            else if (!dialog.open) dialogTrigger = link;
        }
        if (document.body.classList.contains("nav-open")) document.querySelector("[data-nav-close]")?.click();
        navigate(url, mode);
    });
    const confirmDialog = document.querySelector("#workspace-confirm");
    const confirmed = new WeakSet();
    let pendingConfirmation;
    window.stmsWorkspaceReady = true;
    confirmDialog.addEventListener("cancel", () => { pendingConfirmation = null; });
    confirmDialog.querySelector("form").addEventListener("submit", event => {
        event.preventDefault();
        const pending = pendingConfirmation;
        const decision = event.submitter?.value;
        pendingConfirmation = null;
        confirmDialog.close(decision || "cancel");
        if (!pending || decision !== "confirm" || !pending.form.isConnected) return;
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
            navigate(url, dialog.contains(form) ? "dialog" : "page");
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
            if (!sameWorkspace(result)) { fail(window.stmsT("The site did not return an editable page. Refresh to check the result before trying again.")); return; }
            const redirectedToForm = result.res.redirected && /\/(Tenders\/Create|Tenders\/AssignCommittee|Tenders\/Criteria)(\/|$)/i.test(new URL(result.res.url).pathname);
            if ((redirectedToForm || form.hasAttribute("data-workspace-dialog")) && !dialog.open) {
                // Refresh the originating page after the completed workflow action before opening its next form.
                const nextForm = result;
                const origin = await load(location.href, request.signal);
                if (sameWorkspace(origin)) await install(origin, "page", false);
                await install(nextForm, "dialog");
            } else if (inDialog && result.res.redirected && (department || supplier || committee ||
                (sourcing && (redirectedToForm || previousDialogs.length || content.querySelector(".admin-record-hero, .pr-hero") ||
                    url.pathname.startsWith("/Clarifications/") && result.main.querySelector(".admin-record-hero") ||
                    url.pathname.startsWith("/Suppliers/Approve/"))) ||
                (admin && result.main.querySelector(".admin-record-hero")))) {
                refreshOnClose = true;
                const prior = previousDialogs.at(-1);
                const destination = new URL(result.res.url);
                const panelOrigin = [prior, dialogUrl].find(candidate => candidate?.pathname === destination.pathname &&
                    candidate.searchParams.get("panel"));
                if (panelOrigin && !destination.searchParams.has("panel")) {
                    const refreshed = await load(panelOrigin, request.signal);
                    if (sameWorkspace(refreshed)) {
                        // The redirect already consumed the one-time response messages.
                        refreshed.main.prepend(...result.main.querySelectorAll(":scope > .notice, :scope > .alert-success, :scope > .alert-danger, :scope > [data-workspace-feedback]"));
                        if (panelOrigin === prior) previousDialogs.pop();
                        await install(refreshed, "dialog");
                    } else await install(result, "dialog");
                } else {
                    if (prior?.href === result.res.url) previousDialogs.pop();
                    await install(result, "dialog");
                }
            } else {
                await install(result, inDialog && !result.res.redirected ? "dialog" : "page");
            }
        } catch {
            fail(window.stmsT("The connection was interrupted. Refresh to check whether your changes were saved."));
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
