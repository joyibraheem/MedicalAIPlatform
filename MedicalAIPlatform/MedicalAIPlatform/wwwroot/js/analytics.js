// Analytics JavaScript

let xrayFile = null;
let ctFile = null;
let clinicalText = '';

/** Keep in-memory File refs aligned with native file inputs (e.g. after refresh/back navigation). */
function syncFileStateFromInputs() {
    const xrayInput = document.getElementById('xrayFileInput');
    const ctInput = document.getElementById('ctFileInput');
    const clinicalEl = document.getElementById('clinicalText');

    xrayFile = xrayInput?.files?.[0] ?? null;
    ctFile = ctInput?.files?.[0] ?? null;
    clinicalText = clinicalEl ? clinicalEl.value : '';
}

function updateActionButtons() {
    syncFileStateFromInputs();
    const hasXray = !!xrayFile;
    const hasText = !!clinicalText.trim();
    const hasCt = !!ctFile;
    const hasAny = hasXray || hasText || hasCt;

    const btnXRay = document.getElementById('btnXRay');
    const btnText = document.getElementById('btnText');
    const btnCT = document.getElementById('btnCT');
    const btnCombined = document.getElementById('btnCombined');

    if (btnXRay) btnXRay.disabled = !hasXray;
    if (btnText) btnText.disabled = !hasText;
    if (btnCT) btnCT.disabled = !hasCt;
    if (btnCombined) btnCombined.disabled = !hasAny;

    updateDownloadAllButton();
}
document.getElementById('xrayFileInput')?.addEventListener('change', function(e) {
    const file = e.target.files[0];
    if (file) {
        xrayFile = file;
        if (window.RadiologyFileInputHelpers) {
            RadiologyFileInputHelpers.applyPreview(file, {
                nameDisplay: document.getElementById('xrayFileName'),
                previewWrap: document.getElementById('xrayPreview'),
                imgEl: document.getElementById('xrayPreviewImg'),
                badgeEl: document.getElementById('xrayPreviewDicom')
            });
        } else {
            document.getElementById('xrayFileName').textContent = file.name;
            const reader = new FileReader();
            reader.onload = function(ev) {
                document.getElementById('xrayPreviewImg').src = ev.target.result;
                document.getElementById('xrayPreview').style.display = 'block';
            };
            reader.readAsDataURL(file);
            document.getElementById('xrayPreview').style.display = 'block';
        }
        document.getElementById('btnDownloadXRay').style.display = 'inline-flex';
        updateActionButtons();
    }
});

// CT file handling
document.getElementById('ctFileInput')?.addEventListener('change', function(e) {
    const file = e.target.files[0];
    if (file) {
        ctFile = file;
        if (window.RadiologyFileInputHelpers) {
            RadiologyFileInputHelpers.applyPreview(file, {
                nameDisplay: document.getElementById('ctFileName'),
                previewWrap: document.getElementById('ctPreview'),
                imgEl: document.getElementById('ctPreviewImg'),
                badgeEl: document.getElementById('ctPreviewDicom')
            });
        } else {
            document.getElementById('ctFileName').textContent = file.name;
            const reader = new FileReader();
            reader.onload = function(ev) {
                document.getElementById('ctPreviewImg').src = ev.target.result;
                document.getElementById('ctPreview').style.display = 'block';
            };
            reader.readAsDataURL(file);
            document.getElementById('ctPreview').style.display = 'block';
        }
        document.getElementById('btnDownloadCT').style.display = 'inline-flex';
        updateActionButtons();
    }
});

// Clinical text handling
document.getElementById('clinicalText')?.addEventListener('input', function(e) {
    clinicalText = e.target.value;
    if (clinicalText.trim()) {
        document.getElementById('btnDownloadText').style.display = 'inline-flex';
    } else {
        document.getElementById('btnDownloadText').style.display = 'none';
    }
    updateActionButtons();
});

function clearXRay() {
    xrayFile = null;
    if (window.RadiologyFileInputHelpers) {
        RadiologyFileInputHelpers.resetPreview({
            input: document.getElementById('xrayFileInput'),
            previewWrap: document.getElementById('xrayPreview'),
            imgEl: document.getElementById('xrayPreviewImg'),
            badgeEl: document.getElementById('xrayPreviewDicom')
        });
    } else {
        document.getElementById('xrayFileInput').value = '';
        document.getElementById('xrayPreview').style.display = 'none';
    }
    if (typeof window.setLanguage === 'function') {
        window.setLanguage(localStorage.getItem('lang') === 'ar' ? 'ar' : 'en');
    } else {
        const el = document.getElementById('xrayFileName');
        if (el) el.textContent = 'Upload Chest X-Ray';
    }
    document.getElementById('btnDownloadXRay').style.display = 'none';
    updateActionButtons();
}

