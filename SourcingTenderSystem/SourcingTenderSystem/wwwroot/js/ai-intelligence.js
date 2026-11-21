// AI Intelligence: simple bar charts + the AI chatbot.
(() => {
    if (window.__aiIntelligenceReady) return;
    window.__aiIntelligenceReady = true;

    const t = text => (window.stmsT ? window.stmsT(text) : text);

    // ------------------------------------------------------------ Charts
    // Draws a horizontal bar chart inside "box" from { title, labels, values }.
    function drawChart(box, chart) {
        box.innerHTML = "";
        const title = document.createElement("h3");
        title.className = "ai-chart-title";
        title.textContent = chart.title;
        box.append(title);
        if (!chart.values || chart.values.length === 0) {
            box.append(Object.assign(document.createElement("p"), { className: "ai-empty", textContent: t("No data yet.") }));
            return;
        }
        const max = Math.max(...chart.values, 1);
        chart.labels.forEach((label, i) => {
            const row = document.createElement("div");
            row.className = "ai-bar-row";
            row.title = `${label}: ${chart.values[i]}`;
            row.innerHTML = `<span class="ai-bar-label"></span><span class="ai-bar-track"><span class="ai-bar"></span></span><span class="ai-bar-value"></span>`;
            row.querySelector(".ai-bar-label").textContent = label;
            row.querySelector(".ai-bar-value").textContent = Number(chart.values[i]).toLocaleString();
            row.querySelector(".ai-bar").style.width = (100 * chart.values[i] / max) + "%";
            box.append(row);
        });
    }

    function drawPageCharts(root) {
        root.querySelectorAll("[data-ai-chart]").forEach(box => drawChart(box, JSON.parse(box.dataset.aiChart)));
        // Choosing a tender in the list opens its report.
        root.querySelectorAll("[data-ai-autosubmit]:not([data-ready])").forEach(select => {
            select.dataset.ready = "true";
            select.addEventListener("change", () => select.form.requestSubmit());
        });
    }

    // ------------------------------------------------------------ Table helpers
    function buildTable(table) {
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
        return wrap;
    }

    // Lets the user download the table of an answer as an Excel (CSV) file.
    function downloadCsv(table) {
        const quote = v => `"${String(v).replaceAll('"', '""')}"`;
        const lines = [table.columns.map(quote).join(","), ...table.rows.map(r => r.map(quote).join(","))];
        const blob = new Blob(["﻿" + lines.join("\n")], { type: "text/csv" });
        const link = Object.assign(document.createElement("a"), { href: URL.createObjectURL(blob), download: "ai-answer.csv" });
        link.click();
        URL.revokeObjectURL(link.href);
    }

    // ------------------------------------------------------------ Chatbot
    const chat = document.querySelector("[data-ai-chat]");
    if (chat) {
        const messages = chat.querySelector("[data-ai-messages]");
        const form = chat.querySelector("[data-ai-form]");
        const question = chat.querySelector("[data-ai-question]");
        const file = chat.querySelector("[data-ai-file]");
        const fileName = chat.querySelector("[data-ai-file-name]");

        document.addEventListener("click", event => {
            if (event.target.closest("[data-ai-chat-open]")) { chat.hidden = false; question.focus(); }
            if (event.target.closest("[data-ai-chat-close]")) chat.hidden = true;
            const suggestion = event.target.closest("[data-ai-suggestion]");
            if (suggestion) { question.value = suggestion.textContent.trim(); form.requestSubmit(); }
        });

        file.addEventListener("change", () => fileName.textContent = file.files[0]?.name ?? "");

        function addMessage(kind, text) {
            const div = document.createElement("div");
            div.className = "ai-msg ai-msg-" + kind;
            div.textContent = text;
            messages.append(div);
            messages.scrollTop = messages.scrollHeight;
            return div;
        }

        form.addEventListener("submit", async event => {
            event.preventDefault();
            if (!question.value.trim() && !file.files.length) return;
            addMessage("user", question.value.trim() || file.files[0].name);
            const waiting = addMessage("bot", t("Thinking…"));
            const data = new FormData(form);
            question.value = "";
            file.value = "";
            fileName.textContent = "";

            try {
                const response = await fetch(form.action, { method: "POST", body: data });
                const answer = await response.json();
                waiting.remove();
                showAnswer(answer);
            } catch {
                waiting.textContent = t("Sorry, something went wrong. Please try again.");
            }
        });

        // Shows text, cards, chart, table and recommendations of one answer.
        function showAnswer(answer) {
            const box = addMessage("bot", answer.answer || "");
            if (answer.cards?.length) {
                const cards = document.createElement("div");
                cards.className = "ai-cards ai-cards-small";
                answer.cards.forEach(c => {
                    const card = document.createElement("div");
                    card.className = "ai-card";
                    card.innerHTML = "<strong></strong><span></span>";
                    card.querySelector("strong").textContent = c.value;
                    card.querySelector("span").textContent = t(c.label);
                    cards.append(card);
                });
                box.append(cards);
            }
            if (answer.chart?.values?.length) {
                const chartBox = document.createElement("div");
                chartBox.className = "ai-chart";
                drawChart(chartBox, { ...answer.chart, title: t(answer.chart.title) });
                box.append(chartBox);
            }
            if (answer.table?.rows?.length) {
                box.append(buildTable(answer.table));
                const download = Object.assign(document.createElement("button"),
                    { type: "button", className: "btn btn-sm btn-outline-secondary", textContent: t("Download as Excel") });
                download.addEventListener("click", () => downloadCsv(answer.table));
                box.append(download);
            }
            if (answer.recommendations?.length) {
                const title = Object.assign(document.createElement("strong"), { className: "d-block mt-2", textContent: t("AI recommendations (advice only)") });
                const list = document.createElement("ul");
                answer.recommendations.forEach(r => list.append(Object.assign(document.createElement("li"), { textContent: r })));
                box.append(title, list);
            }
            messages.scrollTop = messages.scrollHeight;
        }
    }

    // Run on the first page load and every time the workspace loads a new page.
    drawPageCharts(document);
    document.addEventListener("workspace:load", event => drawPageCharts(event.detail || document));
})();
