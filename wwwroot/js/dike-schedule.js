document.addEventListener('DOMContentLoaded', () => {
    const form = document.getElementById('dikeAgreementForm');
    const preview = document.getElementById('dikeSchedulePreview');
    if (!form || !preview) return;
    let timer;
    let request;
    const field = id => document.getElementById(id);
    const show = status => { preview.textContent = preview.dataset[status]; };
    const time = field('dikeSendTime');
    const timeError = field('dikeTimeError');
    const timePicker = field('dikeTimePicker');
    const timeToggle = field('dikeTimeToggle');
    let timeTouched = false;

    function validateTime(touched = false) {
        timeTouched ||= touched;
        const value = time.value;
        let message = '';
        if (!value && time.required) message = time.dataset.requiredError;
        else if (value && !/^(?:[01][0-9]|2[0-3]):[0-5][0-9]$/.test(value))
            message = /^\d{2}:\d{2}$/.test(value) ? time.dataset.rangeError : time.dataset.formatError;
        time.setCustomValidity(message);
        const showError = Boolean(message) && timeTouched;
        time.classList.toggle('is-invalid', showError);
        time.setAttribute('aria-invalid', showError ? 'true' : 'false');
        timeError.textContent = message;
        timeError.hidden = !showError;
        return !message;
    }
    function closeTimePicker(focus = false) {
        timePicker.hidden = true;
        timeToggle.setAttribute('aria-expanded', 'false');
        if (focus) time.focus();
    }
    timeToggle.addEventListener('click', () => {
        if (!timePicker.hidden) { closeTimePicker(true); return; }
        const [hour, minute] = time.value.split(':');
        field('dikeTimeHour').value = /^(?:[01][0-9]|2[0-3])$/.test(hour) ? hour : '09';
        field('dikeTimeMinute').value = /^[0-5][0-9]$/.test(minute) ? minute : '00';
        timePicker.hidden = false;
        timeToggle.setAttribute('aria-expanded', 'true');
        field('dikeTimeHour').focus();
    });
    field('dikeTimeApply').addEventListener('click', () => {
        time.value = `${field('dikeTimeHour').value}:${field('dikeTimeMinute').value}`;
        time.dispatchEvent(new Event('input', { bubbles: true }));
        time.dispatchEvent(new Event('change', { bubbles: true }));
        closeTimePicker(true);
    });
    time.addEventListener('input', () => validateTime(true));
    time.addEventListener('blur', () => validateTime(true));
    time.addEventListener('invalid', () => validateTime(true));
    document.addEventListener('pointerdown', event => {
        if (!field('dikeTimeControl').contains(event.target)) closeTimePicker();
    });
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && !timePicker.hidden) { event.preventDefault(); closeTimePicker(true); }
    });
    form.addEventListener('submit', event => {
        if (validateTime(true)) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        time.focus();
        time.reportValidity();
    }, true);

    async function update() {
        request?.abort();
        if (!field('dikeAutomaticSending').checked) { show('disabled'); return; }
        if (!field('dikeAgreementActive').checked) { show('inactive'); return; }
        if (field('dikeFrequency').value === 'Pagal poreiki') { show('ondemand'); return; }
        const values = {
            firstPeriod: field('dikeFirstPeriod').value,
            frequency: field('dikeFrequency').value,
            monthDays: field('dikeMonthDays').value,
            sendTime: field('dikeSendTime').value,
            validFrom: field('dikeAgreementStart').value
        };
        if (Object.values(values).some(value => !value.trim()) || !field('dikeSendTime').validity.valid) {
            show('incomplete'); return;
        }
        request = new AbortController();
        show('loading');
        try {
            const url = new URL(preview.dataset.url, location.href);
            Object.entries(values).forEach(([key, value]) => url.searchParams.set(key, value));
            const culture = document.documentElement.lang.startsWith('en') ? 'en-GB' : 'lt-LT';
            url.searchParams.set('culture', culture);
            url.searchParams.set('ui-culture', culture);
            const response = await fetch(url, { signal: request.signal, cache: 'no-store' });
            if (!response.ok) throw new Error('Preview unavailable');
            const result = await response.json();
            if (result.status !== 'ready') { show('incomplete'); return; }
            preview.textContent = preview.dataset.ready.replace('{0}', result.period).replace('{1}', result.date)
                + (result.clockAdjusted ? ' ' + preview.dataset.adjusted : '');
            const previous = document.createElement('div');
            previous.className = 'mt-2';
            previous.textContent = result.previousDate
                ? preview.dataset.previous.replace('{0}', result.previousDate)
                : preview.dataset.noPrevious;
            preview.append(previous);
        } catch (error) {
            if (error.name !== 'AbortError') show('failed');
        }
    }
    function scheduleUpdate() { validateTime(); clearTimeout(timer); request?.abort(); timer = setTimeout(update, 200); }
    form.addEventListener('input', scheduleUpdate);
    form.addEventListener('change', scheduleUpdate);
    form.addEventListener('reset', () => setTimeout(() => { timeTouched = false; closeTimePicker(); scheduleUpdate(); }, 0));
    validateTime();
    update();
});
