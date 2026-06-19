(function () {
    'use strict';

    const config = window.ctScanConfig || {};
    const placeholders = config.placeholders || [
        '/images/ct-placeholders/ct-1.svg',
        '/images/ct-placeholders/ct-2.svg',
        '/images/ct-placeholders/ct-3.svg',
        '/images/ct-placeholders/ct-4.svg',
        '/images/ct-placeholders/ct-5.svg'
    ];

    let scans = [];
    let currentIndex = 0;
    let isPlaying = false;
    let playInterval = null;
    let isZoomed = false;
    let panMode = false;
    let rectMode = false;
    let annotateMode = false;
    let uploadFile = null;

    const panOffsets = [{ x: 0, y: 0 }, { x: 0, y: 0 }, { x: 0, y: 0 }, { x: 0, y: 0 }];
    const annotations = [[], [], [], []];

    const els = {
        sidebar: document.getElementById('ctSidebar'),
        thumbnailList: document.getElementById('thumbnailList'),
        panels: document.querySelectorAll('.ctscan-panel'),
        resultsContent: document.getElementById('resultsContent'),
        btnSeries: document.getElementById('btnSeries'),
        btnPrevious: document.getElementById('btnPrevious'),
        btnNext: document.getElementById('btnNext'),
        btnPlay: document.getElementById('btnPlay'),
        btnZoom: document.getElementById('btnZoom'),
        btnDelete: document.getElementById('btnDelete'),
        btnPan: document.getElementById('btnPan'),
        btnRectangle: document.getElementById('btnRectangle'),
        btnAnnotate: document.getElementById('btnAnnotate'),
        btnMore: document.getElementById('btnMore'),
        btnAnalyze: document.getElementById('btnAnalyze'),
        moreDropdown: document.getElementById('moreDropdown'),
        uploadOverlay: document.getElementById('uploadOverlay'),
        uploadFileInput: document.getElementById('uploadFileInput'),
        uploadFileName: document.getElementById('uploadFileName'),
        uploadZone: document.getElementById('uploadZone'),
        uploadCancel: document.getElementById('uploadCancel'),
        uploadSubmit: document.getElementById('uploadSubmit'),
        menuUpload: document.getElementById('menuUpload'),
        menuDownload: document.getElementById('menuDownload'),
        menuReset: document.getElementById('menuReset')
    };

    function mapDbScan(s) {
        return {
            id: s.id,
            imageUrl: s.imageUrl,
            label: s.label,
            isPlaceholder: false,
            predictionResult: s.predictionResult,
            confidence: s.confidence,
            notes: s.notes
        };
    }

    async function loadScans(selectLast) {
        try {
            if (config.getScansUrl) {
                const res = await fetch(config.getScansUrl);
                const data = await res.json();
                if (data.success && data.scans && data.scans.length > 0) {
                    scans = data.scans.map(mapDbScan);
                } else {
                    scans = buildDefaultScans();
                }
            } else {
                scans = buildDefaultScans();
            }
        } catch {
            scans = buildDefaultScans();
        }

        if (selectLast) currentIndex = Math.max(0, getScanList().length - 1);
        else currentIndex = Math.min(currentIndex, Math.max(0, getScanList().length - 1));

        renderThumbnails();
        updateViewer();
        showResultsForCurrent();
    }

    function buildDefaultScans() {
        return placeholders.map((url, i) => ({
            id: 'placeholder-' + i,
            imageUrl: url,
            label: `CT ${i + 1}/5 CHEST`,
            isPlaceholder: true
        }));
    }

    function getScanList() {
        return scans.length ? scans : buildDefaultScans();
    }

    function getCurrentScan() {
        const list = getScanList();
        return list[currentIndex] || list[0];
    }

    function renderThumbnails() {
        if (!els.thumbnailList) return;
        const list = getScanList();
        els.thumbnailList.innerHTML = '';

        list.forEach((scan, idx) => {
            const div = document.createElement('div');
            div.className = 'ctscan-thumbnail' + (idx === currentIndex ? ' active' : '');
            div.innerHTML = `
                <img src="${scan.imageUrl}" alt="${scan.label}" />
                <div class="thumb-label">${scan.label}</div>
            `;
            div.addEventListener('click', () => selectScan(idx));
            els.thumbnailList.appendChild(div);
        });
    }

    function selectScan(index) {
        const list = getScanList();
        currentIndex = ((index % list.length) + list.length) % list.length;
        renderThumbnails();
        updateViewer();
        showResultsForCurrent();
    }

    function updateViewer() {
        const list = getScanList();
        els.panels.forEach((panel, panelIdx) => {
            const scanIdx = (currentIndex + panelIdx) % list.length;
            const scan = list[scanIdx];
            const img = panel.querySelector('img');
            const label = panel.querySelector('.panel-label');
            img.src = scan.imageUrl;
            label.textContent = scan.label;
            panel.classList.toggle('primary', panelIdx === 0);
            applyTransform(panel, panelIdx);
            resizeCanvas(panel);
        });
    }

    function applyTransform(panel, panelIdx) {
        const img = panel.querySelector('img');
        const scale = isZoomed ? 1.8 : 1;
        const { x, y } = panOffsets[panelIdx];
        img.style.transform = `translate(${x}px, ${y}px) scale(${scale})`;
    }

    function resizeCanvas(panel) {
        const canvas = panel.querySelector('.ctscan-annotation-layer');
        if (!canvas) return;
        const rect = panel.getBoundingClientRect();
        canvas.width = rect.width;
        canvas.height = rect.height;
        redrawAnnotations(panel);
    }

    function redrawAnnotations(panel) {
        const panelIdx = parseInt(panel.dataset.panel, 10);
        const canvas = panel.querySelector('.ctscan-annotation-layer');
        const ctx = canvas.getContext('2d');
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        annotations[panelIdx].forEach(ann => {
            if (ann.type === 'rect') {
                ctx.strokeStyle = '#127FEC';
                ctx.lineWidth = 2;
                ctx.strokeRect(ann.x, ann.y, ann.w, ann.h);
            } else if (ann.type === 'text') {
                ctx.fillStyle = 'rgba(18, 127, 236, 0.85)';
                ctx.font = '13px Inter, sans-serif';
                const w = ctx.measureText(ann.text).width;
                ctx.fillRect(ann.x, ann.y - 16, w + 10, 20);
                ctx.fillStyle = '#fff';
                ctx.fillText(ann.text, ann.x + 5, ann.y);
            }
        });
    }

    function showResultsForCurrent() {
        const scan = getCurrentScan();
        if (scan.predictionResult) {
            renderResults(scan.predictionResult, scan.confidence, scan.notes);
        } else {
            els.resultsContent.innerHTML = '<p class="placeholder-text">Click "Analyze Scan" to run AI analysis.</p>';
        }
    }

    function renderResults(prediction, confidence, notes) {
        els.resultsContent.innerHTML = `
            <div class="ctscan-result-item">
                <div class="result-label">Prediction</div>
                <div class="result-value">${prediction}</div>
            </div>
            <div class="ctscan-result-item">
                <div class="result-label">Confidence</div>
                <div class="result-confidence">${confidence}%</div>
            </div>
            <div class="ctscan-result-item">
                <div class="result-label">Notes</div>
                <div class="result-notes">${notes}</div>
            </div>
        `;
    }

    function clearModes() {
        panMode = rectMode = annotateMode = false;
        ['btnPan', 'btnRectangle', 'btnAnnotate'].forEach(id => els[id]?.classList.remove('active'));
        els.panels.forEach(p => {
            p.classList.remove('pan-mode');
            p.querySelector('.ctscan-annotation-layer')?.classList.remove('interactive', 'pan-active');
        });
    }

    function stopSlideshow() {
        isPlaying = false;
        clearInterval(playInterval);
        els.btnPlay?.classList.remove('active');
        const icon = els.btnPlay?.querySelector('i');
        const text = els.btnPlay?.querySelector('.btn-text');
        if (icon) icon.className = 'bi bi-play-fill';
        if (text) text.textContent = 'Play';
    }

    function resetViewer() {
        stopSlideshow();
        isZoomed = false;
        clearModes();
        panOffsets.forEach(o => { o.x = 0; o.y = 0; });
        annotations.forEach(a => a.length = 0);
        els.btnZoom?.classList.remove('active');
        els.panels.forEach((p, i) => { applyTransform(p, i); redrawAnnotations(p); });
        els.resultsContent.innerHTML = '<p class="placeholder-text">Click "Analyze Scan" to run AI analysis.</p>';
    }

    // Toolbar
    els.btnSeries?.addEventListener('click', () => {
        els.sidebar?.classList.toggle('hidden');
        els.btnSeries.classList.toggle('active');
    });

    els.btnPrevious?.addEventListener('click', () => selectScan(currentIndex - 1));
    els.btnNext?.addEventListener('click', () => selectScan(currentIndex + 1));

    els.btnPlay?.addEventListener('click', () => {
        if (isPlaying) { stopSlideshow(); return; }
        isPlaying = true;
        els.btnPlay.classList.add('active');
        els.btnPlay.querySelector('i').className = 'bi bi-pause-fill';
        els.btnPlay.querySelector('.btn-text').textContent = 'Stop';
        playInterval = setInterval(() => selectScan(currentIndex + 1), 1000);
    });

    els.btnZoom?.addEventListener('click', () => {
        isZoomed = !isZoomed;
        els.btnZoom.classList.toggle('active', isZoomed);
        els.btnZoom.querySelector('i').className = isZoomed ? 'bi bi-zoom-out' : 'bi bi-zoom-in';
        els.panels.forEach((p, i) => applyTransform(p, i));
    });

    els.btnDelete?.addEventListener('click', async () => {
        const scan = getCurrentScan();
        if (scan.isPlaceholder) {
            alert('Cannot delete default placeholder scans. Upload your own scans first.');
            return;
        }
        if (!confirm('Delete this scan?')) return;

        try {
            const res = await fetch(config.deleteUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ id: scan.id })
            });
            const data = await res.json();
            if (data.success) {
                await loadScans(false);
            } else {
                alert(data.error || 'Delete failed.');
            }
        } catch (e) {
            alert('Delete failed: ' + e.message);
        }
    });

    els.btnPan?.addEventListener('click', () => {
        const on = !panMode;
        clearModes();
        if (on) {
            panMode = true;
            els.btnPan.classList.add('active');
            els.panels.forEach(p => {
                p.classList.add('pan-mode');
                p.querySelector('.ctscan-annotation-layer')?.classList.add('pan-active');
            });
        }
    });

    els.btnRectangle?.addEventListener('click', () => {
        const on = !rectMode;
        clearModes();
        if (on) {
            rectMode = true;
            els.btnRectangle.classList.add('active');
            els.panels[0].querySelector('.ctscan-annotation-layer')?.classList.add('interactive');
        }
    });

    els.btnAnnotate?.addEventListener('click', () => {
        const on = !annotateMode;
        clearModes();
        if (on) {
            annotateMode = true;
            els.btnAnnotate.classList.add('active');
            els.panels[0].querySelector('.ctscan-annotation-layer')?.classList.add('interactive');
        }
    });

    // Pan + annotations on primary panel
    let isDragging = false;
    let dragStart = { x: 0, y: 0 };
    let dragPanelIdx = 0;
    let rectStart = null;

    els.panels.forEach((panel, panelIdx) => {
        const canvas = panel.querySelector('.ctscan-annotation-layer');
        canvas?.addEventListener('mousedown', e => {
            if (panMode) {
                isDragging = true;
                dragPanelIdx = panelIdx;
                dragStart = { x: e.clientX - panOffsets[panelIdx].x, y: e.clientY - panOffsets[panelIdx].y };
                e.preventDefault();
            }
            if (rectMode && panelIdx === 0) {
                const rect = canvas.getBoundingClientRect();
                rectStart = { x: e.clientX - rect.left, y: e.clientY - rect.top };
                e.preventDefault();
            }
        });
        canvas?.addEventListener('mouseup', e => {
            if (rectMode && rectStart && panelIdx === 0) {
                const rect = canvas.getBoundingClientRect();
                const endX = e.clientX - rect.left;
                const endY = e.clientY - rect.top;
                annotations[0].push({
                    type: 'rect',
                    x: Math.min(rectStart.x, endX),
                    y: Math.min(rectStart.y, endY),
                    w: Math.abs(endX - rectStart.x),
                    h: Math.abs(endY - rectStart.y)
                });
                rectStart = null;
                redrawAnnotations(panel);
            }
            if (annotateMode && panelIdx === 0) {
                const rect = canvas.getBoundingClientRect();
                const text = prompt('Enter annotation:');
                if (text) {
                    annotations[0].push({ type: 'text', x: e.clientX - rect.left, y: e.clientY - rect.top, text });
                    redrawAnnotations(panel);
                }
            }
        });
    });

    document.addEventListener('mousemove', e => {
        if (isDragging && panMode) {
            panOffsets[dragPanelIdx].x = e.clientX - dragStart.x;
            panOffsets[dragPanelIdx].y = e.clientY - dragStart.y;
            applyTransform(els.panels[dragPanelIdx], dragPanelIdx);
        }
    });
    document.addEventListener('mouseup', () => { isDragging = false; });

    // More menu
    els.btnMore?.addEventListener('click', e => { e.stopPropagation(); els.moreDropdown?.classList.toggle('show'); });
    document.addEventListener('click', () => els.moreDropdown?.classList.remove('show'));
    els.menuUpload?.addEventListener('click', () => { els.moreDropdown?.classList.remove('show'); openUpload(); });
    els.menuDownload?.addEventListener('click', () => {
        els.moreDropdown?.classList.remove('show');
        const scan = getCurrentScan();
        const blob = new Blob([JSON.stringify({ scan: scan.label, prediction: scan.predictionResult }, null, 2)], { type: 'application/json' });
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = 'ct-scan-report.json';
        a.click();
    });
    els.menuReset?.addEventListener('click', () => { els.moreDropdown?.classList.remove('show'); resetViewer(); });

    // Analyze (placeholder AI)
    els.btnAnalyze?.addEventListener('click', () => {
        els.btnAnalyze.classList.add('loading');
        els.btnAnalyze.innerHTML = '<i class="bi bi-hourglass-split"></i><span class="btn-text">Analyzing...</span>';
        setTimeout(() => {
            const scan = getCurrentScan();
            scan.predictionResult = 'Possible Pneumonia';
            scan.confidence = 98.4;
            scan.notes = 'AI detected abnormal chest pattern. Please review by specialist.';
            renderResults(scan.predictionResult, scan.confidence, scan.notes);
            els.btnAnalyze.classList.remove('loading');
            els.btnAnalyze.innerHTML = '<i class="bi bi-cpu"></i><span class="btn-text">Analyze Scan</span>';
        }, 800);
    });

    // Upload (client-side preview only)
    function openUpload() {
        uploadFile = null;
        els.uploadFileInput.value = '';
        els.uploadFileName.textContent = 'Drop file here or click to browse';
        els.uploadSubmit.disabled = true;
        els.uploadOverlay?.classList.add('show');
    }

    els.uploadCancel?.addEventListener('click', () => els.uploadOverlay?.classList.remove('show'));
    els.uploadOverlay?.addEventListener('click', e => { if (e.target === els.uploadOverlay) els.uploadOverlay.classList.remove('show'); });
    els.uploadFileInput?.addEventListener('change', e => setUploadFile(e.target.files[0]));
    els.uploadZone?.addEventListener('dragover', e => { e.preventDefault(); els.uploadZone.classList.add('dragover'); });
    els.uploadZone?.addEventListener('dragleave', () => els.uploadZone.classList.remove('dragover'));
    els.uploadZone?.addEventListener('drop', e => { e.preventDefault(); els.uploadZone.classList.remove('dragover'); setUploadFile(e.dataTransfer.files[0]); });

    function setUploadFile(file) {
        if (!file) return;
        uploadFile = file;
        els.uploadFileName.textContent = file.name;
        els.uploadSubmit.disabled = false;
    }

    els.uploadSubmit?.addEventListener('click', async () => {
        if (!uploadFile) return;

        const formData = new FormData();
        formData.append('file', uploadFile);
        const patientName = document.getElementById('uploadPatientName')?.value;
        if (patientName) formData.append('patientName', patientName);

        els.uploadSubmit.disabled = true;
        els.uploadSubmit.textContent = 'Uploading...';

        try {
            const res = await fetch(config.uploadUrl, { method: 'POST', body: formData });
            const data = await res.json();
            if (data.success) {
                els.uploadOverlay.classList.remove('show');
                await loadScans(true);
            } else {
                alert(data.error || 'Upload failed.');
            }
        } catch (e) {
            alert('Upload failed: ' + e.message);
        } finally {
            els.uploadSubmit.disabled = false;
            els.uploadSubmit.textContent = 'Upload';
        }
    });

    window.addEventListener('resize', () => els.panels.forEach(p => resizeCanvas(p)));

    loadScans(false);
})();
