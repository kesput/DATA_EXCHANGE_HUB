(() => {
    const initPages = result => {
        result.querySelectorAll('[data-iris-preview-table]').forEach(table => {
            const rows = [...table.querySelectorAll('tbody > tr')];
            const previous = table.querySelector('[data-iris-preview-previous]');
            const next = table.querySelector('[data-iris-preview-next]');
            const info = table.querySelector('[data-iris-preview-page-info]');
            if (!previous || !next || !info) return;
            let page = 0;
            const size = 50;
            const update = () => {
                const start = page * size;
                const end = Math.min(start + size, rows.length);
                rows.forEach((row, index) => { row.hidden = index < start || index >= end; });
                previous.disabled = page === 0;
                next.disabled = end === rows.length;
                const values = [start + 1, end, rows.length, page + 1, Math.ceil(rows.length / size)];
                info.textContent = info.dataset.template.replace(/\{(\d)\}/g, (_, index) => values[Number(index)]);
                table.querySelector('.iris-preview-table-scroll').scrollTop = 0;
            };
            previous.addEventListener('click', () => { if (page > 0) { page--; update(); } });
            next.addEventListener('click', () => { if ((page + 1) * size < rows.length) { page++; update(); } });
            update();
        });
    };
    document.querySelectorAll('[data-iris-source-preview-form]').forEach(form => {
        const button = form.querySelector('[data-iris-source-preview]');
        const result = document.getElementById('irisSourcePreviewResult');
        if (!button || !result) return;
        const panel = result.closest('[data-iris-source-preview-panel]');
        let revision = 0;
        button.addEventListener('click', async () => {
            if (button.disabled) return;
            const startedAt = revision;
            const label = button.textContent;
            button.disabled = true;
            button.textContent = button.dataset.loading;
            result.hidden = false;
            panel.hidden = false;
            result.setAttribute('aria-busy', 'true');
            result.textContent = button.dataset.loading;
            try {
                const response = await fetch(button.dataset.url, {
                    method: 'POST', body: new FormData(form), credentials: 'same-origin',
                    headers: { 'X-Requested-With': 'XMLHttpRequest' }
                });
                if (!response.ok || response.redirected || !response.headers.get('content-type')?.includes('text/html'))
                    throw new Error(button.dataset.error);
                const html = await response.text();
                if (revision === startedAt) {
                    result.innerHTML = html;
                    initPages(result);
                }
            } catch {
                if (revision !== startedAt) return;
                const error = document.createElement('div');
                error.className = 'alert alert-danger mb-0';
                error.setAttribute('role', 'alert');
                error.textContent = button.dataset.error;
                result.replaceChildren(error);
            } finally {
                result.removeAttribute('aria-busy');
                button.disabled = false;
                button.textContent = label;
                if (revision === startedAt) panel.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }
        });
        const invalidate = () => { revision++; result.hidden = true; panel.hidden = true; };
        form.addEventListener('input', invalidate);
        form.addEventListener('change', invalidate);
    });
})();
