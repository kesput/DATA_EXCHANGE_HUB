// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

(() => {
    function normalizeSearchText(value) {
        return (value ?? "")
            .toString()
            .toLocaleLowerCase("lt-LT")
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "");
    }

    function getGroups(input) {
        return (input.dataset.searchGroups ?? "")
            .split(/\s+/)
            .map((group) => group.trim())
            .filter(Boolean);
    }

    function itemMatches(item, terms) {
        if (terms.length === 0) {
            return true;
        }

        const searchText = normalizeSearchText(
            item.dataset.searchText || item.textContent);
        return terms.every((term) => searchText.includes(term));
    }

    function updateSearchGroup(group, terms) {
        const lists = document.querySelectorAll(`[data-search-list="${group}"]`);
        let visibleCount = 0;

        lists.forEach((list) => {
            list.querySelectorAll("[data-search-item]").forEach((item) => {
                const isVisible = itemMatches(item, terms);
                item.hidden = !isVisible;
                if (isVisible) {
                    visibleCount += 1;
                }
            });
        });

        document.querySelectorAll(`[data-search-empty="${group}"]`).forEach((emptyState) => {
            emptyState.hidden = visibleCount !== 0;
        });
    }

    function applyListSearch(input) {
        const terms = normalizeSearchText(input.value)
            .split(/\s+/)
            .map((term) => term.trim())
            .filter(Boolean);

        getGroups(input).forEach((group) => updateSearchGroup(group, terms));
    }
    document.addEventListener("DOMContentLoaded", () => {
        document.querySelectorAll("[data-list-search]").forEach((input) => {
            input.addEventListener("input", () => applyListSearch(input));
            applyListSearch(input);
        });
    });
})();
(() => {
    function getDateFormat() {
        return document.documentElement.lang?.toLowerCase().startsWith("en")
            ? "dd/MM/yyyy"
            : "yyyy-MM-dd";
    }

    function formatDate(isoValue) {
        if (!isoValue) {
            return "";
        }

        const parts = isoValue.split("-");
        if (parts.length !== 3) {
            return isoValue;
        }

        const [year, month, day] = parts;
        return getDateFormat() === "dd/MM/yyyy"
            ? `${day}/${month}/${year}`
            : `${year}-${month}-${day}`;
    }

    function openDatePicker(nativeInput) {
        nativeInput.focus();
        if (typeof nativeInput.showPicker === "function") {
            nativeInput.showPicker();
        }
    }
    document.addEventListener("DOMContentLoaded", () => {
        document.querySelectorAll(".localized-date-field").forEach((field) => {
            const nativeInput = field.querySelector("[data-localized-date-native]");
            const displayInput = field.querySelector("[data-localized-date-display]");
            if (!nativeInput || !displayInput) {
                return;
            }

            const syncDisplay = () => {
                displayInput.value = formatDate(nativeInput.value);
            };

            displayInput.addEventListener("click", () => openDatePicker(nativeInput));
            displayInput.addEventListener("keydown", (event) => {
                if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    openDatePicker(nativeInput);
                }
            });
            nativeInput.addEventListener("change", syncDisplay);
            syncDisplay();
        });
    });
})();
(() => {
    function clipboardFileName(type) {
        const extension = type === "image/jpeg" ? "jpg" : "png";
        const stamp = new Date()
            .toISOString()
            .replace(/[-:]/g, "")
            .replace(/\..+$/, "")
            .replace("T", "_");
        return `clipboard_${stamp}.${extension}`;
    }

    function setStatus(form, message) {
        const status = form.querySelector("[data-clipboard-file-status]");
        if (status) {
            status.textContent = message;
        }
    }

    function attachClipboardFile(input, blob) {
        const file = new File([blob], clipboardFileName(blob.type), { type: blob.type || "image/png" });
        const transfer = new DataTransfer();
        transfer.items.add(file);
        input.files = transfer.files;
        input.dispatchEvent(new Event("change", { bubbles: true }));
        return file;
    }

    function handlePaste(form, event) {
        const input = form.querySelector("[data-clipboard-file-input]");
        if (!input || !event.clipboardData?.items?.length) {
            return;
        }

        for (const item of event.clipboardData.items) {
            if (!item.type.startsWith("image/")) {
                continue;
            }

            const blob = item.getAsFile();
            if (!blob) {
                continue;
            }

            const file = attachClipboardFile(input, blob);
            setStatus(form, `Ikeltas is clipboard: ${file.name}. Failu: ${input.files.length}`);
            event.preventDefault();
            break;
        }
    }
    document.addEventListener("DOMContentLoaded", () => {
        document.querySelectorAll("[data-clipboard-file-form]").forEach((form) => {
            form.addEventListener("paste", (event) => handlePaste(form, event));
        });
    });
})();

