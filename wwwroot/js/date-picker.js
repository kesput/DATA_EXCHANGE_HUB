// Keep form values in ISO format while displaying dates in the selected UI language.
(() => {
    const english = document.documentElement.lang.toLowerCase().startsWith('en');
    const states = new WeakMap();
    const compactStates = new WeakMap();
    const activeStates = new Set();
    const kinds = new Set(['date', 'month', 'datetime-local']);
    let sequence = 0;

    function format(value, kind) {
        if (!value) return '';
        if (kind === 'month') return value.replace('-', '');
        const [date, time] = value.split('T');
        const [year, month, day] = date.split('-');
        return (english ? `${day}/${month}/${year}` : date) + (time ? ` ${time}` : '');
    }

    function parse(value, kind, acceptIso = false) {
        if (!value.trim()) return '';
        if (kind === 'month') {
            const match = /^(\d{4})-?(0[1-9]|1[0-2])$/.exec(value.trim());
            return match && Number(match[1]) > 0 ? `${match[1]}-${match[2]}` : null;
        }
        const parts = value.trim().split(/[T ]/);
        let date = parts[0];
        if (english && !(acceptIso && /^\d{4}-\d{2}-\d{2}$/.test(date))) {
            const match = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(date);
            if (!match) return null;
            date = `${match[3]}-${match[2]}-${match[1]}`;
        }
        if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) return null;
        const [year, month, day] = date.split('-').map(Number);
        const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
        const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
        if (year < 1 || month < 1 || month > 12 || day < 1 || day > days[month - 1]) return null;
        if (kind === 'date') return parts.length === 1 ? date : null;
        if (parts.length !== 2 || !/^([01]\d|2[0-3]):[0-5]\d(?::[0-5]\d(?:\.\d{1,3})?)?$/.test(parts[1])) return null;
        return `${date}T${parts[1]}`;
    }

    function invalidMessage(value, kind, placeholder) {
        const date = value.trim().split(/[T ]/)[0];
        const dateShape = english ? /^\d{2}\/\d{2}\/\d{4}$/ : /^\d{4}-\d{2}-\d{2}$/;
        const nonexistent = kind === 'month'
            ? /^\d{4}-?\d{2}$/.test(value.trim()) && parse(value, kind) === null
            : dateShape.test(date) && parse(date, 'date') === null;
        if (nonexistent) return english
            ? 'This date does not exist. Check the year, month and day.'
            : 'Tokios datos nėra. Patikrinkite metus, mėnesį ir dieną.';
        return english ? `Enter a valid value: ${placeholder}.` : `Įveskite teisingą reikšmę: ${placeholder}.`;
    }

    function convertText(input, kind, value = input.value) {
        const iso = parse(value, kind, true);
        input.removeAttribute('pattern');
        input.removeAttribute('inputmode');
        input.removeAttribute('readonly');
        input.type = kind;
        input.value = iso ?? '';
        input.classList.remove('apis-date-display', 'localized-date-native');
    }

    function adoptExisting() {
        document.querySelectorAll('.apis-date-input').forEach(group => {
            const input = group.querySelector('.apis-date-display');
            if (!input) return;
            convertText(input, 'date');
            group.replaceWith(input);
        });
        document.querySelectorAll('.localized-date-field').forEach(group => {
            const native = group.querySelector('[data-localized-date-native]');
            if (!native) return;
            native.classList.remove('localized-date-native');
            group.replaceWith(native);
        });
        document.querySelectorAll('.iris-date-combo').forEach(group => {
            const input = group.querySelector('[data-iris-date-text]');
            if (!input) return;
            input.removeAttribute('data-iris-date-text');
            convertText(input, 'date');
            group.replaceWith(input);
        });
        document.querySelectorAll('[data-atlas-date-input]').forEach(input => {
            input.removeAttribute('data-atlas-date-input');
            convertText(input, 'date');
        });
        ['dikeAgreementStart', 'dikeFirstPeriod'].forEach(id => {
            const input = document.getElementById(id);
            if (!input) return;
            const group = input.closest('.input-group');
            convertText(input, id === 'dikeFirstPeriod' ? 'month' : 'date');
            if (group) group.replaceWith(input);
        });
        const monthHint = document.getElementById('dikeFirstPeriodHint');
        if (monthHint) monthHint.textContent = english ? 'First reconciliation period: YYYYMM.' : 'Pirmo derinimo laikotarpis formatu YYYYMM.';
    }

    function enhance(native) {
        if (!kinds.has(native.type)) return;
        let state = states.get(native);
        if (state) { refresh(state); return; }

        // Filter rows can be cloned after enhancement. Discard the cloned UI.
        const cloneGroup = native.closest('.hub-date-field');
        if (cloneGroup) {
            native.className = native.dataset.hubDateOriginalClass || 'form-control';
            cloneGroup.replaceWith(native);
        }
        const focused = document.activeElement === native;
        const group = document.createElement('span');
        group.className = 'input-group has-validation hub-date-field';
        if (native.classList.contains('form-control-sm')) group.classList.add('input-group-sm');
        if (native.style.maxWidth) group.style.maxWidth = native.style.maxWidth;
        const display = document.createElement('input');
        display.type = 'text';
        display.className = native.className || 'form-control';
        display.classList.remove('visually-hidden');
        display.classList.add('form-control');
        display.id = `hub-date-display-${++sequence}`;
        display.autocomplete = 'off';
        display.spellcheck = false;
        display.setAttribute('aria-label', native.getAttribute('aria-label') || native.labels?.[0]?.textContent.trim() || (english ? 'Date' : 'Data'));
        // Explicit labels should focus the visible field rather than the native picker.
        const labels = Array.from(native.labels || []);
        labels.forEach(label => {
            if (native.id && label.htmlFor === native.id) label.htmlFor = display.id;
        });
        const describedBy = (native.getAttribute('aria-describedby') || '').split(/\s+/).filter(id => id && document.getElementById(id));
        const error = document.createElement('span');
        error.className = 'invalid-feedback';
        error.id = `hub-date-feedback-${sequence}`;
        error.setAttribute('aria-live', 'polite');
        const hint = document.createElement('span');
        hint.className = 'form-text w-100';
        hint.id = `hub-date-range-hint-${sequence}`;
        hint.hidden = true;
        display.setAttribute('aria-describedby', [...describedBy, error.id, hint.id].join(' '));
        if (native.hasAttribute('form')) display.setAttribute('form', native.getAttribute('form'));
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'btn btn-outline-secondary hub-date-button';
        button.textContent = '\u{1F4C5}';
        button.title = english ? 'Choose from calendar' : 'Pasirinkti kalendoriuje';
        button.setAttribute('aria-label', `${button.title}: ${display.getAttribute('aria-label')}`);
        native.dataset.hubDateOriginalClass = native.className;
        native.className = 'hub-date-native';
        native.tabIndex = -1;
        native.setAttribute('aria-hidden', 'true');
        native.before(group);
        group.append(display, button, native, error, hint);
        const controller = new AbortController();
        state = { native, display, button, group, error, hint, labels, controller, kind: native.type,
            baseMin: native.min, baseMax: native.max, appliedMin: native.min, appliedMax: native.max };
        states.set(native, state);
        activeStates.add(state);

        const sync = () => {
            if (state.committing || !native.isConnected || !kinds.has(native.type)) return;
            display.value = format(native.value, native.type);
            state.lastValue = native.value;
            updateRanges();
        };
        native.addEventListener('change', () => { state.touched = true; sync(); }, { signal: controller.signal });
        native.addEventListener('input', () => { state.touched = true; sync(); }, { signal: controller.signal });
        native.addEventListener('invalid', event => {
            event.preventDefault();
            state.touched = true;
            validate(state);
            display.focus();
            display.reportValidity();
        }, { signal: controller.signal });
        display.addEventListener('input', () => commit(state, false));
        display.addEventListener('change', () => commit(state, true));
        display.addEventListener('blur', () => validate(state));
        display.addEventListener('invalid', () => { state.touched = true; validate(state); });
        button.addEventListener('click', () => {
            if (native.disabled || native.readOnly) return;
            updateRanges();
            if (typeof native.showPicker === 'function') native.showPicker();
            else { native.focus(); native.click(); }
        });
        display.addEventListener('keydown', event => {
            if (event.key === 'Escape' || event.key === 'Enter') {
                const forwarded = new KeyboardEvent('keydown', { key: event.key, bubbles: true, cancelable: true });
                native.dispatchEvent(forwarded);
                if (forwarded.defaultPrevented) event.preventDefault();
            }
        });
        group.addEventListener('focusout', event => {
            if (group.contains(event.relatedTarget)) return;
            if (validate(state)) native.dispatchEvent(new FocusEvent('blur'));
        });
        native.form?.addEventListener('reset', () => setTimeout(sync, 0), { signal: controller.signal });
        refresh(state);
        sync();
        if (focused) display.focus();
    }

    function commit(state, change) {
        const { native, display, kind } = state;
        state.touched = true;
        const iso = parse(display.value, kind);
        native.value = iso ?? '';
        state.lastValue = native.value;
        updateRanges();
        // Existing handlers must receive the canonical value, not the localized text.
        state.committing = true;
        try { native.dispatchEvent(new Event(change ? 'change' : 'input', { bubbles: true })); }
        finally { state.committing = false; }
        if (change && iso !== null) {
            display.value = format(native.value, kind);
            state.lastValue = native.value;
            validate(state);
        }
    }

    function validate(state) {
        const { native, display, error, kind } = state;
        const iso = parse(display.value, kind);
        let message = '';
        if (iso === null) message = invalidMessage(display.value, kind, display.placeholder);
        else if (state.rangeMessage) message = state.rangeMessage;
        else if (native.validity.valueMissing) message = window.HubI18n.t('This field is required.');
        else if (native.validity.rangeUnderflow) message = window.HubI18n.format('Date cannot be earlier than {0}.', format(native.min, kind));
        else if (native.validity.rangeOverflow) message = window.HubI18n.format('Date cannot be later than {0}.', format(native.max, kind));
        else if (!native.validity.valid) message = window.HubI18n.t('Choose a valid date and time.');
        display.setCustomValidity(message);
        const showError = Boolean(message) && (state.touched || Boolean(state.rangeMessage));
        display.classList.toggle('is-invalid', showError);
        display.setAttribute('aria-invalid', showError ? 'true' : 'false');
        if (error.textContent !== message) error.textContent = message;
        return !message;
    }

    function refresh(state) {
        const { native, display, button } = state;
        const kindChanged = state.kind !== native.type;
        state.kind = native.type;
        display.disabled = native.disabled;
        display.readOnly = native.readOnly;
        display.required = native.required;
        button.disabled = native.disabled || native.readOnly;
        display.placeholder = native.type === 'month' ? 'YYYYMM' : (english ? 'DD/MM/YYYY' : 'YYYY-MM-DD') + (native.type === 'datetime-local' ? ' HH:mm' : '');
        if (kindChanged || state.lastValue !== native.value) {
            display.value = format(native.value, native.type);
            state.lastValue = native.value;
        }
    }

    function rangeRole(input) {
        const row = input.closest('.exchange-filter-row');
        const column = row?.querySelector('select[name="FilterColumns"], input[name="FilterColumns"], [data-apis-optional-name]');
        const key = (input.dataset.dateRangeField || column?.value || input.name || input.id)
            .replace(/^.*\[([^\]]+)\]$/, '$1').replace(/[^a-z0-9]/gi, '').toLowerCase();
        const pairs = [
            [['sndate'], ['endate']],
            [['startdate', 'startdatetext'], ['enddate', 'enddatetext']],
            [['datefrom'], ['dateto', 'datetill']],
            [['fromdate'], ['todate']],
            [['validfrom'], ['validto']],
            [['schedulestartdate'], ['scheduleendat']],
            [['needdate'], ['minexpirationrequest']],
            [['promostart'], ['promoend']],
            [['periodfrom'], ['periodto']]
        ];
        for (let index = 0; index < pairs.length; index++) {
            if (pairs[index][0].includes(key)) return { pair: index, side: 'start' };
            if (pairs[index][1].includes(key)) return { pair: index, side: 'end' };
        }
        return null;
    }

    function updateRanges() {
        const scopes = new Map();
        const bounds = new Map();
        for (const state of activeStates) {
            const { native } = state;
            if (!native.isConnected || !kinds.has(native.type)) { activeStates.delete(state); continue; }
            // Retain constraints changed by the module, in addition to pair constraints.
            if (native.min !== state.appliedMin) state.baseMin = native.min;
            if (native.max !== state.appliedMax) state.baseMax = native.max;
            state.rangeMessage = '';
            bounds.set(state, { min: state.baseMin, max: state.baseMax, hint: '' });
            const role = rangeRole(native);
            if (!role || native.disabled) continue;
            const scope = native.form || document;
            if (!scopes.has(scope)) scopes.set(scope, new Map());
            const groups = scopes.get(scope);
            const key = `${role.pair}:${native.type}`;
            if (!groups.has(key)) groups.set(key, { start: [], end: [] });
            groups.get(key)[role.side].push(state);
        }
        for (const groups of scopes.values()) for (const pair of groups.values()) {
            // Ambiguous repeated filters must not restrict unrelated fields.
            if (pair.start.length !== 1 || pair.end.length !== 1) continue;
            const start = pair.start[0], end = pair.end[0];
            const startBounds = bounds.get(start), endBounds = bounds.get(end);
            if (start.native.value) {
                const value = start.native.value;
                endBounds.min = endBounds.min && endBounds.min > value ? endBounds.min : value;
                endBounds.hint = english
                    ? `End date cannot be earlier than start date (${format(value, start.kind)}).`
                    : `Pabaigos data negali būti ankstesnė už pradžios datą (${format(value, start.kind)}).`;
            }
            if (end.native.value) {
                const value = end.native.value;
                startBounds.max = startBounds.max && startBounds.max < value ? startBounds.max : value;
                startBounds.hint = english
                    ? `Start date cannot be later than end date (${format(value, end.kind)}).`
                    : `Pradžios data negali būti vėlesnė už pabaigos datą (${format(value, end.kind)}).`;
            }
            if (start.native.value && end.native.value && start.native.valueAsNumber > end.native.valueAsNumber) {
                start.rangeMessage = startBounds.hint;
                end.rangeMessage = endBounds.hint;
            }
        }
        for (const [state, bound] of bounds) {
            state.appliedMin = bound.min;
            state.appliedMax = bound.max;
            if (state.native.min !== bound.min) state.native.min = bound.min;
            if (state.native.max !== bound.max) state.native.max = bound.max;
            if (state.hint.textContent !== bound.hint) state.hint.textContent = bound.hint;
            state.hint.hidden = !bound.hint;
            validate(state);
        }
    }

    function enhanceCompactMonth(input) {
        const existing = compactStates.get(input);
        if (existing) {
            existing.button.disabled = input.disabled || input.readOnly;
            existing.picker.disabled = input.disabled || input.readOnly;
            return;
        }
        const group = document.createElement('span');
        group.className = 'input-group input-group-sm has-validation hub-date-field';
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'btn btn-outline-secondary hub-date-button';
        button.textContent = '\u{1F4C5}';
        button.setAttribute('aria-label', english ? 'Choose month' : 'Pasirinkti menesi');
        const picker = document.createElement('input');
        picker.type = 'month';
        picker.className = 'hub-date-native';
        picker.dataset.hubDateSkip = '';
        picker.tabIndex = -1;
        picker.setAttribute('aria-hidden', 'true');
        const error = document.createElement('span');
        error.className = 'invalid-feedback';
        error.setAttribute('aria-live', 'polite');
        input.before(group);
        group.append(input, button, picker, error);
        const validate = () => {
            const valid = parse(input.value, 'month') !== null;
            const message = valid ? '' : invalidMessage(input.value, 'month', 'YYYYMM');
            input.setCustomValidity(message);
            input.classList.toggle('is-invalid', !valid);
            input.setAttribute('aria-invalid', valid ? 'false' : 'true');
            error.textContent = message;
        };
        button.addEventListener('click', () => {
            picker.value = parse(input.value, 'month') || '';
            if (typeof picker.showPicker === 'function') picker.showPicker();
            else { picker.focus(); picker.click(); }
        });
        picker.addEventListener('change', () => {
            input.value = format(picker.value, 'month');
            input.dispatchEvent(new Event('input', { bubbles: true }));
            input.dispatchEvent(new Event('change', { bubbles: true }));
            input.focus();
        });
        input.addEventListener('input', validate);
        input.addEventListener('blur', validate);
        compactStates.set(input, { button, picker });
        enhanceCompactMonth(input);
    }

    function configureFilters(root) {
        const rows = root.matches?.('.exchange-filter-row') ? [root] : root.querySelectorAll?.('.exchange-filter-row') || [];
        rows.forEach(row => {
            const column = row.querySelector('select[name="FilterColumns"]');
            const input = row.querySelector('input[name="FilterValues"]');
            if (!column?.querySelector('option[data-type]') || !input) return;
            const type = column.selectedOptions[0]?.dataset.type?.toLowerCase() || '';
            const operator = row.querySelector('select[name="FilterOperators"]')?.value.toLowerCase();
            const dateType = type === 'date' ? 'date' : ['datetime', 'datetime2', 'smalldatetime', 'datetimeoffset'].includes(type) ? 'datetime-local' : null;
            const desired = !['in', 'between', 'like'].includes(operator) ? dateType : null;
            if (desired && input.type !== desired) {
                const value = parse(input.value, desired, true);
                input.type = desired;
                input.value = value ?? '';
            } else if (!desired && kinds.has(input.type)) input.type = 'text';
        });
    }

    function scan(root = document) {
        const inputs = root instanceof HTMLInputElement ? [root] : root.querySelectorAll?.('input') || [];
        inputs.forEach(input => {
            if (input.hasAttribute('data-hub-date-skip')) return;
            if (input.hasAttribute('data-hub-month-compact')) { enhanceCompactMonth(input); return; }
            const state = states.get(input);
            if (state && !kinds.has(input.type)) {
                state.controller.abort();
                activeStates.delete(state);
                input.min = state.baseMin;
                input.max = state.baseMax;
                input.className = input.dataset.hubDateOriginalClass;
                input.removeAttribute('aria-hidden');
                input.removeAttribute('tabindex');
                state.labels.forEach(label => {
                    if (label.htmlFor === state.display.id) label.htmlFor = input.id;
                });
                state.group.replaceWith(input);
                states.delete(input);
            } else if (kinds.has(input.type)) enhance(input);
        });
    }

    adoptExisting();
    document.addEventListener('DOMContentLoaded', () => {
        configureFilters(document);
        scan();
        updateRanges();
        new MutationObserver(records => {
            for (const record of records) {
                if (record.type === 'attributes') scan(record.target);
                else record.addedNodes.forEach(node => {
                    if (node.nodeType === 1 && node.isConnected) { configureFilters(node); scan(node); }
                });
            }
            updateRanges();
        }).observe(document.body, { childList: true, subtree: true, attributes: true,
            attributeFilter: ['type', 'disabled', 'readonly', 'required', 'min', 'max', 'step', 'value'] });
        document.addEventListener('change', event => {
            if (event.target.matches('select[name="FilterColumns"], select[name="FilterOperators"]')) {
                const row = event.target.closest('.exchange-filter-row');
                if (row) { configureFilters(row); scan(row); }
                updateRanges();
            }
        });
        document.addEventListener('submit', event => {
            updateRanges();
            const invalid = [...activeStates].find(state => state.native.form === event.target &&
                !state.native.disabled && !state.display.validity.valid);
            if (!invalid) return;
            event.preventDefault();
            event.stopImmediatePropagation();
            invalid.touched = true;
            validate(invalid);
            invalid.display.focus();
            invalid.display.reportValidity();
        }, true);
    });
})();
