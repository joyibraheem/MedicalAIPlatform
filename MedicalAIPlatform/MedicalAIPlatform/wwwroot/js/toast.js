/**
 * Medical AI Platform — Toast Notification System
 * Replaces browser alert() with professional toasts without changing call sites.
 */
(function () {
    'use strict';

    const ICONS = {
        success: 'bi-check-circle-fill',
        error: 'bi-x-circle-fill',
        warning: 'bi-exclamation-triangle-fill',
        info: 'bi-info-circle-fill'
    };

    const TITLES = {
        success: 'Success',
        error: 'Error',
        warning: 'Warning',
        info: 'Notice'
    };

    let container = null;

    function getContainer() {
        if (!container || !document.body.contains(container)) {
            container = document.createElement('div');
            container.className = 'mai-toast-container';
            container.setAttribute('role', 'region');
            container.setAttribute('aria-label', 'Notifications');
            document.body.appendChild(container);
        }
        return container;
    }

    function inferType(message) {
        const m = String(message || '').toLowerCase();
        if (m.includes('success') || m.includes('saved') || m.includes('complete')) return 'success';
        if (m.includes('warning') || m.includes('please')) return 'warning';
        if (m.includes('error') || m.includes('fail') || m.includes('could not')) return 'error';
        return 'info';
    }

    function showToast(message, type, options) {
        type = type || inferType(message);
        options = options || {};
        const duration = options.duration != null ? options.duration : 5000;
        const title = options.title || TITLES[type] || TITLES.info;

        const toast = document.createElement('div');
        toast.className = 'mai-toast mai-toast-' + type;
        toast.setAttribute('role', 'alert');
        toast.innerHTML =
            '<div class="mai-toast-icon"><i class="bi ' + (ICONS[type] || ICONS.info) + '"></i></div>' +
            '<div class="mai-toast-body">' +
                '<div class="mai-toast-title">' + escapeHtml(title) + '</div>' +
                '<div class="mai-toast-message">' + escapeHtml(String(message)) + '</div>' +
            '</div>' +
            '<button type="button" class="mai-toast-close" aria-label="Dismiss"><i class="bi bi-x"></i></button>';

        const closeBtn = toast.querySelector('.mai-toast-close');
        let timer = null;

        function dismiss() {
            if (timer) clearTimeout(timer);
            toast.classList.add('mai-toast-exit');
            setTimeout(function () { toast.remove(); }, 320);
        }

        closeBtn.addEventListener('click', dismiss);
        getContainer().appendChild(toast);

        if (duration > 0) {
            timer = setTimeout(dismiss, duration);
        }

        return { dismiss: dismiss };
    }

    function escapeHtml(str) {
        return str
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    /** Global loading overlay */
    let loadingOverlay = null;

    function showLoading(message) {
        if (!loadingOverlay) {
            loadingOverlay = document.createElement('div');
            loadingOverlay.className = 'mai-loading-overlay';
            loadingOverlay.innerHTML =
                '<div class="mai-loading-card">' +
                    '<div class="mai-loading-spinner"></div>' +
                    '<p class="mai-loading-text">Loading…</p>' +
                '</div>';
            document.body.appendChild(loadingOverlay);
        }
        const textEl = loadingOverlay.querySelector('.mai-loading-text');
        if (textEl) textEl.textContent = message || 'Loading…';
        loadingOverlay.classList.add('active');
    }

    function hideLoading() {
        if (loadingOverlay) loadingOverlay.classList.remove('active');
    }

    /** Button loading state helper */
    function setButtonLoading(btn, loading) {
        if (!btn) return;
        if (loading) {
            btn.classList.add('loading');
            btn.style.position = 'relative';
            if (!btn.querySelector('.mai-btn-spinner')) {
                const spinner = document.createElement('span');
                spinner.className = 'mai-btn-spinner';
                btn.appendChild(spinner);
            }
            btn.dataset.maiOriginalDisabled = btn.disabled;
            btn.disabled = true;
        } else {
            btn.classList.remove('loading');
            const spinner = btn.querySelector('.mai-btn-spinner');
            if (spinner) spinner.remove();
            btn.disabled = btn.dataset.maiOriginalDisabled === 'true';
        }
    }

    window.MedicalToast = {
        show: showToast,
        success: function (msg, opts) { return showToast(msg, 'success', opts); },
        error: function (msg, opts) { return showToast(msg, 'error', opts); },
        warning: function (msg, opts) { return showToast(msg, 'warning', opts); },
        info: function (msg, opts) { return showToast(msg, 'info', opts); },
        showLoading: showLoading,
        hideLoading: hideLoading,
        setButtonLoading: setButtonLoading
    };

    /** Override native alert — preserves functionality, improves UX */
    const nativeAlert = window.alert.bind(window);
    window.alert = function (message) {
        if (typeof message === 'undefined' || message === null) return;
        showToast(String(message), inferType(message));
    };
    window.alert._native = nativeAlert;

    document.addEventListener('DOMContentLoaded', function () {
        /** Active nav link highlighting for _Layout navbar */
        const path = window.location.pathname.toLowerCase();
        document.querySelectorAll('.mai-navbar .nav-link[href]').forEach(function (link) {
            const href = (link.getAttribute('href') || '').toLowerCase();
            if (href && href !== '/' && path.startsWith(href)) {
                link.classList.add('active');
                link.setAttribute('aria-current', 'page');
            } else if (href === '/' && (path === '/' || path === '/home' || path === '/home/index')) {
                link.classList.add('active');
                link.setAttribute('aria-current', 'page');
            }
        });
    });
})();