(() => {    document.addEventListener("DOMContentLoaded", () => {
        if (!window.bootstrap?.Tooltip) {
            return;
        }

        document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach((element) => {
            new bootstrap.Tooltip(element, {
                trigger: "hover focus"
            });
        });
    });
})();
(() => {
    function getBusyText(form, submitter) {
        const text = (submitter?.textContent ?? "").trim().toLowerCase();
        const title = submitter?.dataset.busyTitle
            || form.dataset.busyTitle
            || (text.includes("excel") ? "Failas ruosiamas" : "Uzklausa vykdoma");
        const message = submitter?.dataset.busyMessage
            || form.dataset.busyMessage
            || "Palaukite, skaiciavimas dar vykdomas. Neuzdarykite puslapio ir nespauskite mygtuko dar karta.";
        const label = submitter?.dataset.busyLabel
            || (text.includes("excel") ? "Ruosiamas Excel..." : "Vykdoma...");

        return { title, message, label };
    }

    function ensureBusyPanel(form, title, message) {
        let panel = form.querySelector("[data-busy-panel]");
        if (!panel) {
            panel = document.createElement("div");
            panel.className = "query-busy-progress";
            panel.dataset.busyPanel = "";
            panel.setAttribute("role", "status");
            panel.setAttribute("aria-live", "polite");
            panel.innerHTML = `
                <div class="query-busy-spinner" aria-hidden="true"></div>
                <div>
                    <strong data-busy-panel-title></strong>
                    <span data-busy-panel-message></span>
                </div>`;

            const actionRow = form.querySelector(".form-actions, .oracul-actions, .exchange-filter-actions, .d-flex.gap-2, .excel-actions");
            if (actionRow?.parentElement === form || actionRow?.closest("form") === form) {
                actionRow.insertAdjacentElement("afterend", panel);
            } else {
                form.appendChild(panel);
            }
        }

        panel.querySelector("[data-busy-panel-title]").textContent = title;
        panel.querySelector("[data-busy-panel-message]").textContent = message;
        panel.hidden = false;
        return panel;
    }

    function resetBusyForm(form) {
        delete form.dataset.submitting;
        form.classList.remove("is-submitting");
        form.querySelectorAll("[data-busy-panel]").forEach((panel) => {
            panel.hidden = true;
        });
        form.querySelectorAll("button[type='submit'], input[type='submit']").forEach((button) => {
            button.disabled = false;
            if (button.dataset.originalText) {
                button.textContent = button.dataset.originalText;
            }
        });
    }

    function isExportAction(element) {
        const handler = (element?.getAttribute("asp-page-handler") || element?.formAction || element?.href || "").toLowerCase();
        const text = (element?.textContent || "").toLowerCase();
        return handler.includes("exportexcel") || handler.includes("exportcsv") || text.includes("export excel") || text.includes("export csv");
    }

    function getExportFormat(element) {
        const value = `${element?.formAction || element?.href || ""} ${element?.textContent || ""}`.toLowerCase();
        return value.includes("csv") ? "CSV" : "Excel";
    }

    function setSubmittingState(form, submitter, busyText) {
        form.dataset.submitting = "true";
        form.classList.add("is-submitting");

        const panel = ensureBusyPanel(form, busyText.title, busyText.message);
        panel.scrollIntoView({ behavior: "smooth", block: "center" });

        form.querySelectorAll("button[type='submit'], input[type='submit']").forEach((button) => {
            button.dataset.originalText ||= button.textContent;
            button.disabled = true;
        });

        if (submitter && "textContent" in submitter) {
            submitter.dataset.originalText ||= submitter.textContent;
            submitter.textContent = busyText.label;
        }
    }

    function getDownloadFileName(response) {
        const disposition = response.headers.get("Content-Disposition") || "";
        const utfMatch = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
        if (utfMatch) {
            return decodeURIComponent(utfMatch[1]);
        }

        const match = /filename="?([^";]+)"?/i.exec(disposition);
        return match ? match[1] : "export";
    }

    function downloadBlob(blob, fileName) {
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = fileName || "export";
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(url);
    }

    function showExportSummary(anchor, response, format) {
        const rowCount = response.headers.get("X-Export-Row-Count") || "0";
        const columnCount = response.headers.get("X-Export-Column-Count") || "0";
        const host = anchor.closest("section, .tool-section, .hub-board, .exchange-content") || anchor.parentElement;
        if (!host) {
            return;
        }

        let summary = host.querySelector("[data-export-summary-metrics]");
        if (!summary) {
            summary = document.createElement("div");
            summary.className = "hub-metrics my-4 export-summary-metrics";
            summary.dataset.exportSummaryMetrics = "";
            const table = host.querySelector(".table-responsive, table.exchange-table");
            if (table) {
                table.insertAdjacentElement("beforebegin", summary);
            } else {
                anchor.closest("form, .excel-actions")?.insertAdjacentElement("afterend", summary);
            }
        }

        summary.innerHTML = `
            <div class="metric-card">
                <small>Exportuotos eilutes</small>
                <h4>${rowCount}</h4>
            </div>
            <div class="metric-card">
                <small>Exportuoti stulpeliai</small>
                <h4>${columnCount}</h4>
            </div>
            <div class="metric-card">
                <small>Formatas</small>
                <h4>${format}</h4>
            </div>`;
    }

    async function handleExportSubmit(event, form, submitter) {
        event.preventDefault();
        event.stopImmediatePropagation();
        const format = getExportFormat(submitter);
        const busyText = {
            title: `${format} exportavimas vykdomas`,
            message: `Ruosiamas ${format} failas. Neuzdarykite puslapio ir nespauskite mygtuko dar karta.`,
            label: `Ruosiamas ${format}...`
        };
        setSubmittingState(form, submitter, busyText);

        try {
            const formData = new FormData(form);
            const action = submitter?.formAction || form.action || window.location.href;
            const response = await fetch(action, {
                method: (form.method || "POST").toUpperCase(),
                body: formData,
                credentials: "same-origin"
            });

            if (!response.ok) {
                window.location.href = action;
                return;
            }

            const blob = await response.blob();
            downloadBlob(blob, getDownloadFileName(response));
            showExportSummary(form, response, format);
        } finally {
            resetBusyForm(form);
        }
    }

    async function handleExportLink(event, link) {
        event.preventDefault();
        const format = getExportFormat(link);
        let panelHost = link.closest(".excel-actions") || link.parentElement;
        const fakeForm = panelHost || document.body;
        const busyText = {
            title: `${format} exportavimas vykdomas`,
            message: `Ruosiamas ${format} failas. Neuzdarykite puslapio ir nespauskite nuorodos dar karta.`,
            label: `Ruosiamas ${format}...`
        };
        const originalText = link.textContent;
        ensureBusyPanel(fakeForm, busyText.title, busyText.message);
        link.textContent = busyText.label;
        link.classList.add("disabled");

        try {
            const response = await fetch(link.href, { credentials: "same-origin" });
            if (!response.ok) {
                window.location.href = link.href;
                return;
            }

            const blob = await response.blob();
            downloadBlob(blob, getDownloadFileName(response));
            showExportSummary(link, response, format);
        } finally {
            link.textContent = originalText;
            link.classList.remove("disabled");
            fakeForm.querySelectorAll?.("[data-busy-panel]").forEach((panel) => {
                panel.hidden = true;
            });
        }
    }

    document.addEventListener("submit", (event) => {
        const form = event.target;
        const submitter = event.submitter;
        if (!(form instanceof HTMLFormElement) || !isExportAction(submitter)) {
            return;
        }

        if (form.dataset.submitting === "true") {
            event.preventDefault();
            event.stopImmediatePropagation();
            return;
        }

        handleExportSubmit(event, form, submitter);
    }, true);
    document.addEventListener("DOMContentLoaded", () => {
        document.querySelectorAll("form[data-busy-form]").forEach((form) => {
            form.addEventListener("submit", (event) => {
                if (form.dataset.submitting === "true") {
                    event.preventDefault();
                    return;
                }

                const submitter = event.submitter;
                if (isExportAction(submitter)) {
                    handleExportSubmit(event, form, submitter);
                    return;
                }

                const busyText = getBusyText(form, submitter);
                setSubmittingState(form, submitter, busyText);

                const autoResetMs = Number(submitter?.dataset.busyAutoResetMs || form.dataset.busyAutoResetMs || 0);
                if (autoResetMs > 0) {
                    window.setTimeout(() => resetBusyForm(form), autoResetMs);
                }
            });
        });

        document.querySelectorAll("a[href*='ExportExcel'], a[href*='ExportCsv']").forEach((link) => {
            link.addEventListener("click", (event) => handleExportLink(event, link));
        });
    });
})();
(() => {
    document.addEventListener("DOMContentLoaded", () => {
        if (!window.bootstrap?.Tab) {
            return;
        }

        const tabs = document.querySelectorAll('#kairosTabs [data-bs-toggle="tab"]');
        if (tabs.length === 0) {
            return;
        }

        const showTabFromHash = () => {
            if (!window.location.hash) {
                return;
            }

            const target = document.querySelector(
                `#kairosTabs [data-bs-target="${window.location.hash}"]`);
            if (target) {
                bootstrap.Tab.getOrCreateInstance(target).show();
            }
        };

        tabs.forEach((tab) => {
            tab.addEventListener("shown.bs.tab", (event) => {
                const target = event.target?.dataset?.bsTarget;
                if (target) {
                    history.replaceState(null, "", target);
                }
            });
        });

        showTabFromHash();
        window.addEventListener("hashchange", showTabFromHash);
    });
})();
(() => {
    function parseSortValue(rawValue) {
        const value = (rawValue ?? "").trim();
        const normalizedNumber = value
            .replace(/\s/g, "")
            .replace(",", ".");
        const numberValue = Number(normalizedNumber);

        if (value !== "" && Number.isFinite(numberValue)) {
            return { kind: "number", value: numberValue };
        }

        const dateValue = Date.parse(value);
        if (!Number.isNaN(dateValue) && /\d{4}|\d{1,2}[./-]\d{1,2}/.test(value)) {
            return { kind: "date", value: dateValue };
        }

        return { kind: "text", value };
    }

    function compareValues(left, right, direction) {
        const multiplier = direction === "asc" ? 1 : -1;

        if (left.value === "" || right.value === "") {
            if (left.value === right.value) {
                return 0;
            }

            return left.value === "" ? 1 : -1;
        }

        if (left.kind === right.kind && (left.kind === "number" || left.kind === "date")) {
            return (left.value - right.value) * multiplier;
        }

        return String(left.value).localeCompare(String(right.value), "lt", {
            numeric: true,
            sensitivity: "base"
        }) * multiplier;
    }

    function sortTable(table, columnIndex, header) {
        const tbody = table.tBodies[0];
        if (!tbody || Number.isNaN(columnIndex)) {
            return;
        }

        const direction = table.dataset.sortColumn === String(columnIndex) && table.dataset.sortDirection === "asc"
            ? "desc"
            : "asc";
        const rows = Array.from(tbody.rows);

        rows.sort((leftRow, rightRow) => compareValues(
            parseSortValue(leftRow.cells[columnIndex]?.textContent),
            parseSortValue(rightRow.cells[columnIndex]?.textContent),
            direction));

        rows.forEach(row => tbody.appendChild(row));
        table.dataset.sortColumn = String(columnIndex);
        table.dataset.sortDirection = direction;

        table.querySelectorAll("thead th").forEach(item => item.classList.remove("sort-asc", "sort-desc"));
        header.classList.add(direction === "asc" ? "sort-asc" : "sort-desc");
    }
    document.addEventListener("DOMContentLoaded", () => {
        document.querySelectorAll("table.exchange-table").forEach((table) => {
            if (table.querySelector("th[data-sort-column], th[data-hydra-sort-column]")) {
                return;
            }

            table.dataset.genericSortable = "true";
            table.querySelectorAll("thead th").forEach((header, index) => {
                if (header.classList.contains("row-actions") || header.classList.contains("text-end")) {
                    return;
                }

                header.classList.add("sortable-header");
                header.title = header.title || "Double click to sort";
                header.addEventListener("dblclick", () => sortTable(table, index, header));
            });
        });
    });
})();
(() => {
    function setIrisSourceFieldState(field, isEnabled) {
        if (!field) {
            return;
        }

        field.disabled = !isEnabled;
        field.closest("div")?.classList.toggle("iris-disabled-field", !isEnabled);
    }

    function syncIrisSqlTextField(select) {
        const form = select.closest("form");
        if (!form) {
            return;
        }

        setIrisSourceFieldState(form.querySelector("[data-iris-sql-text]"), select.value === "INLINE_SQL");
        setIrisSourceFieldState(form.querySelector("[data-iris-sql-file]"), select.value === "SQL_FILE");
        setIrisSourceFieldState(form.querySelector("[data-iris-stored-procedure]"), select.value === "STORED_PROCEDURE");
    }

    document.addEventListener("DOMContentLoaded", () => {
        document.querySelectorAll("[data-iris-source-type]").forEach((select) => {
            syncIrisSqlTextField(select);
            select.addEventListener("change", () => syncIrisSqlTextField(select));
        });
    });
})();
(() => {
    function syncIrisScheduler(form) {
        const selected = form.querySelector("[data-iris-schedule-type]:checked")?.value || "MANUAL";
        form.querySelectorAll("[data-iris-schedule-panel]").forEach((panel) => {
            panel.classList.toggle("is-active", panel.dataset.irisSchedulePanel === selected);
        });

        const lastDay = form.querySelector("[data-iris-last-day]");
        const monthDay = form.querySelector("[data-iris-month-day]");
        const monthDayRow = form.querySelector("[data-iris-month-day-row]");
        const useLastDay = Boolean(lastDay?.checked);
        if (monthDay) {
            monthDay.disabled = useLastDay;
        }
        monthDayRow?.classList.toggle("iris-disabled-field", useLastDay);

        const noEndDate = form.querySelector("[data-iris-no-end-date]");
        const endDate = form.querySelector("[data-iris-end-date]");
        const endDateRow = form.querySelector("[data-iris-end-date-row]");
        const isInfinite = Boolean(noEndDate?.checked);
        if (endDate) {
            endDate.disabled = isInfinite;
        }
        endDateRow?.classList.toggle("iris-disabled-field", isInfinite);

        form.querySelectorAll("[data-iris-date-text]").forEach((input) => {
            const picker = document.getElementById(input.dataset.irisDateText || "");
            if (picker && picker.value !== input.value) {
                picker.value = input.value;
            }
        });
    }

    document.addEventListener("DOMContentLoaded", () => {
        document.querySelectorAll("[data-iris-scheduler-form]").forEach((form) => {
            syncIrisScheduler(form);
            form.querySelectorAll("[data-iris-schedule-type], [data-iris-last-day], [data-iris-no-end-date], [data-iris-date-text]").forEach((input) => {
                input.addEventListener("change", () => syncIrisScheduler(form));
            });
            form.querySelectorAll("[data-iris-date-button]").forEach((button) => {
                button.addEventListener("click", () => {
                    const picker = document.getElementById(button.dataset.irisDateButton || "");
                    if (!picker) {
                        return;
                    }
                    if (typeof picker.showPicker === "function") {
                        picker.showPicker();
                    } else {
                        picker.click();
                    }
                });
            });
            form.querySelectorAll("[data-iris-date-picker]").forEach((picker) => {
                picker.addEventListener("change", () => {
                    const textInput = form.querySelector(`[data-iris-date-text="${picker.id}"]`);
                    if (textInput) {
                        textInput.value = picker.value;
                        syncIrisScheduler(form);
                    }
                });
            });
        });
    });
})();




