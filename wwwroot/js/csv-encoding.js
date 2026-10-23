// Keep the CSV encoding choice in sync across GET links and POST exports.
(() => {
    const isCsv = value => (value || '').toLowerCase() === 'exportcsv';
    const links = [...document.querySelectorAll('a[href]')].filter(link =>
        isCsv(new URL(link.href, location.href).searchParams.get('handler')));
    const buttons = [...document.querySelectorAll('button, input[type="submit"]')].filter(button =>
        (button.name === 'handler' && isCsv(button.value)) ||
        isCsv(new URL(button.getAttribute('formaction') || location.href, location.href).searchParams.get('handler')));
    const forms = new Set(buttons.map(button => button.form).filter(Boolean));
    document.querySelectorAll('form input[name="handler"]').forEach(input => {
        if (isCsv(input.value)) forms.add(input.form);
    });
    const controls = [];
    const english = document.documentElement.lang.toLowerCase().startsWith('en');
    let selected = new URL(location.href).searchParams.get('CsvWindows1257') === 'true';

    function update() {
        controls.forEach(control => { control.checked = selected; });
        forms.forEach(form => {
            let input = form.querySelector('input[type="hidden"][name="CsvWindows1257"]');
            if (!input) {
                input = document.createElement('input');
                input.type = 'hidden';
                input.name = 'CsvWindows1257';
                form.append(input);
            }
            input.value = String(selected);
        });
        links.forEach(link => {
            const url = new URL(link.href, location.href);
            url.searchParams.set('CsvWindows1257', String(selected));
            link.href = url.href;
        });
    }

    function addChoice(parent, before) {
        parent.classList.add('export-action-bar');
        const row = parent.parentElement;
        if (row?.classList.contains('row')) {
            const queryColumn = [...row.children].find(child => child !== parent && child.querySelector('select'));
            if (queryColumn) {
                row.classList.add('query-export-row');
                queryColumn.classList.add('query-export-select');
                parent.classList.add('query-export-actions');
            }
        }
        const label = document.createElement('label');
        label.className = 'form-check d-inline-flex align-items-center gap-2 me-3 my-1';
        label.title = english
            ? 'Checked: Windows-1257 without BOM, all values quoted. Unchecked: UTF-8 with BOM.'
            : 'Pažymėta: Windows-1257 be BOM, visos reikšmės kabutėse. Nepažymėta: UTF-8 su BOM.';
        const checkbox = document.createElement('input');
        checkbox.type = 'checkbox';
        checkbox.className = 'form-check-input m-0';
        const text = document.createElement('span');
        text.className = 'form-check-label';
        text.textContent = english ? 'CSV Windows-1257 (no BOM)' : 'CSV Windows-1257 (be BOM)';
        label.append(checkbox, text);
        parent.insertBefore(label, before);
        controls.push(checkbox);
        checkbox.addEventListener('change', () => { selected = checkbox.checked; update(); });
    }

    forms.forEach(form => {
        const button = buttons.find(candidate => candidate.form === form);
        if (button) addChoice(button.parentNode, button);
        else addChoice(form, form.querySelector('button[type="submit"]'));
    });
    const groups = new Set();
    links.forEach(link => {
        if (groups.has(link.parentNode)) return;
        groups.add(link.parentNode);
        addChoice(link.parentNode, link);
    });
    update();
})();
