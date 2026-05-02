/**
 * Shared radiology upload helpers: accept list + preview behavior.
 * Browsers often report .dcm as application/dicom or octet-stream — extension-based accept is required for reliable pickers.
 */
(function (global) {
    'use strict';

    /** Use in HTML accept="..." — explicit types + RFC DICOM + extensions (not image/*). */
    const AcceptRadiologyBitmapAndDicom =
        'image/png,image/jpeg,application/dicom,.dcm,.dicm';

    function supportsBitmapPreview(file) {
        if (!file) return false;
        var type = (file.type || '').toLowerCase();
        if (type === 'image/png' || type === 'image/jpeg' || type === 'image/pjpeg') return true;
        var name = (file.name || '').toLowerCase();
        return name.endsWith('.png') || name.endsWith('.jpg') || name.endsWith('.jpeg');
    }

    function isLikelyDicom(file) {
        if (!file) return false;
        var name = (file.name || '').toLowerCase();
        if (name.endsWith('.dcm') || name.endsWith('.dicm')) return true;
        var type = (file.type || '').toLowerCase();
        return type === 'application/dicom' || type === 'application/x-dicom';
    }

    /**
     * @param {File} file
     * @param {{ nameDisplay: HTMLElement|null, previewWrap: HTMLElement, imgEl: HTMLElement, badgeEl: HTMLElement|null }} cfg
     */
    function applyPreview(file, cfg) {
        if (!file || !cfg.previewWrap || !cfg.imgEl) return;
        if (cfg.nameDisplay) cfg.nameDisplay.textContent = file.name;
        cfg.previewWrap.style.display = 'block';

        if (supportsBitmapPreview(file)) {
            if (cfg.badgeEl) cfg.badgeEl.style.display = 'none';
            cfg.imgEl.style.display = 'block';
            var reader = new FileReader();
            reader.onload = function (ev) {
                cfg.imgEl.src = ev.target.result;
            };
            reader.readAsDataURL(file);
        } else {
            cfg.imgEl.removeAttribute('src');
            cfg.imgEl.style.display = 'none';
            if (cfg.badgeEl) cfg.badgeEl.style.display = 'flex';
        }
    }

    /**
     * @param {{ input: HTMLInputElement, previewWrap: HTMLElement, imgEl: HTMLElement, badgeEl: HTMLElement|null, nameDisplay?: HTMLElement|null, defaultNameText?: string|null }} cfg
     */
    function resetPreview(cfg) {
        if (!cfg.input || !cfg.previewWrap || !cfg.imgEl) return;
        cfg.input.value = '';
        cfg.previewWrap.style.display = 'none';
        cfg.imgEl.removeAttribute('src');
        cfg.imgEl.style.display = 'block';
        if (cfg.badgeEl) cfg.badgeEl.style.display = 'none';
        if (cfg.nameDisplay != null && cfg.defaultNameText != null) cfg.nameDisplay.textContent = cfg.defaultNameText;
    }

    global.RadiologyFileInputHelpers = {
        acceptRadiologyBitmapAndDicom: AcceptRadiologyBitmapAndDicom,
        supportsBitmapPreview: supportsBitmapPreview,
        isLikelyDicom: isLikelyDicom,
        applyPreview: applyPreview,
        resetPreview: resetPreview
    };
})(typeof window !== 'undefined' ? window : globalThis);
