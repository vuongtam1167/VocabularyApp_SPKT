window.vocabApp = window.vocabApp || {};

window.vocabApp.requestJson = async function requestJson(url, options = {}) {
    const headers = { ...(options.headers || {}) };
    const hasBody = Object.prototype.hasOwnProperty.call(options, 'body');
    const isFormData = typeof FormData !== 'undefined' && options.body instanceof FormData;

    if (hasBody && !isFormData && !headers['Content-Type'] && !headers['content-type']) {
        headers['Content-Type'] = 'application/json';
    }

    const response = await fetch(url, {
        credentials: 'same-origin',
        ...options,
        headers
    });

    let payload = null;
    const contentType = response.headers.get('content-type') || '';
    if (contentType.includes('application/json')) {
        payload = await response.json();
    }

    return { response, payload };
};

window.vocabApp.openModal = function openModal(id) {
    const element = document.getElementById(id);
    if (!element) {
        return;
    }

    element.classList.remove('hidden');
    element.classList.add('flex');
    document.body.classList.add('overflow-hidden');
};

window.vocabApp.closeModal = function closeModal(id) {
    const element = document.getElementById(id);
    if (!element) {
        return;
    }

    element.classList.add('hidden');
    element.classList.remove('flex');
    document.body.classList.remove('overflow-hidden');
};

window.vocabApp.toast = function toast(message, type = 'info') {
    const existing = document.getElementById('vocab-toast');
    if (existing) {
        existing.remove();
    }

    const toast = document.createElement('div');
    toast.id = 'vocab-toast';
    toast.textContent = message;
    toast.className = [
        'fixed',
        'right-4',
        'top-4',
        'z-[9999]',
        'max-w-sm',
        'rounded-xl',
        'px-4',
        'py-3',
        'text-sm',
        'shadow-xl',
        type === 'error' ? 'bg-red-600 text-white' : type === 'success' ? 'bg-emerald-600 text-white' : 'bg-slate-900 text-white'
    ].join(' ');

    document.body.appendChild(toast);
    window.setTimeout(() => toast.classList.add('opacity-0', 'transition-opacity', 'duration-300'), 2400);
    window.setTimeout(() => toast.remove(), 2800);
};

window.vocabApp.safeReload = function safeReload(delay = 400) {
    window.setTimeout(() => window.location.reload(), delay);
};

window.vocabApp.escapeHtml = function escapeHtml(value) {
    const div = document.createElement('div');
    div.innerText = value ?? '';
    return div.innerHTML;
};