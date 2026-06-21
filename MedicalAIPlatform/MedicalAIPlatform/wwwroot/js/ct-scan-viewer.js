(function () {
    'use strict';

    const root = document.getElementById('ctViewerRoot');
    if (!root) return;

    const cfg = {
        sessionId: root.dataset.sessionId || '',
        hasSession: root.dataset.hasSession === 'true',
        uploadUrl: root.dataset.uploadUrl,
        sessionUrl: root.dataset.sessionUrl,
        sliceUrlBase: root.dataset.sliceUrlBase,
        analyzeUrl: root.dataset.analyzeUrl,
        reportUrl: root.dataset.reportUrl
    };

    const state = {
        sessionId: cfg.sessionId,
        slices: [],
        metadata: null,
        currentIndex: 0,
        zoom: 1,
        panX: 0,
        panY: 0,
        panMode: false,
        isDragging: false,
        dragStart: { x: 0, y: 0 },
        playTimer: null,
        isPlaying: false,
        analysis: null
    };

    const els = {
        emptyState: document.getElementById('emptyState'),
        viewportWrap: document.getElementById('viewportWrap'),
        viewport: document.getElementById('viewport'),
        mainImage: document.getElementById('mainSliceImage'),
        viewportLoading: document.getElementById('viewportLoading'),
        thumbsList: document.getElementById('thumbsList'),
        thumbCounter: document.getElementById('thumbCounter'),
        currentSliceLabel: document.getElementById('currentSliceLabel'),
        zoomLabel: document.getElementById('zoomLabel'),
        studyPatient: document.getElementById('studyPatient'),
        studyModality: document.getElementById('studyModality'),
        studySliceCount: document.getElementById('studySliceCount'),
        aiPlaceholder: document.getElementById('aiPlaceholder'),
        aiResults: document.getElementById('aiResults'),
        aiAnalyzing: document.getElementById('aiAnalyzing'),
        aiPredicted: document.getElementById('aiPredicted'),
        aiConfidence: document.getElementById('aiConfidence'),
        aiConfidenceBar: document.getElementById('aiConfidenceBar'),
        aiRisk: document.getElementById('aiRisk'),
        aiModel: document.getElementById('aiModel'),
        aiProbList: document.getElementById('aiProbList'),
        seriesDrawer: document.getElementById('seriesDrawer'),
        seriesMetaList: document.getElementById('seriesMetaList'),
        toast: document.getElementById('ctToast'),
        btnPrev: document.getElementById('btnPrev'),
        btnNext: document.getElementById('btnNext'),
        btnPlay: document.getElementById('btnPlay'),
        btnZoomIn: document.getElementById('btnZoomIn'),
        btnZoomOut: document.getElementById('btnZoomOut'),
        btnPanMode: document.getElementById('btnPanMode'),
        btnAnalyzeScan: document.getElementById('btnAnalyzeScan'),
        btnDownloadReport: document.getElementById('btnDownloadReport'),
        btnResetViewer: document.getElementById('btnResetViewer'),
        btnSeries: document.getElementById('btnSeries'),
        btnCloseSeries: document.getElementById('btnCloseSeries'),
        btnBack: document.getElementById('btnBack'),
        uploadInputs: [document.getElementById('ctUploadInput'), document.getElementById('ctUploadInputHero')]
    };

    function antiforgeryToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    }

    function sliceUrl(index) {
        return cfg.sliceUrlBase +
            '?sessionId=' + encodeURIComponent(state.sessionId) +
            '&index=' + index;
    }

    function showToast(message, isSuccess) {
        if (!els.toast) return;
        els.toast.textContent = message;
        els.toast.classList.toggle('success', !!isSuccess);
        els.toast.classList.remove('d-none');
        clearTimeout(showToast._timer);
        showToast._timer = setTimeout(function () {
            els.toast.classList.add('d-none');
        }, 5000);
    }

    function setViewerEnabled(enabled) {
        [
            els.btnPrev, els.btnNext, els.btnPlay, els.btnZoomIn, els.btnZoomOut,
            els.btnPanMode, els.btnAnalyzeScan, els.btnDownloadReport, els.btnResetViewer
        ].forEach(function (btn) {
            if (btn) btn.disabled = !enabled;
        });
    }

    function applyTransform() {
        if (!els.mainImage) return;
        els.mainImage.style.transform =
            'translate(calc(-50% + ' + state.panX + 'px), calc(-50% + ' + state.panY + 'px)) scale(' + state.zoom + ')';
        if (els.zoomLabel) els.zoomLabel.textContent = Math.round(state.zoom * 100) + '%';
    }

    function resetViewport() {
        state.zoom = 1;
        state.panX = 0;
        state.panY = 0;
        state.panMode = false;
        if (els.btnPanMode) els.btnPanMode.classList.remove('active');
        if (els.viewport) els.viewport.classList.remove('pan-mode', 'dragging');
        applyTransform();
    }

    function stopPlay() {
        state.isPlaying = false;
        if (state.playTimer) {
            clearInterval(state.playTimer);
            state.playTimer = null;
        }
        if (els.btnPlay) {
            els.btnPlay.innerHTML = '<i class="bi bi-play-fill"></i><span>Play</span>';
        }
    }

    function updateNavButtons() {
        const last = Math.max(0, state.slices.length - 1);
        if (els.btnPrev) els.btnPrev.disabled = state.currentIndex <= 0;
        if (els.btnNext) els.btnNext.disabled = state.currentIndex >= last;
        if (els.thumbCounter) {
            els.thumbCounter.textContent = state.slices.length
                ? (state.currentIndex + 1) + ' / ' + state.slices.length
                : '0 / 0';
        }
    }

    function highlightThumb(index) {
        document.querySelectorAll('.ct-thumb').forEach(function (el) {
            el.classList.toggle('active', parseInt(el.dataset.index, 10) === index);
        });
        const active = document.querySelector('.ct-thumb[data-index="' + index + '"]');
        if (active) active.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }

    function loadMainSlice(index) {
        if (!state.sessionId || index < 0 || index >= state.slices.length) return;

        state.currentIndex = index;
        const slice = state.slices[index];
        if (els.currentSliceLabel) els.currentSliceLabel.textContent = slice.label || ('Slice ' + (index + 1));
        if (els.viewportLoading) els.viewportLoading.classList.remove('d-none');
        if (els.mainImage) {
            els.mainImage.onload = function () {
                if (els.viewportLoading) els.viewportLoading.classList.add('d-none');
            };
            els.mainImage.src = sliceUrl(index) + '&_=' + Date.now();
        }
        highlightThumb(index);
        updateNavButtons();
    }

    function buildThumbnails() {
        if (!els.thumbsList) return;
        els.thumbsList.innerHTML = '';

        state.slices.forEach(function (slice, index) {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'ct-thumb';
            btn.dataset.index = String(index);
            btn.innerHTML =
                '<img src="' + sliceUrl(index) + '" alt="' + (slice.label || '') + '">' +
                '<span>' + (slice.label || ('Slice ' + (index + 1))) + '</span>';
            btn.addEventListener('click', function () {
                stopPlay();
                loadMainSlice(index);
            });
            els.thumbsList.appendChild(btn);
        });
    }

    function updateStudyMeta() {
        const meta = state.metadata || {};
        if (els.studyPatient) {
            els.studyPatient.textContent = meta.patientName
                ? meta.patientName + (meta.patientId ? ' · ' + meta.patientId : '')
                : 'Unknown patient';
        }
        if (els.studyModality) {
            els.studyModality.textContent = [meta.modality, meta.bodyPartExamined].filter(Boolean).join(' · ') || 'CT';
        }
        if (els.studySliceCount) {
            els.studySliceCount.textContent = state.slices.length + ' slice' + (state.slices.length === 1 ? '' : 's');
        }
    }

    function renderSeriesDrawer() {
        if (!els.seriesMetaList) return;
        const meta = state.metadata || {};
        const rows = [
            ['Patient', meta.patientName],
            ['Patient ID', meta.patientId],
            ['Modality', meta.modality],
            ['Body part', meta.bodyPartExamined],
            ['Study', meta.studyDescription],
            ['Series', meta.seriesDescription],
            ['Frames', String(state.slices.length)]
        ];
        els.seriesMetaList.innerHTML = rows
            .filter(function (r) { return r[1]; })
            .map(function (r) {
                return '<dt>' + r[0] + '</dt><dd>' + escapeHtml(String(r[1])) + '</dd>';
            })
            .join('');
    }

    function escapeHtml(text) {
        const d = document.createElement('div');
        d.textContent = text;
        return d.innerHTML;
    }

    function showSessionUi() {
        if (els.emptyState) els.emptyState.classList.add('d-none');
        if (els.viewportWrap) els.viewportWrap.classList.remove('d-none');
        setViewerEnabled(true);
        updateStudyMeta();
        buildThumbnails();
        loadMainSlice(0);
    }

    function riskClass(level) {
        var l = (level || '').toLowerCase();
        if (l === 'high') return 'risk-high';
        if (l === 'medium') return 'risk-medium';
        if (l === 'low') return 'risk-low';
        return 'risk-minimal';
    }

    function renderAnalysis(analysis) {
        if (!analysis) return;
        state.analysis = analysis;

        if (els.aiPlaceholder) els.aiPlaceholder.classList.add('d-none');
        if (els.aiAnalyzing) els.aiAnalyzing.classList.add('d-none');
        if (els.aiResults) els.aiResults.classList.remove('d-none');

        if (els.aiPredicted) els.aiPredicted.textContent = analysis.predictedDisease || '—';
        var conf = typeof analysis.confidenceScore === 'number' ? analysis.confidenceScore : 0;
        if (els.aiConfidence) els.aiConfidence.textContent = (conf * 100).toFixed(1) + '%';
        if (els.aiConfidenceBar) els.aiConfidenceBar.style.width = Math.min(100, conf * 100) + '%';
        if (els.aiRisk) {
            els.aiRisk.textContent = analysis.riskLevel || '—';
            els.aiRisk.className = 'ct-risk-badge ' + riskClass(analysis.riskLevel);
        }
        if (els.aiModel) els.aiModel.textContent = analysis.modelName || '—';

        if (els.aiProbList && analysis.probabilities) {
            var entries = Object.entries(analysis.probabilities).sort(function (a, b) { return b[1] - a[1]; });
            els.aiProbList.innerHTML = entries.slice(0, 12).map(function (kv) {
                return '<div class="ct-prob-row"><span>' + escapeHtml(kv[0]) + '</span><strong>' +
                    (kv[1] * 100).toFixed(1) + '%</strong></div>';
            }).join('');
        }
    }

    async function fetchSession() {
        if (!state.sessionId) return;
        var url = cfg.sessionUrl + '?sessionId=' + encodeURIComponent(state.sessionId);
        var response = await fetch(url, { credentials: 'same-origin' });
        if (!response.ok) throw new Error('Could not load viewer session.');
        var data = await response.json();
        state.slices = data.summary?.slices || [];
        state.metadata = data.summary?.metadata || null;
        if (data.analysis) renderAnalysis(data.analysis);
        showSessionUi();
    }

    async function uploadFile(file) {
        if (!file) return;
        var formData = new FormData();
        formData.append('dicomFile', file);
        formData.append('__RequestVerificationToken', antiforgeryToken());
        var response = await fetch(cfg.uploadUrl, {
            method: 'POST',
            body: formData,
            credentials: 'same-origin'
        });
        var data = await response.json();
        if (!response.ok || !data.success) {
            throw new Error(data.error || 'Upload failed.');
        }
        state.sessionId = data.sessionId;
        var newUrl = data.redirect || (window.location.pathname + '?sessionId=' + data.sessionId);
        window.history.replaceState({}, '', newUrl);
        state.slices = data.summary?.slices || [];
        state.metadata = data.summary?.metadata || null;
        state.analysis = null;
        if (els.aiResults) els.aiResults.classList.add('d-none');
        if (els.aiPlaceholder) els.aiPlaceholder.classList.remove('d-none');
        resetViewport();
        stopPlay();
        showSessionUi();
        showToast('Scan loaded — ' + state.slices.length + ' slice(s)', true);
    }

    async function analyzeScan() {
        if (!state.sessionId) return;
        if (els.aiPlaceholder) els.aiPlaceholder.classList.add('d-none');
        if (els.aiResults) els.aiResults.classList.add('d-none');
        if (els.aiAnalyzing) els.aiAnalyzing.classList.remove('d-none');
        if (els.btnAnalyzeScan) els.btnAnalyzeScan.disabled = true;

        try {
            var formData = new FormData();
            formData.append('__RequestVerificationToken', antiforgeryToken());
            var response = await fetch(cfg.analyzeUrl + '?sessionId=' + encodeURIComponent(state.sessionId), {
                method: 'POST',
                body: formData,
                credentials: 'same-origin'
            });
            var data = await response.json();
            if (!data.success) throw new Error(data.error || 'Analysis failed.');
            renderAnalysis(data.analysis);
            showToast('AI analysis complete', true);
        } catch (err) {
            if (els.aiAnalyzing) els.aiAnalyzing.classList.add('d-none');
            if (els.aiPlaceholder) els.aiPlaceholder.classList.remove('d-none');
            showToast(err.message || 'Analysis failed.');
        } finally {
            if (els.btnAnalyzeScan) els.btnAnalyzeScan.disabled = false;
        }
    }

    function bindUploadInputs() {
        els.uploadInputs.forEach(function (input) {
            if (!input) return;
            input.addEventListener('change', function (e) {
                var file = e.target.files && e.target.files[0];
                if (!file) return;
                uploadFile(file).catch(function (err) {
                    showToast(err.message);
                }).finally(function () {
                    input.value = '';
                });
            });
        });
    }

    function bindControls() {
        if (els.btnBack) {
            els.btnBack.addEventListener('click', function () {
                if (window.history.length > 1) window.history.back();
                else window.location.href = '/Analytics';
            });
        }

        if (els.btnPrev) {
            els.btnPrev.addEventListener('click', function () {
                stopPlay();
                loadMainSlice(Math.max(0, state.currentIndex - 1));
            });
        }

        if (els.btnNext) {
            els.btnNext.addEventListener('click', function () {
                stopPlay();
                loadMainSlice(Math.min(state.slices.length - 1, state.currentIndex + 1));
            });
        }

        if (els.btnPlay) {
            els.btnPlay.addEventListener('click', function () {
                if (state.isPlaying) {
                    stopPlay();
                    return;
                }
                if (state.slices.length <= 1) return;
                state.isPlaying = true;
                els.btnPlay.innerHTML = '<i class="bi bi-pause-fill"></i><span>Pause</span>';
                state.playTimer = setInterval(function () {
                    var next = state.currentIndex + 1;
                    if (next >= state.slices.length) next = 0;
                    loadMainSlice(next);
                }, 450);
            });
        }

        if (els.btnZoomIn) {
            els.btnZoomIn.addEventListener('click', function () {
                state.zoom = Math.min(5, state.zoom + 0.25);
                applyTransform();
            });
        }

        if (els.btnZoomOut) {
            els.btnZoomOut.addEventListener('click', function () {
                state.zoom = Math.max(0.25, state.zoom - 0.25);
                applyTransform();
            });
        }

        if (els.btnPanMode) {
            els.btnPanMode.addEventListener('click', function () {
                state.panMode = !state.panMode;
                els.btnPanMode.classList.toggle('active', state.panMode);
                if (els.viewport) els.viewport.classList.toggle('pan-mode', state.panMode);
            });
        }

        if (els.btnResetViewer) {
            els.btnResetViewer.addEventListener('click', function () {
                stopPlay();
                resetViewport();
                loadMainSlice(state.currentIndex);
            });
        }

        if (els.btnAnalyzeScan) els.btnAnalyzeScan.addEventListener('click', analyzeScan);

        if (els.btnDownloadReport) {
            els.btnDownloadReport.addEventListener('click', function () {
                if (!state.sessionId) return;
                window.location.href = cfg.reportUrl + '?sessionId=' + encodeURIComponent(state.sessionId);
            });
        }

        if (els.btnSeries) {
            els.btnSeries.addEventListener('click', function () {
                renderSeriesDrawer();
                if (els.seriesDrawer) els.seriesDrawer.classList.remove('d-none');
            });
        }

        if (els.btnCloseSeries) {
            els.btnCloseSeries.addEventListener('click', function () {
                if (els.seriesDrawer) els.seriesDrawer.classList.add('d-none');
            });
        }

        if (els.viewport) {
            els.viewport.addEventListener('wheel', function (e) {
                if (!state.sessionId) return;
                e.preventDefault();
                var delta = e.deltaY > 0 ? -0.15 : 0.15;
                state.zoom = Math.min(5, Math.max(0.25, state.zoom + delta));
                applyTransform();
            }, { passive: false });

            els.viewport.addEventListener('mousedown', function (e) {
                if (!state.panMode) return;
                state.isDragging = true;
                state.dragStart = { x: e.clientX - state.panX, y: e.clientY - state.panY };
                els.viewport.classList.add('dragging');
            });

            window.addEventListener('mousemove', function (e) {
                if (!state.isDragging) return;
                state.panX = e.clientX - state.dragStart.x;
                state.panY = e.clientY - state.dragStart.y;
                applyTransform();
            });

            window.addEventListener('mouseup', function () {
                state.isDragging = false;
                if (els.viewport) els.viewport.classList.remove('dragging');
            });
        }

        window.addEventListener('keydown', function (e) {
            if (!state.sessionId || state.slices.length === 0) return;
            if (e.key === 'ArrowLeft') {
                stopPlay();
                loadMainSlice(Math.max(0, state.currentIndex - 1));
            } else if (e.key === 'ArrowRight') {
                stopPlay();
                loadMainSlice(Math.min(state.slices.length - 1, state.currentIndex + 1));
            } else if (e.key === ' ') {
                e.preventDefault();
                if (els.btnPlay) els.btnPlay.click();
            }
        });
    }

    bindUploadInputs();
    bindControls();
    applyTransform();

    if (cfg.hasSession && cfg.sessionId) {
        fetchSession().catch(function (err) {
            showToast(err.message);
        });
    }
})();