function clearCT() {
    ctFile = null;
    if (window.RadiologyFileInputHelpers) {
        RadiologyFileInputHelpers.resetPreview({
            input: document.getElementById('ctFileInput'),
            previewWrap: document.getElementById('ctPreview'),
            imgEl: document.getElementById('ctPreviewImg'),
            badgeEl: document.getElementById('ctPreviewDicom')
        });
    } else {
        document.getElementById('ctFileInput').value = '';
        document.getElementById('ctPreview').style.display = 'none';
    }
    if (typeof window.setLanguage === 'function') {
        window.setLanguage(localStorage.getItem('lang') === 'ar' ? 'ar' : 'en');
    } else {
        const el = document.getElementById('ctFileName');
        if (el) el.textContent = 'Upload CT Scan';
    }
    document.getElementById('btnDownloadCT').style.display = 'none';
    updateActionButtons();
}

function updateDownloadAllButton() {
    syncFileStateFromInputs();
    const hasAny = xrayFile || clinicalText.trim() || ctFile;
    const btn = document.getElementById('btnDownloadAll');
    if (btn) btn.style.display = hasAny ? 'inline-flex' : 'none';
}

function ctAnalyzeHint() {
    syncFileStateFromInputs();
    if (xrayFile && clinicalText.trim()) {
        return 'No CT scan uploaded. Use Combined Results for X-ray + clinical text, or upload a CT file first.';
    }
    if (xrayFile) {
        return 'No CT scan uploaded. Click X-Ray to analyze your chest image, or upload a CT file first.';
    }
    if (clinicalText.trim()) {
        return 'No CT scan uploaded. Click Text for BioBERT analysis, or upload a CT file first.';
    }
    return 'Upload a CT scan file in the CT section above, then click CT.';
}

/**
 * Parses JSON analytics POST responses and surfaces HTTP errors without calling response.json() on HTML/error pages.
 * @returns {{ success: boolean, redirect?: string, error?: string }}
 */
async function readAnalyticsPostResult(response) {
    const text = await response.text();
    let parsed = null;
    if (text) {
        try {
            parsed = JSON.parse(text);
        } catch {
            /* login page HTML, 413 body, etc. */
        }
    }
    const bodyError =
        parsed && typeof parsed === 'object' && typeof parsed.error === 'string'
            ? parsed.error
            : null;

    if (response.status === 401 || response.status === 403) {
        return {
            success: false,
            error:
                bodyError ||
                'Not signed in or session expired. Refresh the page and log in again.'
        };
    }
    if (response.status === 413) {
        return {
            success: false,
            error:
                bodyError ||
                'Upload is too large for the server. Try a smaller file or contact an administrator.'
        };
    }
    if (parsed && typeof parsed === 'object' && Object.prototype.hasOwnProperty.call(parsed, 'success')) {
        return parsed;
    }
    if (!response.ok) {
        const hint = text && text.trim() ? text.trim().slice(0, 240) : response.statusText;
        return {
            success: false,
            error:
                bodyError ||
                ('Request failed (' + response.status + '). ' + hint)
        };
    }
    return { success: false, error: 'Unexpected response from server.' };
}

async function analyzeXRay() {
    syncFileStateFromInputs();
    if (!xrayFile) {
        showError('Please upload an X-Ray image in the Radiology Scan section above.');
        return;
    }

    setLoading('btnXRay', true);
    const formData = new FormData();
    formData.append('xrayFile', xrayFile);
    const fileName = xrayFile.name || 'Chest X-ray';

    try {
        const response = await fetch('/api/job/analyze-xray', {
            method: 'POST',
            body: formData,
            credentials: 'same-origin'
        });

        const text = await response.text();
        let data = null;
        if (text) {
            try {
                data = JSON.parse(text);
            } catch {
                /* non-JSON error body */
            }
        }

        if (response.status === 401 || response.status === 403) {
            showError(
                (data && data.error) ||
                    'Not signed in or session expired. Refresh and log in again.'
            );
            return;
        }

        if (response.status === 413) {
            showError(
                (data && data.error) ||
                    'Upload is too large for the server. Try a smaller file.'
            );
            return;
        }

        if (!response.ok) {
            showError(
                (data && data.error) ||
                    'Could not start X-ray analysis (' + response.status + ').'
            );
            return;
        }

        if (
            data &&
            data.jobId &&
            window.MedicalAiAssistantChat &&
            typeof window.MedicalAiAssistantChat.startXRayJobPolling === 'function'
        ) {
            window.MedicalAiAssistantChat.startXRayJobPolling(data.jobId, fileName);
        } else if (data && data.jobId && window.MedicalAiAssistantChat) {
            window.MedicalAiAssistantChat.startCtJobPolling(data.jobId, fileName);
        } else if (data && data.jobId) {
            showError('Assistant UI failed to load. Job id: ' + data.jobId);
        } else {
            showError('Unexpected response from analysis server.');
        }
    } catch (error) {
        showError('Network error: ' + error.message);
    } finally {
        setLoading('btnXRay', false);
    }
}

async function analyzeText() {
    const text = document.getElementById('clinicalText').value.trim();
    if (!text) {
        showError('Please enter clinical text.');
        return;
    }

    setLoading('btnText', true);
    const formData = new FormData();
    formData.append('clinicalText', text);

    try {
        const response = await fetch('/Analytics/AnalyzeText', {
            method: 'POST',
            body: formData,
            credentials: 'same-origin'
        });

        const result = await readAnalyticsPostResult(response);
        if (result.success) {
            window.location.href = result.redirect;
        } else {
            showError(result.error || 'Analysis failed.');
        }
    } catch (error) {
        showError('Network error: ' + error.message);
    } finally {
        setLoading('btnText', false);
    }
}

