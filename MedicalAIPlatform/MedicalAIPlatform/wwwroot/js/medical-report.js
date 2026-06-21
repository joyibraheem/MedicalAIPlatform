(function () {
    function qs(sel, root) {
        return (root || document).querySelector(sel);
    }

    function readAiOriginal() {
        var el = qs('#mr-ai-original');
        if (!el || !el.textContent) return null;
        try {
            return JSON.parse(el.textContent.trim());
        } catch (e) {
            console.warn('[MedicalReport] invalid AI baseline JSON', e);
            return null;
        }
    }

    function reportId() {
        var page = qs('.mr-page');
        return page && page.getAttribute('data-report-id');
    }

    async function putClinical(body) {
        var id = reportId();
        var res = await fetch('/api/medical-reports/' + encodeURIComponent(id) + '/clinical', {
            method: 'PUT',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                Accept: 'application/json'
            },
            body: JSON.stringify(body)
        });
        if (!res.ok) {
            var err = await res.text();
            throw new Error(err || 'Save failed');
        }
        return res.json();
    }

    async function resetAi() {
        var id = reportId();
        var res = await fetch('/api/medical-reports/' + encodeURIComponent(id) + '/reset-ai', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { Accept: 'application/json' }
        });
        if (!res.ok) {
            var err = await res.text();
            throw new Error(err || 'Revert failed');
        }
        return res.json();
    }

    function setEditing(editing) {
        var page = qs('.mr-page');
        if (!page) return;
        page.classList.toggle('mr-editing', editing);

        document.querySelectorAll('.mr-narrative .mr-view').forEach(function (el) {
            el.classList.toggle('mr-edit-hidden', editing);
        });
        document.querySelectorAll('.mr-narrative .mr-edit').forEach(function (el) {
            el.classList.toggle('mr-edit-hidden', !editing);
        });

        var btnEdit = qs('#mr-btn-edit');
        var btnSave = qs('#mr-btn-save');
        var btnCancel = qs('#mr-btn-cancel');
        if (btnEdit) btnEdit.classList.toggle('mr-btn-hidden', editing);
        if (btnSave) btnSave.classList.toggle('mr-btn-hidden', !editing);
        if (btnCancel) btnCancel.classList.toggle('mr-btn-hidden', !editing);
    }

    function applyBaselineTextareas(ai) {
        if (!ai) return;
        var f = qs('#mr-findings');
        var i = qs('#mr-impression');
        var r = qs('#mr-recommendations');
        if (f) f.value = ai.findings || '';
        if (i) i.value = ai.impression || '';
        if (r) r.value = ai.recommendations || '';
    }

    function boot() {
        var page = qs('.mr-page');
        if (!page) return;

        var aiOriginal = readAiOriginal();

        qs('#mr-btn-edit')?.addEventListener('click', function () {
            setEditing(true);
        });

        qs('#mr-btn-cancel')?.addEventListener('click', function () {
            applyBaselineTextareas(aiOriginal);
            setEditing(false);
        });

        qs('#mr-btn-save')?.addEventListener('click', async function () {
            var btn = qs('#mr-btn-save');
            if (!btn) return;
            btn.disabled = true;
            try {
                await putClinical({
                    findings: qs('#mr-findings')?.value ?? '',
                    impression: qs('#mr-impression')?.value ?? '',
                    recommendations: qs('#mr-recommendations')?.value ?? ''
                });
                window.location.reload();
            } catch (e) {
                alert(e && e.message ? e.message : 'Could not save.');
                btn.disabled = false;
            }
        });

        qs('#mr-btn-reset-ai')?.addEventListener('click', async function () {
            if (!window.confirm('Revert findings, impression, and recommendations to the original AI baseline?')) return;
            var btn = qs('#mr-btn-reset-ai');
            if (btn) btn.disabled = true;
            try {
                await resetAi();
                window.location.reload();
            } catch (e) {
                alert(e && e.message ? e.message : 'Could not revert.');
                if (btn) btn.disabled = false;
            }
        });

        qs('#mr-btn-print')?.addEventListener('click', function () {
            var id = reportId();
            if (id) window.open('/MedicalReport/Print/' + encodeURIComponent(id), '_blank', 'noopener,noreferrer');
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();
})();
