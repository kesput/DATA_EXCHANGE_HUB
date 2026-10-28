// Share the Excel option across export actions on the current page.
(() => {
    const modules = new Set(['apis', 'arete', 'hydra', 'oracul', 'clientinfo', 'themis', 'dataexchange', 'kairos', 'sphinx']);
    const module = location.pathname.split('/').filter(Boolean)[0]?.toLowerCase();
    if (!modules.has(module)) return;
    const handlers = new Set(['exportexcel', 'exporttemplate', 'exportsurveytemplate',
        'exportpccpickdelaycountryexcel', 'exportpromoforecastexcel', 'exportpromoanalysisexcel']);
    const excelHandler = (value) => handlers.has((value || '').toLowerCase()) ||
        (module === 'sphinx' && (value || '').toLowerCase() === 'results');
    const links = [...document.querySelectorAll('a[href]')].filter((link) =>
        excelHandler(new URL(link.href, location.href).searchParams.get('handler')));
    const buttons = [...document.querySelectorAll('button')].filter((button) => {
        if (button.name === 'handler') return excelHandler(button.value);
        const action = button.getAttribute('formaction');
        return action && excelHandler(new URL(action, location.href).searchParams.get('handler'));
    });
    const forms = new Set(buttons.map((button) => button.form).filter(Boolean));
    document.querySelectorAll('form input[name="handler"]').forEach((input) => {
        if (excelHandler(input.value)) forms.add(input.form);
    });
    const controls = [];
    const english = document.documentElement.lang.toLowerCase().startsWith('en');
    let selected = new URL(location.href).searchParams.get('ExcelGridlines') === 'true';

    function update() {
        controls.forEach((control) => { control.checked = selected; });
        forms.forEach((form) => {
            let input = form.querySelector('input[type="hidden"][name="ExcelGridlines"]');
            if (!input) {
                input = document.createElement('input');
                input.type = 'hidden';
                input.name = 'ExcelGridlines';
                form.append(input);
            }
            input.value = String(selected);
        });
        links.forEach((link) => {
            const url = new URL(link.href, location.href);
            url.searchParams.set('ExcelGridlines', String(selected));
            link.href = url.href;
        });
    }

    function addChoice(parent, before) {
        const label = document.createElement('label');
        label.className = 'form-check d-inline-flex align-items-center gap-2 me-3 my-1';
        label.title = english
            ? 'Add black borders to headers and all exported data cells.'
            : 'Juodos kraštinės antraštėms ir visiems eksportuojamos lentelės langeliams.';
        const checkbox = document.createElement('input');
        checkbox.type = 'checkbox';
        checkbox.className = 'form-check-input m-0';
        const text = document.createElement('span');
        text.className = 'form-check-label';
        text.textContent = english ? 'Excel cell borders' : 'Excel langelių kraštinės';
        label.append(checkbox, text);
        parent.insertBefore(label, before);
        controls.push(checkbox);
        checkbox.addEventListener('change', () => {
            selected = checkbox.checked;
            update();
        });
    }

    forms.forEach((form) => {
        const button = buttons.find((candidate) => candidate.form === form);
        if (button) addChoice(button.parentNode, button);
        else addChoice(form, form.querySelector('button[type="submit"]'));
    });
    const groups = new Set();
    links.forEach((link) => {
        if (groups.has(link.parentNode)) return;
        groups.add(link.parentNode);
        // One option also controls template links in the same action group.
        if (controls.some(control => link.parentNode.contains(control))) return;
        addChoice(link.parentNode, link);
    });
    update();
})();
