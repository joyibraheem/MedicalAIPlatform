(function () {
    'use strict';

    const root = document.getElementById('ctViewerRoot');
    if (!root) return;

    const cfg = {
        sessionId: root.dataset.sessionId || '',
        hasSession: root.dataset.hasSession === 'true',
        patientId: root.dataset.patientId || '',
        patientUrl: root.dataset.patientUrl || '',
        uploadUrl: root.dataset.uploadUrl,
        sessionUrl: root.dataset.sessionUrl,
        sliceUrlBase: root.dataset.sliceUrlBase,
        analyzeUrl: root.dataset.analyzeUrl,
        reportUrl: root.dataset.reportUrl
    };

    const state = {
        sessionId: cfg.sessionId,
        series: [],
        metadata: null,
        layoutMode: 'single',
        activeSeriesIndex: 0,
        sliceIndexBySeries: {},
        currentIndex: 0,
        zoom: 1,
        fitScale: 1,
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
        viewportGrid: document.getElementById('viewportGrid'),
        mainImage: document.getElementById('mainSliceImage'),
        viewportLoading: document.getElementById('viewportLoading'),
        seriesList: document.getElementById('seriesList'),
        seriesCountLabel: document.getElementById('seriesCountLabel'),
        thumbsList: document.getElementById('thumbsList'),
        thumbCounter: document.getElementById('thumbCounter'),
        currentSliceLabel: document.getElementById('currentSliceLabel'),
        zoomLabel: document.getElementById('zoomLabel'),
        overlaySeries: document.getElementById('overlaySeries'),
        overlaySlice: document.getElementById('overlaySlice'),
        overlayZoom: document.getElementById('overlayZoom'),
        toolbarSliceIndicator: document.getElementById('toolbarSliceIndicator'),
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
        btnResetViewport: document.getElementById('btnResetViewport'),
        btnSeries: document.getElementById('btnSeries'),
        btnCloseSeries: document.getElementById('btnCloseSeries'),
        btnBack: document.getElementById('btnBack'),
        uploadInputs: [document.getElementById('ctUploadInput'), document.getElementById('ctUploadInputHero')]
    };

    function antiforgeryToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    }

    function sliceUrl(seriesIndex, sliceIndex) {
        return cfg.sliceUrlBase +
            '?sessionId=' + encodeURIComponent(state.sessionId) +
            '&seriesIndex=' + seriesIndex +
            '&index=' + sliceIndex;
    }

    function getActiveSeries() {
        return state.series.find(function (s) { return s.seriesIndex === state.activeSeriesIndex; })
            || state.series[state.activeSeriesIndex]
            || null;
    }

    function getActiveSlices() {
        var s = getActiveSeries();
        return s && s.slices ? s.slices : [];
    }

    function escapeHtml(text) {
        var d = document.createElement('div');
        d.textContent = text;
        return d.innerHTML;
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
            els.btnPrev, els.btnNext, els.btnPlay, els.btnSeries, els.btnZoomIn, els.btnZoomOut,
            els.btnPanMode, els.btnAnalyzeScan, els.btnDownloadReport, els.btnResetViewport
        ].forEach(function (btn) {
            if (btn) btn.disabled = !enabled;
        });
        if (!enabled && els.btnDownloadReport) els.btnDownloadReport.disabled = true;
    }

    function computeFitScale() {
        if (!els.mainImage || !els.viewport) return 1;
        var nw = els.mainImage.naturalWidth || 1;
        var nh = els.mainImage.naturalHeight || 1;
        var vw = els.viewport.clientWidth || 1;
        var vh = els.viewport.clientHeight || 1;
        return Math.min(vw / nw, vh / nh, 1) || 1;
    }

    function applyTransform() {
        if (!els.mainImage) return;
        var scale = state.fitScale * state.zoom;
        els.mainImage.style.transform =
            'translate(calc(-50% + ' + state.panX + 'px), calc(-50% + ' + state.panY + 'px)) scale(' + scale + ')';
        var pct = Math.round(state.zoom * 100) + '%';
        if (els.zoomLabel) els.zoomLabel.textContent = pct;
        if (els.overlayZoom) els.overlayZoom.textContent = pct;
    }

    function resetViewport() {
        state.zoom = 1;
        state.panX = 0;
        state.panY = 0;
        state.panMode = false;
        state.fitScale = computeFitScale();
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
        var slices = getActiveSlices();
        var last = Math.max(0, slices.length - 1);
        var current = slices.length ? state.currentIndex + 1 : 0;
        var total = slices.length;
        var label = total ? current + ' / ' + total : '0 / 0';
        var series = getActiveSeries();
        var seriesLabel = series ? series.label : '';

        if (els.btnPrev) els.btnPrev.disabled = state.currentIndex <= 0;
        if (els.btnNext) els.btnNext.disabled = state.currentIndex >= last;
        if (els.btnPlay) els.btnPlay.disabled = slices.length <= 1;
        if (els.thumbCounter) els.thumbCounter.textContent = label;
        if (els.toolbarSliceIndicator) els.toolbarSliceIndicator.textContent = label;
        if (els.overlaySlice) els.overlaySlice.textContent = label;
        if (els.overlaySeries) els.overlaySeries.textContent = seriesLabel || '';
        if (els.currentSliceLabel) {
            var slice = slices[state.currentIndex];
            els.currentSliceLabel.textContent = slice
                ? (seriesLabel ? seriesLabel + ' · ' : '') + (slice.label || ('Slice ' + (state.currentIndex + 1)))
                : '—';
        }
    }

    function highlightThumb(index) {
        document.querySelectorAll('.ct-thumb').forEach(function (el) {
            el.classList.toggle('active', parseInt(el.dataset.index, 10) === index);
        });
        var active = document.querySelector('.ct-thumb[data-index="' + index + '"]');
        if (active) active.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }

    function highlightSeriesCard(seriesIndex) {
        document.querySelectorAll('.ct-series-card').forEach(function (el) {
            el.classList.toggle('active', parseInt(el.dataset.seriesIndex, 10) === seriesIndex);
        });
    }

    function renderLayoutMode() {
        var useGrid = state.layoutMode === 'grid' && state.series.length >= 2;
        if (els.viewport) els.viewport.classList.toggle('d-none', useGrid);
        if (els.viewportGrid) {
            els.viewportGrid.classList.toggle('d-none', !useGrid);
            if (useGrid) renderViewportGrid();
        }
    }

    function renderViewportGrid() {
        if (!els.viewportGrid) return;
        els.viewportGrid.innerHTML = '';
        var cols = state.series.length >= 4 ? 2 : state.series.length;
        els.viewportGrid.className = 'ct-viewport-grid ct-viewport-grid--' + Math.min(state.series.length, 4);

        state.series.slice(0, 4).forEach(function (series) {
            var savedIdx = state.sliceIndexBySeries[series.seriesIndex];
            var idx = typeof savedIdx === 'number' ? savedIdx : series.previewSliceIndex || 0;
            idx = Math.min(Math.max(0, idx), Math.max(0, series.slices.length - 1));

            var cell = document.createElement('button');
            cell.type = 'button';
            cell.className = 'ct-grid-cell' + (series.seriesIndex === state.activeSeriesIndex ? ' active' : '');
            cell.dataset.seriesIndex = String(series.seriesIndex);
            cell.innerHTML =
                '<div class="ct-grid-cell__label">' + escapeHtml(series.label) + '</div>' +
                '<img src="' + sliceUrl(series.seriesIndex, idx) + '" alt="" />' +
                '<div class="ct-grid-cell__meta">' + (idx + 1) + ' / ' + series.slices.length + '</div>';
            cell.addEventListener('click', function () {
                selectSeries(series.seriesIndex, false);
            });
            els.viewportGrid.appendChild(cell);
        });
    }

    function loadMainSlice(index) {
        var slices = getActiveSlices();
        if (!state.sessionId || index < 0 || index >= slices.length) return;

        state.currentIndex = index;
        state.sliceIndexBySeries[state.activeSeriesIndex] = index;
        var slice = slices[index];

        if (state.layoutMode === 'grid' && state.series.length >= 2) {
            renderViewportGrid();
            updateNavButtons();
            highlightThumb(index);
            return;
        }

        if (els.viewportLoading) els.viewportLoading.classList.remove('d-none');
        if (els.mainImage) {
            els.mainImage.onload = function () {
                state.fitScale = computeFitScale();
                if (els.viewportLoading) els.viewportLoading.classList.add('d-none');
                applyTransform();
            };
            els.mainImage.onerror = function () {
                if (els.viewportLoading) els.viewportLoading.classList.add('d-none');
                showToast('Could not load slice image.');
            };
            els.mainImage.src = sliceUrl(state.activeSeriesIndex, index) + '&_=' + Date.now();
        }
        highlightThumb(index);
        updateNavButtons();
    }

    function buildThumbnails() {
        if (!els.thumbsList) return;
        els.thumbsList.innerHTML = '';
        var slices = getActiveSlices();
        var si = state.activeSeriesIndex;

        slices.forEach(function (slice, index) {
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'ct-thumb';
            btn.dataset.index = String(index);
            btn.innerHTML =
                '<img src="' + sliceUrl(si, index) + '" alt="" loading="lazy">' +
                '<span>' + escapeHtml(slice.label || ('Slice ' + (index + 1))) + '</span>';
            btn.addEventListener('click', function () {
                stopPlay();
                loadMainSlice(index);
            });
            els.thumbsList.appendChild(btn);
        });
    }

    function buildSeriesList() {
        if (!els.seriesList) return;
        els.seriesList.innerHTML = '';

        state.series.forEach(function (series) {
            var previewIdx = series.previewSliceIndex || 0;
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'ct-series-card';
            btn.dataset.seriesIndex = String(series.seriesIndex);
            btn.innerHTML =
                '<div class="ct-series-card__thumb">' +
                '<img src="' + sliceUrl(series.seriesIndex, previewIdx) + '" alt="" loading="lazy" />' +
                '</div>' +
                '<div class="ct-series-card__body">' +
                '<div class="ct-series-card__title">' + escapeHtml(series.label || ('Series ' + (series.seriesIndex + 1))) + '</div>' +
                '<div class="ct-series-card__meta">' + escapeHtml(series.modality || '') +
                (series.sliceCount ? ' · ' + series.sliceCount + ' slices' : '') + '</div>' +
                '</div>';
            btn.addEventListener('click', function () {
                selectSeries(series.seriesIndex, true);
            });
            els.seriesList.appendChild(btn);
        });

        if (els.seriesCountLabel) els.seriesCountLabel.textContent = String(state.series.length);
        highlightSeriesCard(state.activeSeriesIndex);
    }

    function selectSeries(seriesIndex, resetZoom) {
        if (!state.series.some(function (s) { return s.seriesIndex === seriesIndex; })) return;
        stopPlay();
        state.activeSeriesIndex = seriesIndex;
        highlightSeriesCard(seriesIndex);

        var saved = state.sliceIndexBySeries[seriesIndex];
        state.currentIndex = typeof saved === 'number'
            ? Math.min(saved, Math.max(0, getActiveSlices().length - 1))
            : 0;

        if (resetZoom) resetViewport();
        buildThumbnails();
        renderLayoutMode();
        loadMainSlice(state.currentIndex);
        updateStudyMeta();
    }

    function applySessionSummary(summary) {
        state.series = summary.series || [];
        if (state.series.length === 0 && summary.slices) {
            state.series = [{
                seriesIndex: 0,
                label: 'Series 1',
                modality: summary.metadata?.modality || 'CT',
                sliceCount: summary.slices.length,
                previewSliceIndex: 0,
                slices: summary.slices
            }];
        }
        state.metadata = summary.metadata || null;
        state.layoutMode = summary.layoutMode || (state.series.length >= 2 ? 'grid' : 'single');
        state.activeSeriesIndex = state.series[0]?.seriesIndex ?? 0;
        state.sliceIndexBySeries = {};
        state.currentIndex = 0;
    }

    function updateStudyMeta() {
        var meta = state.metadata || {};
        var active = getActiveSeries();
        var totalSlices = state.series.reduce(function (n, s) { return n + (s.slices ? s.slices.length : s.sliceCount || 0); }, 0);

        if (els.studyPatient) {
            els.studyPatient.textContent = meta.patientName
                ? meta.patientName + (meta.patientId ? ' · ' + meta.patientId : '')
                : 'Unknown patient';
        }
        if (els.studyModality) {
            var mod = active?.modality || meta.modality;
            els.studyModality.textContent = [mod, meta.bodyPartExamined].filter(Boolean).join(' · ') || 'CT';
        }
        if (els.studySliceCount) {
            var seriesPart = state.series.length > 1 ? state.series.length + ' series · ' : '';
            els.studySliceCount.textContent = seriesPart + totalSlices + ' slice' + (totalSlices === 1 ? '' : 's');
        }
    }

    function renderSeriesDrawer() {
        if (!els.seriesMetaList) return;
        var meta = state.metadata || {};
        var rows = [
            ['Patient', meta.patientName],
            ['Patient ID', meta.patientId],
            ['Study', meta.studyDescription],
            ['Series count', String(state.series.length)],
            ['Total slices', String(state.series.reduce(function (n, s) { return n + (s.slices?.length || s.sliceCount || 0); }, 0))]
        ];
        state.series.forEach(function (s) {
            rows.push(['Series ' + (s.seriesIndex + 1), s.label + ' (' + (s.slices?.length || s.sliceCount) + ' slices)']);
        });
        els.seriesMetaList.innerHTML = rows
            .filter(function (r) { return r[1]; })
            .map(function (r) {
                return '<dt>' + r[0] + '</dt><dd>' + escapeHtml(String(r[1])) + '</dd>';
            })
            .join('');
    }

    function showSessionUi() {
        if (els.emptyState) els.emptyState.classList.add('d-none');
        if (els.viewportWrap) els.viewportWrap.classList.remove('d-none');
        setViewerEnabled(true);
        buildSeriesList();
        updateStudyMeta();
        buildThumbnails();
        renderLayoutMode();
        loadMainSlice(state.currentIndex);
    }

    function resetAiPlaceholder() {
        if (!els.aiPlaceholder) return;
        els.aiPlaceholder.innerHTML =
            '<i class="bi bi-lightning-charge"></i>' +
            '<p>Click <strong>Analyze Scan</strong> to run the DICOM pipeline and LungAI inference.</p>';
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

        if (analysis.error) {
            if (els.aiResults) els.aiResults.classList.add('d-none');
            if (els.aiPlaceholder) {
                els.aiPlaceholder.classList.remove('d-none');
                els.aiPlaceholder.innerHTML =
                    '<i class="bi bi-exclamation-triangle"></i>' +
                    '<p><strong>Analysis failed.</strong> ' + escapeHtml(analysis.error) + '</p>';
            }
            if (els.btnDownloadReport) els.btnDownloadReport.disabled = true;
            return;
        }

        if (els.aiResults) els.aiResults.classList.remove('d-none');
        if (els.btnDownloadReport) els.btnDownloadReport.disabled = false;
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
        applySessionSummary(data.summary || {});
        if (data.analysis) renderAnalysis(data.analysis);
        else resetAiPlaceholder();
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
        if (!response.ok || !data.success) throw new Error(data.error || 'Upload failed.');

        state.sessionId = data.sessionId;
        window.history.replaceState({}, '', data.redirect || (window.location.pathname + '?sessionId=' + data.sessionId));
        state.analysis = null;
        if (els.aiResults) els.aiResults.classList.add('d-none');
        resetAiPlaceholder();
        if (els.btnDownloadReport) els.btnDownloadReport.disabled = true;
        applySessionSummary(data.summary || {});
        resetViewport();
        stopPlay();
        showSessionUi();
        var total = state.series.reduce(function (n, s) { return n + (s.slices?.length || 0); }, 0);
        showToast('Study loaded — ' + state.series.length + ' series, ' + total + ' slices', true);
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
            if (!data.success) {
                if (data.analysis) renderAnalysis(data.analysis);
                throw new Error(data.error || 'Analysis failed.');
            }
            renderAnalysis(data.analysis);
            showToast('AI analysis complete', true);
        } catch (err) {
            if (els.aiAnalyzing) els.aiAnalyzing.classList.add('d-none');
            if (!state.analysis || !state.analysis.error) {
                resetAiPlaceholder();
                if (els.aiPlaceholder) els.aiPlaceholder.classList.remove('d-none');
            }
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
                uploadFile(file).catch(function (err) { showToast(err.message); })
                    .finally(function () { input.value = ''; });
            });
        });
    }

    function bindControls() {
        if (els.btnBack) {
            els.btnBack.addEventListener('click', function () {
                if (cfg.patientId && cfg.patientUrl) {
                    window.location.href = cfg.patientUrl + '/' + cfg.patientId;
                    return;
                }
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
                loadMainSlice(Math.min(getActiveSlices().length - 1, state.currentIndex + 1));
            });
        }

        if (els.btnPlay) {
            els.btnPlay.addEventListener('click', function () {
                if (state.isPlaying) { stopPlay(); return; }
                if (getActiveSlices().length <= 1) return;
                state.isPlaying = true;
                els.btnPlay.innerHTML = '<i class="bi bi-pause-fill"></i><span>Pause</span>';
                state.playTimer = setInterval(function () {
                    var next = state.currentIndex + 1;
                    if (next >= getActiveSlices().length) next = 0;
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

        if (els.btnResetViewport) {
            els.btnResetViewport.addEventListener('click', function () {
                stopPlay();
                resetViewport();
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
                if (!state.sessionId || getActiveSlices().length === 0) return;
                if (state.layoutMode === 'grid' && state.series.length >= 2) return;
                e.preventDefault();
                if (e.ctrlKey) {
                    state.zoom = Math.min(5, Math.max(0.25, state.zoom + (e.deltaY > 0 ? -0.12 : 0.12)));
                    applyTransform();
                    return;
                }
                stopPlay();
                loadMainSlice(Math.min(getActiveSlices().length - 1, Math.max(0, state.currentIndex + (e.deltaY > 0 ? 1 : -1))));
            }, { passive: false });

            els.viewport.addEventListener('mousedown', function (e) {
                if (!state.panMode) return;
                state.isDragging = true;
                state.dragStart = { x: e.clientX - state.panX, y: e.clientY - state.panY };
                els.viewport.classList.add('dragging');
            });
        }

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

        window.addEventListener('keydown', function (e) {
            if (!state.sessionId || getActiveSlices().length === 0) return;
            if (e.target && (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA')) return;
            if (e.key === 'ArrowLeft') {
                stopPlay();
                loadMainSlice(Math.max(0, state.currentIndex - 1));
            } else if (e.key === 'ArrowRight') {
                stopPlay();
                loadMainSlice(Math.min(getActiveSlices().length - 1, state.currentIndex + 1));
            } else if (e.key === ' ') {
                e.preventDefault();
                if (els.btnPlay) els.btnPlay.click();
            }
        });

        window.addEventListener('resize', function () {
            if (!state.sessionId) return;
            state.fitScale = computeFitScale();
            applyTransform();
        });
    }

    bindUploadInputs();
    bindControls();

    if (cfg.hasSession && cfg.sessionId) {
        fetchSession().catch(function (err) { showToast(err.message); });
    }
})();
