document.addEventListener('DOMContentLoaded', () => {
    const selection = document.getElementById('dikeDocumentAgreement');
    selection?.addEventListener('change', () => selection.form.requestSubmit());
    const fileInput = document.getElementById('dikeDocumentFile');
    fileInput?.addEventListener('change', () => {
        const file = fileInput.files[0];
        const message = !file ? '' : !/\.(pdf|eml)$/i.test(file.name) ? fileInput.dataset.typeError
            : file.size < 1 || file.size > 10 * 1024 * 1024 ? fileInput.dataset.sizeError : '';
        fileInput.setCustomValidity(message);
        if (message) fileInput.reportValidity();
    });
    const modal = document.getElementById('dikeDocumentModal');
    const frame = document.getElementById('dikeDocumentFrame');
    document.querySelectorAll('[data-dike-document-preview]').forEach(button => {
        button.addEventListener('click', () => {
            document.getElementById('dikeDocumentModalTitle').textContent = button.dataset.documentName;
            frame.src = button.dataset.dikeDocumentPreview;
            bootstrap.Modal.getOrCreateInstance(modal).show();
        });
    });
    modal?.addEventListener('hidden.bs.modal', () => { frame.src = 'about:blank'; });
    document.querySelectorAll('[data-dike-document-delete]').forEach(form => {
        form.addEventListener('submit', event => {
            if (!confirm(form.dataset.dikeDocumentDelete)) event.preventDefault();
        });
    });
});
