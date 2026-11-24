// AI Intelligence: simple bar charts + the AI Assistant (chatbot) page.
(() => {
    if (window.__aiIntelligenceReady) return;
    window.__aiIntelligenceReady = true;

    const t = text => (window.stmsT ? window.stmsT(text) : text);

    // ------------------------------------------------------------ Charts
    // Draws a chart inside "box" from { title, labels, values }.
    // kind "bars" (default): horizontal bars, one row per label. kind "columns": vertical columns, used for time.
    // bare: the card already shows the title, so none is drawn here.
    function drawChart(box, chart, kind = "bars", bare = false) {
        box.innerHTML = "";
        if (!bare) {
            const title = document.createElement("h3");
            title.className = "ai-chart-title";
            title.textContent = chart.title;
            box.append(title);
        }
        if (!chart.values || chart.values.length === 0) {
            box.append(Object.assign(document.createElement("p"), { className: "ai-empty", textContent: t("No data yet.") }));
            return;
        }
        const max = Math.max(...chart.values, 1);
        const number = v => Number(v).toLocaleString(undefined, { maximumFractionDigits: 2 });
        const list = document.createElement("div");
        list.className = kind === "columns" ? "ai-columns" : "ai-bars";
        list.setAttribute("role", "list");
        chart.labels.forEach((label, i) => {
            const value = chart.values[i];
            const row = document.createElement("div");
            row.className = kind === "columns" ? "ai-col" : "ai-bar-row";
            row.setAttribute("role", "listitem");
            row.tabIndex = 0;
            row.dataset.tip = `${label}: ${number(value)}`;
            row.setAttribute("aria-label", row.dataset.tip);
            row.innerHTML = kind === "columns"
                ? `<span class="ai-bar-value"></span><span class="ai-col-track"><span class="ai-bar"></span></span><span class="ai-bar-label"></span>`
                : `<span class="ai-bar-label"></span><span class="ai-bar-track"><span class="ai-bar"></span></span><span class="ai-bar-value"></span>`;
            row.querySelector(".ai-bar-label").textContent = label;
            row.querySelector(".ai-bar-value").textContent = number(value);
            const bar = row.querySelector(".ai-bar");
            const size = value > 0 ? Math.max(2, 100 * value / max) + "%" : "0";
            // Grow from zero once the box is drawn (skipped when the user prefers less motion).
            if (matchMedia("(prefers-reduced-motion: reduce)").matches) bar.style.setProperty("--size", size);
            else requestAnimationFrame(() => requestAnimationFrame(() => bar.style.setProperty("--size", size)));
            if (value === 0) row.classList.add("is-zero");
            list.append(row);
        });
        box.append(list);
    }

    function drawPageCharts(root) {
        root.querySelectorAll("[data-ai-chart]").forEach(box =>
            drawChart(box, JSON.parse(box.dataset.aiChart), box.dataset.aiChartKind || "bars", box.hasAttribute("data-ai-chart-bare")));
        // Choosing a tender in the list opens its report.
        root.querySelectorAll("[data-ai-autosubmit]:not([data-ready])").forEach(select => {
            select.dataset.ready = "true";
            select.addEventListener("change", () => select.form.requestSubmit());
        });
        // Tabs: Tenders / Suppliers tables
        root.querySelectorAll("[data-ai-tabs]:not([data-ready])").forEach(tabs => {
            tabs.dataset.ready = "true";
            const buttons = [...tabs.querySelectorAll("[data-ai-tab]")];
            const show = button => {
                buttons.forEach(b => b.setAttribute("aria-selected", String(b === button)));
                tabs.querySelectorAll("[data-ai-panel]").forEach(panel => { panel.hidden = panel.dataset.aiPanel !== button.dataset.aiTab; });
            };
            buttons.forEach((button, i) => {
                button.addEventListener("click", () => show(button));
                button.addEventListener("keydown", event => {
                    if (event.key !== "ArrowRight" && event.key !== "ArrowLeft") return;
                    const next = buttons[(i + (event.key === "ArrowRight" ? 1 : -1) + buttons.length) % buttons.length];
                    next.focus(); show(next);
                });
            });
        });
    }

    // ------------------------------------------------------------ Table helpers
    function buildTable(table) {
        const outer = document.createElement("div");
        outer.className = "ai-table-wrap";
        const wrap = document.createElement("div");
        wrap.className = "table-responsive";
        const el = document.createElement("table");
        el.className = "table table-sm ai-table";
        const head = el.createTHead().insertRow();
        table.columns.forEach(c => head.append(Object.assign(document.createElement("th"), { textContent: t(c) })));
        const body = el.createTBody();
        table.rows.forEach(r => {
            const tr = body.insertRow();
            r.forEach(cell => tr.insertCell().textContent = cell);
        });
        wrap.append(el);
        outer.append(wrap);
        return outer;
    }

    // Download links for one answer: the server builds the Excel / PDF report (same template as the Reports page).
    function downloadLinks(form, id) {
        const bar = document.createElement("div");
        bar.className = "ai-downloads";
        const base = form.action.replace(/\/Ask$/, "/DownloadAnswer/") + encodeURIComponent(id);
        for (const [format, label] of [["excel", "Download as Excel"], ["pdf", "Download as PDF"]]) {
            const link = Object.assign(document.createElement("a"),
                { className: "btn btn-sm btn-outline-secondary", href: `${base}?format=${format}`, textContent: t(label) });
            link.setAttribute("data-full-page", "");   // a file download, not a workspace page
            bar.append(link);
        }
        return bar;
    }

    // ------------------------------------------------------------ Admin: confirm what the AI suggests to create
    // The card is a normal form that posts to the Departments / Categories / Employees "Create" page,
    // so nothing is saved until the admin presses Confirm, and the usual validation still runs.
    function actionForm(action, chatForm) {
        const titles = { create_department: "Create department", create_category: "Create category", create_employee: "Create employee" };
        const form = Object.assign(document.createElement("form"), { method: "post", action: action.url, className: "aic-action" });
        form.innerHTML = `<div class="aic-action-head"><strong></strong><span></span></div><div class="aic-action-fields"></div>`;
        form.querySelector("strong").textContent = t(titles[action.action]);
        form.querySelector(".aic-action-head span").textContent = t("Review the details, then confirm. Nothing is saved until you confirm.");
        const token = chatForm.querySelector("input[name=__RequestVerificationToken]");
        if (token) form.append(token.cloneNode());

        if (action.warnings?.length) {
            const warning = document.createElement("ul");
            warning.className = "aic-action-warnings";
            action.warnings.forEach(w => warning.append(Object.assign(document.createElement("li"), { textContent: t(w) })));
            form.querySelector(".aic-action-head").after(warning);
        }

        const fields = form.querySelector(".aic-action-fields");
        const value = name => action.fields?.[name] ?? "";
        const add = (label, control, wide = false) => {
            const wrap = document.createElement("label");
            wrap.className = "field" + (wide ? " field-wide" : "");
            wrap.append(t(label), control);
            fields.append(wrap);
        };
        const input = (name, attrs = {}) => Object.assign(document.createElement(attrs.tag || "input"),
            { name, className: "form-control", value: value(name), ...attrs, tag: undefined });
        const select = (name, options, empty) => {
            const list = Object.assign(document.createElement("select"), { name, className: "form-select" });
            if (empty) list.append(new Option(t(empty), ""));
            options.forEach(o => list.append(new Option(t(o.label), o.value, false, o.value === value(name))));
            return list;
        };
        const active = () => {   // checkbox + hidden "false", the usual ASP.NET MVC pattern
            const wrap = document.createElement("label");
            wrap.className = "aic-action-check";
            wrap.append(Object.assign(document.createElement("input"), { type: "checkbox", name: "IsActive", value: "true", checked: true }), t("Active"));
            fields.append(wrap, Object.assign(document.createElement("input"), { type: "hidden", name: "IsActive", value: "false" }));
        };

        if (action.action === "create_department") {
            add("Name", input("Name", { required: true, maxLength: 200 }));
            add("Description", input("Description", { tag: "textarea", rows: 2, maxLength: 2000 }), true);
            active();
        } else if (action.action === "create_category") {
            add("Name", input("name", { required: true, maxLength: 120 }));
        } else {
            add("Full name", input("FullName", { required: true, maxLength: 200 }));
            add("Email", input("Email", { type: "email", required: true, maxLength: 256 }));
            add("Job title", input("JobTitle", { maxLength: 150 }));
            add("Department", select("DepartmentId", action.departments || [], "No department"));
            const role = select("Role", action.roles || [], "Select a role");
            role.required = true;
            add("Role", role);
            add("Password", input("Password", { type: "password", required: true, minLength: 6, maxLength: 128, autocomplete: "new-password", value: "" }));
            active();
        }

        const buttons = document.createElement("div");
        buttons.className = "aic-action-buttons";
        buttons.innerHTML = `<button type="submit" class="btn btn-primary"></button><button type="button" class="btn btn-outline-secondary"></button>`;
        buttons.children[0].textContent = t("Confirm and create");
        buttons.children[1].textContent = t("Cancel");
        buttons.children[1].addEventListener("click", () =>
            form.replaceWith(Object.assign(document.createElement("p"), { className: "aic-action-cancelled", textContent: t("Cancelled. Nothing was created.") })));
        form.append(buttons);
        return form;
    }

    // ------------------------------------------------------------ AI Assistant page (chatbot)
    function setupChat(chat) {
        const thread = chat.querySelector("[data-ai-messages]");
        const welcome = chat.querySelector("[data-ai-welcome]");
        const form = chat.querySelector("[data-ai-form]");
        const question = chat.querySelector("[data-ai-question]");
        const send = chat.querySelector("[data-ai-send]");
        const file = chat.querySelector("[data-ai-file]");
        const fileChip = chat.querySelector("[data-ai-file-chip]");
        const fileName = chat.querySelector("[data-ai-file-name]");
        let busy = false;

        // The last messages are sent with each question so the AI understands follow-ups ("and the highest?").
        let history = [];
        const remember = (role, content) => {
            if (!content) return;
            history.push({ role, content: content.slice(0, 1500) });
            if (history.length > 6) history.shift();
        };

        // The text box grows with the text (up to 8 lines).
        const resize = () => { question.style.height = "auto"; question.style.height = Math.min(question.scrollHeight, 200) + "px"; };
        question.addEventListener("input", resize);
        question.addEventListener("keydown", event => {
            if (event.key === "Enter" && !event.shiftKey && !event.isComposing) { event.preventDefault(); form.requestSubmit(); }
        });

        const showFile = () => {
            fileName.textContent = file.files[0]?.name ?? "";
            fileChip.hidden = !file.files.length;
        };
        file.addEventListener("change", showFile);

        chat.addEventListener("click", event => {
            const suggestion = event.target.closest("[data-ai-suggestion]");
            if (suggestion) { question.value = suggestion.dataset.question; form.requestSubmit(); }
            if (event.target.closest("[data-ai-file-clear]")) { file.value = ""; showFile(); question.focus(); }
            if (event.target.closest("[data-ai-new-chat]")) {
                thread.querySelectorAll(".aic-msg").forEach(m => m.remove());
                history = [];
                welcome.hidden = false;
                question.focus();
            }
        });

        // One chat message: an avatar (bot only) and a bubble. Returns the bubble.
        function addMessage(kind, text) {
            welcome.hidden = true;
            const message = document.createElement("article");
            message.className = "aic-msg is-" + kind;
            if (kind === "bot") {
                const avatar = document.createElement("span");
                avatar.className = "aic-avatar";
                avatar.innerHTML = chat.querySelector(".aic-logo").innerHTML;
                message.append(avatar);
            }
            const bubble = document.createElement("div");
            bubble.className = "aic-bubble";
            if (text) bubble.append(Object.assign(document.createElement("p"), { className: "aic-text", textContent: text }));
            message.append(bubble);
            thread.append(message);
            message.scrollIntoView({ behavior: matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth", block: "end" });
            return bubble;
        }

        form.addEventListener("submit", async event => {
            event.preventDefault();
            const text = question.value.trim();
            if (busy || (!text && !file.files.length)) return;

            const userBubble = addMessage("user", text);
            if (file.files.length) {
                const tag = Object.assign(document.createElement("span"), { className: "aic-file-tag", textContent: file.files[0].name });
                userBubble.append(tag);
            }
            const data = new FormData(form);
            data.append("history", JSON.stringify(history));
            remember("user", text || `[file] ${file.files[0]?.name ?? ""}`);
            question.value = ""; resize();
            file.value = ""; showFile();

            busy = true; send.disabled = true;
            const waiting = addMessage("bot");
            waiting.closest(".aic-msg").classList.add("is-typing");
            waiting.innerHTML = `<span class="aic-dots" aria-label="${t("Thinking…")}"><i></i><i></i><i></i></span>`;
            try {
                const response = await fetch(form.action, { method: "POST", body: data });
                const answer = await response.json();
                waiting.closest(".aic-msg").remove();
                showAnswer(answer);
                if (response.ok) remember("assistant", answer.answer);
            } catch {
                waiting.closest(".aic-msg").classList.remove("is-typing");
                waiting.textContent = t("Sorry, something went wrong. Please try again.");
            } finally {
                busy = false; send.disabled = false; question.focus();
            }
        });

        // Shows text, analysis cards, chart, table, recommendations and download links of one answer.
        function showAnswer(answer) {
            const box = addMessage("bot", answer.answer || "");
            if (answer.cards?.length) {
                const cards = document.createElement("div");
                cards.className = "aic-cards";
                answer.cards.forEach(c => {
                    const card = document.createElement("div");
                    card.className = "aic-card";
                    card.innerHTML = "<strong></strong><span></span>";
                    card.querySelector("strong").textContent = c.value;
                    card.querySelector("span").textContent = t(c.label);
                    cards.append(card);
                });
                box.append(cards);
            }
            if (answer.chart?.values?.length) {
                const chartBox = document.createElement("div");
                chartBox.className = "ai-chart aic-chart";
                drawChart(chartBox, { ...answer.chart, title: t(answer.chart.title) }, answer.chart.kind === "columns" ? "columns" : "bars");
                box.append(chartBox);
            }
            if (answer.table?.rows?.length) box.append(buildTable(answer.table));
            if (answer.recommendations?.length) {
                const recs = document.createElement("div");
                recs.className = "aic-recs";
                recs.append(Object.assign(document.createElement("strong"), { textContent: t("AI recommendations (advice only)") }));
                const list = document.createElement("ul");
                answer.recommendations.forEach(r => list.append(Object.assign(document.createElement("li"), { textContent: r })));
                recs.append(list);
                box.append(recs);
            }
            if (answer.action) box.append(actionForm(answer.action, form));
            if (answer.id) box.append(downloadLinks(form, answer.id));
            box.closest(".aic-msg").scrollIntoView({ behavior: "auto", block: "start" });
        }
    }

    function initChat(root) {
        root.querySelectorAll("[data-ai-chat]:not([data-ready])").forEach(chat => {
            chat.dataset.ready = "true";
            setupChat(chat);
        });
    }

    // Run on the first page load and every time the workspace loads a new page.
    drawPageCharts(document);
    initChat(document);
    document.addEventListener("workspace:load", event => { drawPageCharts(event.detail || document); initChat(event.detail || document); });
})();