async function analyzeCT() {
    syncFileStateFromInputs();
    if (!ctFile) {
        showError(ctAnalyzeHint());
        return;
    }

    setLoading('btnCT', true);
    const formData = new FormData();
    formData.append('ctFile', ctFile);
    const fileName = ctFile.name || 'CT scan';

    try {
        const response = await fetch('/api/job/analyze-ct', {
            method: 'POST',
            body: formData,
            credentials: 'same-origin'
        });

        const text = await response.text();
        let data = null;
        if (text) {
            try {
                data = JSON.parse(text);
            } catch {
                /* non-JSON error body */
            }
        }

        if (response.status === 401 || response.status === 403) {
            showError(
                (data && data.error) ||
                    'Not signed in or session expired. Refresh and log in again.'
            );
            return;
        }

        if (response.status === 413) {
            showError(
                (data && data.error) ||
                    'Upload is too large for the server. Try a smaller file.'
            );
            return;
        }

        if (!response.ok) {
            showError(
                (data && data.error) ||
                    'Could not start CT analysis (' + response.status + ').'
            );
            return;
        }

        if (data && data.jobId && window.MedicalAiAssistantChat && typeof window.MedicalAiAssistantChat.startCtJobPolling === 'function') {
            window.MedicalAiAssistantChat.startCtJobPolling(data.jobId, fileName);
        } else if (data && data.jobId) {
            showError('Assistant UI failed to load. Job id: ' + data.jobId);
        } else {
            showError('Unexpected response from analysis server.');
        }
    } catch (error) {
        showError('Network error: ' + error.message);
    } finally {
        setLoading('btnCT', false);
    }
}

async function analyzeCombined() {
    syncFileStateFromInputs();
    const clinicalEl = document.getElementById('clinicalText');
    const clinicalTrim = clinicalEl ? clinicalEl.value.trim() : '';
    if (clinicalEl) clinicalText = clinicalEl.value;

    if (!xrayFile && !clinicalTrim && !ctFile) {
        showError('Please provide at least one input (X-Ray, Text, or CT).');
        return;
    }

    setLoading('btnCombined', true);
    const formData = new FormData();
    if (xrayFile) formData.append('xrayFile', xrayFile);
    if (clinicalTrim) formData.append('clinicalText', clinicalTrim);
    if (ctFile) formData.append('ctFile', ctFile);

    try {
        const response = await fetch('/Analytics/AnalyzeCombined', {
            method: 'POST',
            body: formData,
            credentials: 'same-origin'
        });

        const result = await readAnalyticsPostResult(response);
        if (result && result.success === true && result.redirect) {
            window.location.href = result.redirect;
        } else {
            showError((result && result.error) ? result.error : 'Analysis failed.');
        }
    } catch (error) {
        showError('Network error: ' + error.message);
    } finally {
        setLoading('btnCombined', false);
    }
}

function setLoading(buttonId, isLoading) {
    const btn = document.getElementById(buttonId);
    if (!btn) return;
    
    if (isLoading) {
        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Analyzing...';
    } else {
        btn.disabled = false;
        const originalText = {
            'btnXRay': 'X-Ray',
            'btnText': 'Text',
            'btnCT': 'CT',
            'btnCombined': 'Combined Results'
        };
        btn.innerHTML = originalText[buttonId] || 'Submit';
    }
}

function showError(message) {
    const alert = document.getElementById('errorAlert');
    const messageSpan = document.getElementById('errorMessage');
    if (alert && messageSpan) {
        messageSpan.textContent = message;
        alert.style.display = 'block';
        setTimeout(() => {
            alert.style.display = 'none';
        }, 5000);
    } else if (window.MedicalToast) {
        window.MedicalToast.error(message);
    } else {
        alert(message);
    }
}

function downloadXRay() {
    if (xrayFile) {
        const url = URL.createObjectURL(xrayFile);
        const a = document.createElement('a');
        a.href = url;
        a.download = xrayFile.name;
        a.click();
        URL.revokeObjectURL(url);
    }
}

function downloadText() {
    const text = document.getElementById('clinicalText').value;
    if (text) {
        const blob = new Blob([text], { type: 'text/plain' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'clinical_notes.txt';
        a.click();
        URL.revokeObjectURL(url);
    }
}

function downloadCT() {
    if (ctFile) {
        const url = URL.createObjectURL(ctFile);
        const a = document.createElement('a');
        a.href = url;
        a.download = ctFile.name;
        a.click();
        URL.revokeObjectURL(url);
    }
}

function downloadAll() {
    if (xrayFile) downloadXRay();
    setTimeout(() => {
        if (clinicalText.trim()) downloadText();
    }, 300);
    setTimeout(() => {
        if (ctFile) downloadCT();
    }, 600);
}

document.addEventListener('DOMContentLoaded', updateActionButtons);
if (document.readyState !== 'loading') {
    updateActionButtons();
}
