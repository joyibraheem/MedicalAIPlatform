/**
 * Human-in-the-loop: submit prediction feedback (Accept / Modify).
 */
(function () {
    'use strict';

    function submitUrl() {
        return typeof window.__maiFeedbackSubmitUrl === 'string' && window.__maiFeedbackSubmitUrl.length > 0
            ? window.__maiFeedbackSubmitUrl
            : '/api/feedback/submit';
    }

    function readPrediction(jsonId) {
        var el = document.getElementById(jsonId);
        if (!el || !el.textContent) return null;
        try {
            return JSON.parse(el.textContent.trim());
        } catch (e) {
            console.warn('[ClinicalFeedback] invalid prediction JSON', e);
            return null;
        }
    }

    function setStatus(panel, text, kind) {
        var s = panel.querySelector('.clinical-fb-status');
        if (!s) return;
        s.textContent = text || '';
        s.classList.remove('feedback-ok', 'feedback-err');
        if (kind === 'ok') s.classList.add('feedback-ok');
        if (kind === 'err') s.classList.add('feedback-err');
    }

    function ensureModal() {
        var existing = document.getElementById('clinicalFbModifyModal');
        if (existing) return existing;

        var wrap = document.createElement('div');
        wrap.innerHTML =
            '<div class="modal fade" id="clinicalFbModifyModal" tabindex="-1" aria-hidden="true">' +
            '<div class="modal-dialog modal-dialog-centered">' +
            '<div class="modal-content">' +
            '<div class="modal-header">' +
            '<h5 class="modal-title">Correct prediction</h5>' +
            '<button type="button" class="btn-close" data-bs-dismiss="modal"></button>' +
            '</div>' +
            '<div class="modal-body">' +
            '<input type="hidden" id="clinicalFbModalPanelCorr" value="" />' +
            '<div class="mb-3">' +
            '<label class="form-label" for="clinicalFbCorrectedLabel">Primary label / diagnosis</label>' +
            '<select class="form-select form-select-sm" id="clinicalFbCorrectedLabel"></select>' +
            '<input type="text" class="form-control form-control-sm mt-2" id="clinicalFbCorrectedLabelCustom" placeholder="Or enter custom label" />' +
            '</div>' +
            '<div class="mb-3">' +
            '<label class="form-label" for="clinicalFbConfidence">Confidence (0–1)</label>' +
            '<input type="number" class="form-control form-control-sm" id="clinicalFbConfidence" min="0" max="1" step="0.05" placeholder="optional" />' +
            '</div>' +
            '<div class="mb-3">' +
            '<label class="form-label" for="clinicalFbNotes">Clinical notes</label>' +
            '<textarea class="form-control form-control-sm" id="clinicalFbNotes" rows="3" placeholder="Optional rationale"></textarea>' +
            '</div>' +
            '</div>' +
            '<div class="modal-footer">' +
            '<button type="button" class="btn btn-secondary btn-sm" data-bs-dismiss="modal">Cancel</button>' +
            '<button type="button" class="btn btn-primary btn-sm" id="clinicalFbModalSubmit">Submit correction</button>' +
            '</div>' +
            '</div></div></div>';
        document.body.appendChild(wrap.firstElementChild);
        return document.getElementById('clinicalFbModifyModal');
    }

    function openModifyModal(panel) {
        var modalEl = ensureModal();
        var corr = panel.getAttribute('data-fbcorr');
        document.getElementById('clinicalFbModalPanelCorr').value = corr;

        var classNames =
            window.__clinicalFeedbackClassLists && corr ? window.__clinicalFeedbackClassLists[corr] || [] : [];

        var sel = document.getElementById('clinicalFbCorrectedLabel');
        sel.innerHTML = '<option value="">— Select —</option>';
        classNames.forEach(function (n) {
            var o = document.createElement('option');
            o.value = n;
            o.textContent = n;
            sel.appendChild(o);
        });

        document.getElementById('clinicalFbCorrectedLabelCustom').value = '';
        document.getElementById('clinicalFbConfidence').value = '';
        document.getElementById('clinicalFbNotes').value = '';

        var modal = window.bootstrap ? new window.bootstrap.Modal(modalEl) : null;
        if (modal) modal.show();
    }

    function panelByCorrelation(corr) {
        return document.querySelector('.clinical-feedback-panel[data-fbcorr="' + corr + '"]');
    }

    function submitPayload(panel, body) {
        setStatus(panel, 'Saving…', '');
        return fetch(submitUrl(), {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
            body: JSON.stringify(body)
        })
            .then(function (r) {
                return r.json().then(function (j) {
                    return { ok: r.ok, status: r.status, json: j };
                });
            })
            .catch(function () {
                return { ok: false, status: 0, json: { error: 'Network error' } };
            });
    }

    function wirePanel(panel) {
        var jsonId = panel.getAttribute('data-json-id');
        var modality = panel.getAttribute('data-modality') || '';
        var modelKey = panel.getAttribute('data-model') || '';
        var corr = panel.getAttribute('data-fbcorr');
        var relJob = panel.getAttribute('data-related-job');
        var study = panel.getAttribute('data-study');
        var series = panel.getAttribute('data-series');

        var pred = readPrediction(jsonId);
        if (!pred) {
            setStatus(panel, 'Could not read prediction snapshot.', 'err');
            return;
        }

        panel.querySelector('.clinical-fb-accept').addEventListener('click', function () {
            var body = {
                clientSessionCorrelationId: corr,
                relatedJobId: relJob || null,
                studyInstanceUid: study || null,
                seriesInstanceUid: series || null,
                modality: modality,
                modelKey: modelKey,
                originalPredictionJson: JSON.stringify(pred),
                doctorAction: 'Accept',
                clinicalNotes: null
            };
            submitPayload(panel, body).then(function (res) {
                if (res.ok) setStatus(panel, 'Recorded — pending review.', 'ok');
                else setStatus(panel, res.json.error || 'Save failed', 'err');
            });
        });

        panel.querySelector('.clinical-fb-modify').addEventListener('click', function () {
            openModifyModal(panel);
        });

        if (!window.__clinicalFbModalWired) {
            window.__clinicalFbModalWired = true;
            document.addEventListener('click', function (ev) {
                if (!ev.target || ev.target.id !== 'clinicalFbModalSubmit') return;
                var corrHidden = document.getElementById('clinicalFbModalPanelCorr');
                var p = corrHidden ? panelByCorrelation(corrHidden.value) : null;
                if (!p) return;

                var modalityP = p.getAttribute('data-modality') || '';
                var modelKeyP = p.getAttribute('data-model') || '';
                var jsonIdP = p.getAttribute('data-json-id');
                var predP = readPrediction(jsonIdP);
                var relJobP = p.getAttribute('data-related-job');
                var studyP = p.getAttribute('data-study');
                var seriesP = p.getAttribute('data-series');

                var selVal = document.getElementById('clinicalFbCorrectedLabel').value.trim();
                var custom = document.getElementById('clinicalFbCorrectedLabelCustom').value.trim();
                var label = custom || selVal;
                var confRaw = document.getElementById('clinicalFbConfidence').value.trim();
                var notes = document.getElementById('clinicalFbNotes').value.trim();

                var conf = confRaw === '' ? null : Number(confRaw);
                if (conf !== null && (isNaN(conf) || conf < 0 || conf > 1)) {
                    alert('Confidence must be between 0 and 1.');
                    return;
                }

                if (!label && !notes) {
                    alert('Enter a corrected label or clinical notes.');
                    return;
                }

                var body = {
                    clientSessionCorrelationId: corrHidden.value,
                    relatedJobId: relJobP || null,
                    studyInstanceUid: studyP || null,
                    seriesInstanceUid: seriesP || null,
                    modality: modalityP,
                    modelKey: modelKeyP,
                    originalPredictionJson: JSON.stringify(predP),
                    doctorAction: 'Modify',
                    correctedPrimaryLabel: label || null,
                    correctedPrimaryConfidence: conf,
                    clinicalNotes: notes || null
                };

                submitPayload(p, body).then(function (res) {
                    var modalEl = document.getElementById('clinicalFbModifyModal');
                    if (res.ok) {
                        setStatus(p, 'Correction recorded — pending review.', 'ok');
                        if (modalEl && window.bootstrap) {
                            var m = window.bootstrap.Modal.getInstance(modalEl);
                            if (m) m.hide();
                        }
                    } else {
                        alert(res.json.error || 'Save failed');
                    }
                });
            });
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        window.__clinicalFeedbackClassLists = window.__clinicalFeedbackClassLists || {};
        document.querySelectorAll('.clinical-feedback-panel').forEach(wirePanel);
    });
})();
