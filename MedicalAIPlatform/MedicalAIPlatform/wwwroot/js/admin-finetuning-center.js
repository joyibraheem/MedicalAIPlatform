(function () {
    "use strict";

    const API = "/Admin/TrainingCenter";
    const state = {
        dashboard: window.trainingCenterInitial || {},
        selectedModelId: null,
        selectedCheckpointId: null,
        compareIds: new Set(),
        experimentIds: new Set(),
        activeJobId: null,
        charts: {},
        resourceCharts: {},
        pollTimer: null,
        resourceTimer: null,
        refreshSec: window.trainingCenterRefreshSec || 5,
    };

    function token() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
    }

    async function apiGet(path) {
        const r = await fetch(API + path);
        if (!r.ok) throw new Error(await r.text());
        return r.json();
    }

    async function apiPost(path, body, form = false) {
        const headers = { RequestVerificationToken: token() };
        let payload;
        if (form) {
            payload = body instanceof FormData ? body : new URLSearchParams(body);
        } else {
            headers["Content-Type"] = "application/json";
            payload = JSON.stringify(body);
        }
        const r = await fetch(API + path, { method: "POST", headers, body: payload });
        const json = await r.json().catch(() => ({}));
        if (!r.ok) throw new Error(json.error || json.message || r.statusText);
        return json;
    }

    async function apiDelete(path) {
        const r = await fetch(API + path, {
            method: "DELETE",
            headers: { RequestVerificationToken: token() },
        });
        if (!r.ok) throw new Error(await r.text());
        return r.json();
    }

    function pct(v) {
        if (v == null || Number.isNaN(v)) return "—";
        return (v <= 1 ? v * 100 : v).toFixed(1) + "%";
    }

    function fmtDate(d) {
        if (!d) return "—";
        return new Date(d).toLocaleString();
    }

    function fmtBytes(b) {
        if (b == null) return "—";
        if (b < 1024) return b + " B";
        if (b < 1048576) return (b / 1024).toFixed(1) + " KB";
        if (b < 1073741824) return (b / 1048576).toFixed(1) + " MB";
        return (b / 1073741824).toFixed(2) + " GB";
    }

    function fmtDuration(sec) {
        if (sec == null) return "—";
        const m = Math.floor(sec / 60);
        const s = Math.floor(sec % 60);
        return m + "m " + s + "s";
    }

    function statusBadge(status) {
        const s = (status || "Idle").toLowerCase();
        let cls = "ftc-badge-idle";
        if (s.includes("run") || s.includes("train") || s.includes("prep")) cls = "ftc-badge-running";
        if (s.includes("prod")) cls = "ftc-badge-production";
        return `<span class="ftc-badge ${cls}">${status || "Idle"}</span>`;
    }

    function getSelectedModel() {
        return (state.dashboard.models || []).find(m => m.modelId === state.selectedModelId);
    }

    function renderModels() {
        const el = document.getElementById("ftc-model-cards");
        if (!el) return;
        const models = state.dashboard.models || [];
        if (!state.selectedModelId && models.length) state.selectedModelId = models[0].modelId;

        el.innerHTML = models.map(m => {
            const sel = m.modelId === state.selectedModelId ? " selected" : "";
            return `<div class="col-md-6 col-xl-4">
                <div class="ftc-model-card${sel}" data-model-id="${m.modelId}">
                    <div class="d-flex justify-content-between align-items-start">
                        <div>
                            <h3>${esc(m.displayName)}</h3>
                            <div class="ftc-pipeline-kind">${esc(m.pipelineKind)}</div>
                        </div>
                        ${statusBadge(m.status)}
                    </div>
                    <dl class="ftc-model-metrics">
                        <dt>Production</dt><dd><code>${esc(m.currentVersion)}</code></dd>
                        <dt>Deploy</dt><dd>${esc(m.deployStatus)}</dd>
                        <dt>Last training</dt><dd>${fmtDate(m.lastTrainingDate)}</dd>
                        <dt>Dataset</dt><dd>${esc(m.datasetName || "—")}</dd>
                        <dt>Accuracy</dt><dd>${pct(m.productionAccuracy)}</dd>
                        <dt>F1</dt><dd>${pct(m.productionF1)}</dd>
                        <dt>ROC-AUC</dt><dd>${pct(m.productionRocAuc)}</dd>
                        <dt>Checkpoint</dt><dd class="text-truncate" title="${esc(m.productionCheckpoint || "")}">${esc(shortPath(m.productionCheckpoint))}</dd>
                    </dl>
                </div>
            </div>`;
        }).join("");

        el.querySelectorAll(".ftc-model-card").forEach(card => {
            card.addEventListener("click", () => {
                state.selectedModelId = card.dataset.modelId;
                renderModels();
                applyConfigDefaults();
                renderCheckpoints();
                updateSelectedLabel();
            });
        });
        applyConfigDefaults();
        updateSelectedLabel();
    }

    function shortPath(p) {
        if (!p) return "—";
        return p.length > 28 ? "…" + p.slice(-25) : p;
    }

    function esc(s) {
        if (s == null) return "";
        return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
    }

    function applyConfigDefaults() {
        const m = getSelectedModel();
        const d = m?.configDefaults || {};
        const subsetEl = document.getElementById("cfg-subset-size");
        if (subsetEl) {
            const sizes = d.allowedSubsetSizes || [100, 300, 600, 1000, 3000];
            subsetEl.innerHTML = sizes.map(s => `<option value="${s}">${s}</option>`).join("");
            subsetEl.value = d.subsetSize || sizes[0];
        }
        setVal("cfg-epochs", d.epochs ?? 2);
        setVal("cfg-lr", d.learningRate ?? 0.0001);
        setVal("cfg-batch", d.batchSize ?? 8);
        setVal("cfg-val-ratio", d.valRatio ?? 0.2);
        setVal("cfg-patience", d.earlyStoppingPatience ?? 0);
        setVal("cfg-workers", d.numWorkers ?? 0);
        setVal("cfg-seed", d.randomSeed ?? 42);
        document.getElementById("cfg-freeze").checked = d.freezeBackbone !== false;
        document.getElementById("cfg-unfreeze-block").checked = !!d.unfreezeLastBlock;
    }

    function setVal(id, v) {
        const el = document.getElementById(id);
        if (el) el.value = v;
    }

    function renderDatasets() {
        const sel = document.getElementById("ftc-dataset-select");
        if (!sel) return;
        const ds = state.dashboard.storedDatasets || [];
        sel.innerHTML = ds.map(d =>
            `<option value="${esc(d.path)}" data-id="${esc(d.id)}">${esc(d.name)} (${esc(d.kind)})</option>`
        ).join("");
    }

    function renderDatasetReport(report) {
        const el = document.getElementById("ftc-dataset-report");
        if (!el || !report) return;
        const warns = (report.warnings || []).concat(report.errors || []);
        el.innerHTML = `
            <h3 class="ftc-panel-title">Dataset report</h3>
            <p class="${report.ready ? "ftc-dataset-ok" : "text-danger"} mb-2">
                ${report.ready ? "✓ Ready for training" : "✗ Validation failed"}
            </p>
            <dl class="ftc-model-metrics">
                <dt>Name</dt><dd>${esc(report.datasetName)}</dd>
                <dt>Path</dt><dd class="text-truncate" title="${esc(report.datasetPath)}">${esc(shortPath(report.datasetPath))}</dd>
                <dt>Size</dt><dd>${fmtBytes(report.datasetSizeBytes)}</dd>
                <dt>Total rows</dt><dd>${report.totalRows ?? "—"}</dd>
                <dt>Valid rows</dt><dd>${report.validRows ?? "—"}</dd>
                <dt>Patients (est.)</dt><dd>${report.estimatedPatients ?? "—"}</dd>
                <dt>Normal</dt><dd>${report.normalImages ?? "—"}</dd>
                <dt>Positive</dt><dd>${report.positiveImages ?? "—"}</dd>
                <dt>Val split</dt><dd>${report.valRatio != null ? (report.valRatio * 100).toFixed(0) + "%" : "20%"}</dd>
                <dt>Missing images</dt><dd>${report.missingImages ?? "—"}</dd>
            </dl>
            ${warns.length ? `<ul class="ftc-warn-list">${warns.map(w => `<li>${esc(w)}</li>`).join("")}</ul>` : ""}
            ${report.diseaseDistribution ? renderDiseaseDist(report.diseaseDistribution) : ""}`;
    }

    function renderDiseaseDist(dist) {
        const entries = Object.entries(dist).slice(0, 8);
        return `<div class="mt-2 small"><strong>Disease distribution</strong><br>${entries.map(([k, v]) => `${esc(k)}: ${v}`).join(" · ")}</div>`;
    }

    function buildRequest() {
        const dsSel = document.getElementById("ftc-dataset-select");
        return {
            modelId: state.selectedModelId,
            datasetPath: dsSel?.value || "",
            datasetName: dsSel?.selectedOptions[0]?.textContent || "",
            subsetSize: parseInt(document.getElementById("cfg-subset-size")?.value || "100", 10),
            epochs: parseInt(document.getElementById("cfg-epochs")?.value || "2", 10),
            learningRate: parseFloat(document.getElementById("cfg-lr")?.value || "0.0001"),
            batchSize: parseInt(document.getElementById("cfg-batch")?.value || "8", 10),
            valRatio: parseFloat(document.getElementById("cfg-val-ratio")?.value || "0.2"),
            earlyStoppingPatience: parseInt(document.getElementById("cfg-patience")?.value || "0", 10) || null,
            unfreezeLastBlock: document.getElementById("cfg-unfreeze-block")?.checked || false,
            freezeBackbone: document.getElementById("cfg-freeze")?.checked !== false,
            resumeCheckpoint: document.getElementById("cfg-resume")?.value || null,
            finetuneCheckpoint: document.getElementById("cfg-finetune")?.value || null,
            numWorkers: parseInt(document.getElementById("cfg-workers")?.value || "0", 10),
            randomSeed: parseInt(document.getElementById("cfg-seed")?.value || "42", 10),
            prepareSubset: true,
        };
    }

    function updateSelectedLabel() {
        const m = getSelectedModel();
        const el = document.getElementById("ftc-selected-model-label");
        if (el) el.textContent = m ? `Selected: ${m.displayName} (${m.pipelineKind})` : "Select a model above.";
    }

    function renderMonitor(m) {
        const panel = document.getElementById("ftc-monitor-panel");
        const badge = document.getElementById("ftc-monitor-badge");
        if (!panel) return;

        if (!m) {
            panel.innerHTML = `<p class="text-muted small">No active training job.</p>`;
            if (badge) { badge.textContent = "Idle"; badge.className = "ftc-badge ftc-badge-idle ftc-badge-live"; }
            return;
        }

        if (badge) {
            badge.textContent = m.status || "Running";
            badge.className = "ftc-badge ftc-badge-running ftc-badge-live";
        }

        const pctDone = m.progressPercent != null ? m.progressPercent.toFixed(0) : "0";
        panel.innerHTML = `
            <div class="d-flex justify-content-between align-items-center flex-wrap gap-2 mb-2">
                <div>${statusBadge(m.status)} <span class="small text-muted">Job ${m.jobId}</span></div>
                <div class="d-flex gap-2">
                    ${m.canCancel ? `<button type="button" class="btn btn-sm btn-outline-danger" id="ftc-cancel-btn">Cancel</button>` : ""}
                    <button type="button" class="btn btn-sm btn-outline-secondary" disabled title="Future">Pause</button>
                    <button type="button" class="btn btn-sm btn-outline-secondary" disabled title="Future">Resume</button>
                </div>
            </div>
            <div class="ftc-progress-wrap">
                <div class="ftc-progress-label"><span>Progress</span><span>${pctDone}%</span></div>
                <div class="progress" style="height:8px"><div class="progress-bar progress-bar-striped progress-bar-animated" style="width:${pctDone}%"></div></div>
            </div>
            <div class="ftc-monitor-grid">
                ${stat("Epoch", (m.currentEpoch ?? "—") + " / " + (m.totalEpochs ?? "—"))}
                ${stat("Batch", m.currentBatch || "—")}
                ${stat("Train loss", fmtNum(m.trainLoss))}
                ${stat("Val loss", fmtNum(m.valLoss))}
                ${stat("Accuracy", pct(m.accuracy))}
                ${stat("F1", pct(m.f1))}
                ${stat("ROC-AUC", pct(m.rocAuc))}
                ${stat("LR", fmtNum(m.learningRate))}
                ${stat("Elapsed", fmtDuration(m.elapsedSeconds))}
                ${stat("ETA", fmtDuration(m.etaSeconds))}
            </div>
            ${m.logTail ? `<pre class="ftc-log-tail">${esc(m.logTail)}</pre>` : ""}`;

        document.getElementById("ftc-cancel-btn")?.addEventListener("click", async () => {
            if (!confirm("Cancel training?")) return;
            await apiPost("/cancel/" + m.jobId, {});
            await refreshDashboard();
        });
    }

    function stat(label, value) {
        return `<div class="ftc-monitor-stat"><div class="label">${label}</div><div class="value">${value}</div></div>`;
    }

    function fmtNum(v) {
        if (v == null) return "—";
        return Number(v).toFixed(4);
    }

    function renderCheckpoints() {
        const tbody = document.querySelector("#ftc-checkpoints-table tbody");
        if (!tbody) return;
        let cps = state.dashboard.checkpoints || [];
        if (state.selectedModelId)
            cps = cps.filter(c => c.modelId === state.selectedModelId);

        tbody.innerHTML = cps.map(c => {
            const prod = c.isProduction ? " ftc-badge-production" : "";
            const flags = `${c.isFavorite ? "⭐" : ""}${c.isPinned ? "📌" : ""}${c.isProductionCandidate ? "🎯" : ""}${c.isRecommended ? "✓" : ""}`;
            return `<tr data-id="${esc(c.id)}">
                <td><input type="checkbox" class="ftc-cp-compare" value="${esc(c.id)}" ${state.compareIds.has(c.id) ? "checked" : ""} /></td>
                <td><code>${esc(c.version)}</code> <span class="small">${flags}</span></td>
                <td>${esc(c.modelId)}</td>
                <td>${fmtDate(c.trainingDate)}</td>
                <td>${esc(c.dataset)}</td>
                <td>${c.subsetSize ?? "—"}</td>
                <td>${pct(c.accuracy)}</td>
                <td>${pct(c.f1)}</td>
                <td>${pct(c.rocAuc)}</td>
                <td>${fmtNum(c.valLoss)}</td>
                <td><span class="ftc-badge${prod}">${esc(c.status)}</span></td>
                <td class="text-nowrap">
                    <a class="btn btn-sm btn-link p-0" href="${API}/download/${encodeURIComponent(c.id)}">DL</a>
                    <button type="button" class="btn btn-sm btn-link p-0 ftc-cp-detail" data-id="${esc(c.id)}">Details</button>
                    <button type="button" class="btn btn-sm btn-link p-0 ftc-cp-deploy" data-id="${esc(c.id)}" data-model="${esc(c.modelId)}">Deploy</button>
                    <button type="button" class="btn btn-sm btn-link p-0 text-danger ftc-cp-delete" data-id="${esc(c.id)}">Del</button>
                </td>
            </tr>`;
        }).join("");

        tbody.querySelectorAll(".ftc-cp-compare").forEach(cb => {
            cb.addEventListener("change", () => {
                if (cb.checked) state.compareIds.add(cb.value);
                else state.compareIds.delete(cb.value);
            });
        });
        tbody.querySelectorAll(".ftc-cp-deploy").forEach(btn => {
            btn.addEventListener("click", () => selectDeploy(btn.dataset.model, btn.dataset.id));
        });
        tbody.querySelectorAll(".ftc-cp-delete").forEach(btn => {
            btn.addEventListener("click", () => deleteCheckpoint(btn.dataset.id));
        });
        tbody.querySelectorAll(".ftc-cp-detail").forEach(btn => {
            btn.addEventListener("click", () => {
                if (window.ftcOpenCheckpointDetails) window.ftcOpenCheckpointDetails(btn.dataset.id);
            });
        });
    }

    async function selectDeploy(modelId, checkpointId) {
        state.selectedCheckpointId = checkpointId;
        state.selectedModelId = modelId;
        const preview = await apiGet(`/deploy-preview?modelId=${encodeURIComponent(modelId)}&checkpointId=${encodeURIComponent(checkpointId)}`);
        renderDeployPreview(preview);
        document.getElementById("ftc-deploy-btn").disabled = false;
    }

    function renderDeployPreview(preview) {
        const cur = document.getElementById("ftc-deploy-current");
        const cand = document.getElementById("ftc-deploy-candidate");
        if (cur) cur.innerHTML = `<h3 class="ftc-panel-title">Current production</h3>${checkpointHtml(preview.currentProduction)}`;
        if (cand) cand.innerHTML = `<h3 class="ftc-panel-title">Candidate</h3>${checkpointHtml(preview.candidate)}`;
    }

    function checkpointHtml(c) {
        if (!c) return `<p class="text-muted small">None</p>`;
        return `<dl class="ftc-model-metrics">
            <dt>Version</dt><dd>${esc(c.version)}</dd>
            <dt>F1</dt><dd>${pct(c.f1)}</dd>
            <dt>Accuracy</dt><dd>${pct(c.accuracy)}</dd>
            <dt>ROC-AUC</dt><dd>${pct(c.rocAuc)}</dd>
        </dl>`;
    }

    async function deleteCheckpoint(id) {
        if (!confirm("Delete checkpoint " + id + "? This cannot be undone.")) return;
        await apiDelete("/checkpoint/" + encodeURIComponent(id));
        await refreshDashboard();
    }

    function renderHistory() {
        const el = document.getElementById("ftc-history-timeline");
        if (!el) return;
        const rows = state.dashboard.history || [];
        el.innerHTML = rows.map(h => {
            const cls = (h.status || "").toLowerCase().includes("fail") ? "failed"
                : (h.status || "").toLowerCase().includes("complet") ? "completed" : "";
            return `<div class="ftc-timeline-item ${cls}">
                <strong>${esc(h.modelId)}</strong> · ${esc(h.status)}
                <div class="small text-muted">${fmtDate(h.startedAt)} → ${h.finishedAt ? fmtDate(h.finishedAt) : "—"} (${esc(h.duration)})</div>
                <div class="small">Dataset: ${esc(h.dataset)} · ${esc(h.hyperparameters)}</div>
                ${h.failureReason ? `<div class="small text-danger">${esc(h.failureReason)}</div>` : ""}
                ${h.checkpointPath ? `<div class="small"><code>${esc(h.checkpointPath)}</code></div>` : ""}
            </div>`;
        }).join("") || `<p class="text-muted small">No training history yet.</p>`;
    }

    function renderSystem() {
        const el = document.getElementById("ftc-system-grid");
        const s = state.dashboard.system || {};
        if (!el) return;
        const cards = [
            ["Disk free", (s.diskFreeGb?.toFixed(1) ?? "—") + " GB"],
            ["Dataset storage", (s.datasetStorageGb?.toFixed(2) ?? "—") + " GB"],
            ["Checkpoints", s.checkpointCount ?? "—"],
            ["RAM (proc)", (s.ramUsedGb?.toFixed(2) ?? "—") + " GB"],
            ["Python", s.pythonVersion || "—"],
            ["PyTorch", s.torchVersion || "—"],
            ["CUDA", s.cudaAvailable ? "Yes" : "No"],
        ];
        el.innerHTML = cards.map(([lbl, val]) =>
            `<div class="col-6 col-md-4 col-lg-3"><div class="ftc-system-card"><div class="val">${val}</div><div class="lbl">${lbl}</div></div></div>`
        ).join("");
    }

    function renderCharts(data) {
        if (!data || typeof Chart === "undefined") return;
        drawChart("chart-train-loss", "Train Loss", data.trainLoss || [], "#0ea5e9");
        drawChart("chart-val-loss", "Validation Loss", data.valLoss || [], "#f59e0b");
        drawChart("chart-f1", "F1 Score", data.f1 || [], "#22c55e");
        drawChart("chart-roc", "ROC-AUC", data.rocAuc || [], "#a855f7");
    }

    function drawChart(canvasId, label, points, color) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;
        if (state.charts[canvasId]) state.charts[canvasId].destroy();
        state.charts[canvasId] = new Chart(canvas, {
            type: "line",
            data: {
                labels: points.map(p => "Ep " + p.epoch),
                datasets: [{
                    label,
                    data: points.map(p => p.value),
                    borderColor: color,
                    backgroundColor: color + "33",
                    tension: 0.25,
                    fill: true,
                }],
            },
            options: {
                responsive: true,
                plugins: { legend: { display: false } },
                scales: { y: { beginAtZero: false } },
            },
        });
    }

    async function refreshDashboard() {
        if (refreshDashboard._inFlight) return refreshDashboard._inFlight;
        refreshDashboard._inFlight = (async () => {
            try {
                state.dashboard = await apiGet("/dashboard");
                if (state.dashboard.activeJob) {
                    state.activeJobId = state.dashboard.activeJob.jobId;
                    renderMonitor(state.dashboard.activeJob);
                }
                renderModels();
                renderDatasets();
                renderCheckpoints();
                renderHistory();
                renderSystem();
                renderQueue();
                renderPresets();
                renderArchive();
                renderExperiments();
                renderDeploymentHistory();
                renderVersionTimeline();
                renderNotifications();
                renderRecommendation();
                renderLiveResources(state.dashboard.liveResources);
            } catch (e) {
                if (e.name !== "AbortError") console.warn("Dashboard refresh failed:", e);
            } finally {
                refreshDashboard._inFlight = null;
            }
        })();
        return refreshDashboard._inFlight;
    }

    function renderQueue() {
        const tbody = document.querySelector("#ftc-queue-table tbody");
        if (!tbody) return;
        const q = state.dashboard.queue || [];
        tbody.innerHTML = q.map(item => `<tr>
            <td>${item.queuePosition || "—"}</td>
            <td>${esc(item.modelName)}</td>
            <td class="text-truncate" style="max-width:120px">${esc(shortPath(item.dataset))}</td>
            <td>${esc(item.requestedBy || "—")}</td>
            <td>${fmtDate(item.requestedAt)}</td>
            <td>${statusBadge(item.status)}</td>
            <td>${item.canCancel ? `<button type="button" class="btn btn-sm btn-link text-danger ftc-queue-cancel" data-id="${item.jobId}">Cancel</button>` : ""}</td>
        </tr>`).join("") || `<tr><td colspan="7" class="text-muted small">Queue empty.</td></tr>`;
        tbody.querySelectorAll(".ftc-queue-cancel").forEach(btn => {
            btn.addEventListener("click", async () => {
                await apiPost("/queue/cancel/" + btn.dataset.id, {});
                await refreshDashboard();
            });
        });
    }

    function renderPresets() {
        const el = document.getElementById("ftc-preset-cards");
        if (!el) return;
        const presets = state.dashboard.presets || [];
        el.innerHTML = presets.map(p => `<div class="col-md-6 col-lg-3">
            <div class="ftc-preset-card${p.isCustom ? "" : ""}" data-preset="${esc(p.id)}">
                <strong>${esc(p.name)}</strong>
                <div class="small text-muted">${esc(p.description)}</div>
            </div>
        </div>`).join("");
        el.querySelectorAll(".ftc-preset-card").forEach(card => {
            card.addEventListener("click", () => {
                const p = presets.find(x => x.id === card.dataset.preset);
                if (!p || p.isCustom) return;
                el.querySelectorAll(".ftc-preset-card").forEach(c => c.classList.remove("selected"));
                card.classList.add("selected");
                if (p.subsetSize) document.getElementById("cfg-subset-size").value = p.subsetSize;
                setVal("cfg-epochs", p.epochs);
                setVal("cfg-lr", p.learningRate);
                setVal("cfg-batch", p.batchSize);
                setVal("cfg-val-ratio", p.valRatio);
                setVal("cfg-patience", p.earlyStoppingPatience);
                setVal("cfg-workers", p.numWorkers);
                document.getElementById("cfg-freeze").checked = p.freezeBackbone !== false;
                document.getElementById("cfg-unfreeze-block").checked = !!p.unfreezeLastBlock;
            });
        });
    }

    function renderArchive() {
        const tbody = document.querySelector("#ftc-archive-table tbody");
        if (!tbody) return;
        const rows = state.dashboard.datasetArchive || [];
        tbody.innerHTML = rows.map(d => `<tr>
            <td>${esc(d.name)}</td>
            <td>${fmtDate(d.uploadDate)}</td>
            <td>${fmtBytes(d.sizeBytes)}</td>
            <td>${d.imageCount || "—"}</td>
            <td>${d.patientCount || "—"}</td>
            <td>${d.trainingRuns || 0}</td>
            <td>
                <button type="button" class="btn btn-sm btn-link p-0 ftc-reuse-ds" data-path="${esc(d.path)}">Reuse</button>
                ${d.id ? `<button type="button" class="btn btn-sm btn-link p-0 text-danger ftc-del-ds" data-id="${d.id}">Delete</button>` : ""}
            </td>
        </tr>`).join("") || `<tr><td colspan="7" class="text-muted small">No archived datasets.</td></tr>`;
        tbody.querySelectorAll(".ftc-reuse-ds").forEach(btn => {
            btn.addEventListener("click", () => {
                const sel = document.getElementById("ftc-dataset-select");
                if (sel) sel.value = btn.dataset.path;
            });
        });
        tbody.querySelectorAll(".ftc-del-ds").forEach(btn => {
            btn.addEventListener("click", async () => {
                if (!confirm("Delete archive entry?")) return;
                await apiDelete("/dataset-archive/" + btn.dataset.id);
                await refreshDashboard();
            });
        });
    }

    function renderExperiments() {
        const tbody = document.querySelector("#ftc-experiments-table tbody");
        if (!tbody) return;
        const ex = state.dashboard.experiments || [];
        tbody.innerHTML = ex.map(e => `<tr>
            <td><input type="checkbox" class="ftc-exp-pick" value="${e.jobId}" /></td>
            <td>${esc(e.experimentName)}</td>
            <td>${esc(e.modelId)}</td>
            <td>${e.epochs}</td>
            <td>${e.learningRate}</td>
            <td>${pct(e.f1)}</td>
            <td>${pct(e.rocAuc)}</td>
            <td>${fmtNum(e.valLoss)}</td>
        </tr>`).join("") || `<tr><td colspan="8" class="text-muted small">No experiments yet.</td></tr>`;
    }

    function renderDeploymentHistory() {
        const tbody = document.querySelector("#ftc-deployment-history-table tbody");
        if (!tbody) return;
        const rows = state.dashboard.deploymentHistory || [];
        tbody.innerHTML = rows.map(r => `<tr>
            <td>${esc(r.action)}</td>
            <td>${esc(r.fromVersion || "—")}</td>
            <td><code>${esc(r.toVersion)}</code></td>
            <td>${fmtDate(r.deployedAt)}</td>
            <td>${esc(r.reason || "—")}</td>
            <td>${r.isCurrentProduction ? "✓" : ""}</td>
        </tr>`).join("") || `<tr><td colspan="6" class="text-muted small">No deployment history.</td></tr>`;
    }

    function renderVersionTimeline() {
        const el = document.getElementById("ftc-version-timeline");
        if (!el) return;
        const modelFilter = document.getElementById("ftc-timeline-model-filter")?.value || "";
        const statusFilter = document.getElementById("ftc-timeline-status-filter")?.value || "";
        let rows = state.dashboard.versionTimeline || [];
        if (modelFilter) rows = rows.filter(r => r.modelId === modelFilter);
        if (statusFilter) rows = rows.filter(r => r.deploymentStatus === statusFilter);
        el.innerHTML = `<div class="ftc-timeline">${rows.map(r => `<div class="ftc-timeline-item completed">
            <strong>${esc(r.versionNumber)}</strong> · ${esc(r.modelId)} · ${statusBadge(r.deploymentStatus)}
            <div class="small text-muted">${fmtDate(r.trainingDate)} · ${esc(r.dataset)} · Ep ${r.epochs ?? "—"}</div>
            <div class="small">Acc ${pct(r.accuracy)} · F1 ${pct(r.f1)} · ROC ${pct(r.rocAuc)}</div>
        </div>`).join("")}</div>` || `<p class="text-muted small">No versions.</p>`;
    }

    function renderNotifications() {
        const el = document.getElementById("ftc-notifications-list");
        const badge = document.getElementById("ftc-notif-badge");
        const notes = state.dashboard.notifications || [];
        if (badge) badge.textContent = state.dashboard.unreadNotificationCount || 0;
        if (!el) return;
        el.innerHTML = notes.map(n => `<div class="ftc-notification-item${n.isRead ? "" : " unread"}" data-id="${n.id}">
            <div>${n.severity === "danger" ? "⚠" : n.severity === "success" ? "✓" : "ℹ"}</div>
            <div class="flex-grow-1">
                <div class="small text-muted">${fmtDate(n.createdAt)} · ${esc(n.modelName)}</div>
                <div>${esc(n.message)}</div>
            </div>
            ${!n.isRead ? `<button type="button" class="btn btn-sm btn-link ftc-notif-read" data-id="${n.id}">Read</button>` : ""}
        </div>`).join("") || `<p class="text-muted small">No notifications.</p>`;
        el.querySelectorAll(".ftc-notif-read").forEach(btn => {
            btn.addEventListener("click", async () => {
                await apiPost("/notifications/read/" + btn.dataset.id, {});
                await refreshDashboard();
            });
        });
    }

    function renderRecommendation() {
        const el = document.getElementById("ftc-recommendation-panel");
        const rec = state.dashboard.latestRecommendation;
        if (!el) return;
        if (!rec) { el.innerHTML = `<p class="text-muted small">Complete a training run to receive recommendations.</p>`; return; }
        el.innerHTML = `<div class="ftc-recommendation-card">
            <h4 class="h6">${esc(rec.recommendation)}</h4>
            <p class="small mb-2">${esc(rec.explanation)}</p>
            <dl class="ftc-model-metrics">
                <dt>Δ Accuracy</dt><dd>${rec.accuracyDelta != null ? pct(rec.accuracyDelta) : "—"}</dd>
                <dt>Δ F1</dt><dd>${rec.f1Delta != null ? pct(rec.f1Delta) : "—"}</dd>
                <dt>Δ ROC-AUC</dt><dd>${rec.rocAucDelta != null ? pct(rec.rocAucDelta) : "—"}</dd>
            </dl>
        </div>`;
    }

    function renderLiveResources(lr) {
        if (!lr) return;
        const stats = document.getElementById("ftc-live-resource-stats");
        if (stats) {
            stats.innerHTML = [
                ["CPU", lr.cpuUsagePercent?.toFixed(1) + "%"],
                ["RAM", (lr.ramUsagePercent?.toFixed(1) ?? "—") + (lr.ramUsedGb != null ? "% · " + lr.ramUsedGb.toFixed(1) + " GB" : "")],
                ["VRAM", lr.vramUsagePercent != null ? lr.vramUsagePercent.toFixed(1) + "%" : "N/A"],
                ["Disk", lr.diskUsedPercent?.toFixed(1) + "%" + (lr.diskUsedGb != null ? " · " + lr.diskUsedGb.toFixed(1) + " GB" : "")],
                ["GPU", lr.gpuUsagePercent != null ? lr.gpuUsagePercent.toFixed(1) + "%" : "N/A"],
                ["Speed", lr.trainingSpeedImagesPerSec != null ? lr.trainingSpeedImagesPerSec.toFixed(1) + " img/s" : "—"],
                ["ETA", fmtDuration(lr.estimatedRemainingSeconds)],
                ["Epoch", lr.currentEpoch ?? "—"],
                ["Batch", lr.currentBatch ?? "—"],
                ["Temp", lr.gpuTemperatureC != null ? lr.gpuTemperatureC.toFixed(0) + "°C" : "N/A"],
            ].map(([l, v]) => `<div class="col-6 col-md-4 col-lg-2"><div class="ftc-system-card"><div class="val">${v}</div><div class="lbl">${l}</div></div></div>`).join("");
        }
        drawLiveChart("chart-cpu-live", "CPU %", lr.cpuHistory || [], "#0ea5e9");
        drawLiveChart("chart-ram-live", "RAM GB", lr.ramHistory || [], "#22c55e");
        drawLiveChart("chart-gpu-live", "GPU %", lr.gpuHistory || [], "#a855f7");
    }

    function drawLiveChart(id, label, points, color) {
        const canvas = document.getElementById(id);
        if (!canvas || typeof Chart === "undefined") return;
        if (state.resourceCharts[id]) state.resourceCharts[id].destroy();
        state.resourceCharts[id] = new Chart(canvas, {
            type: "line",
            data: { labels: points.map((_, i) => i + 1), datasets: [{ label, data: points.map(p => p.value), borderColor: color, tension: 0.3, fill: false }] },
            options: { responsive: true, plugins: { legend: { display: false } }, animation: { duration: 400 } },
        });
    }

    async function pollMonitor() {
        try {
            const res = await apiGet("/monitor/active");
            if (res.active && res.monitor) {
                state.activeJobId = res.monitor.jobId;
                renderMonitor(res.monitor);
                if (res.monitor.status === "Completed" || res.monitor.status === "Failed" || res.monitor.status === "Cancelled") {
                    stopPoll();
                    if (state.activeJobId) {
                        const charts = await apiGet("/charts/" + state.activeJobId);
                        renderCharts(charts);
                        document.getElementById("ftc-collapse-charts")?.classList.add("show");
                    }
                    await refreshDashboard();
                }
            } else if (state.activeJobId) {
                renderMonitor(null);
                stopPoll();
                await refreshDashboard();
            }
            try {
                const lr = await apiGet("/live-resources");
                renderLiveResources(lr);
            } catch { }
        } catch (e) {
            console.warn("Monitor poll failed", e);
        }
    }

    function startPoll() {
        stopPoll();
        state.pollTimer = setInterval(pollMonitor, state.refreshSec * 1000);
    }

    function stopPoll() {
        if (state.pollTimer) clearInterval(state.pollTimer);
        state.pollTimer = null;
    }

    function wireEvents() {
        document.getElementById("ftc-validate-btn")?.addEventListener("click", async () => {
            const fd = new FormData();
            fd.append("modelId", state.selectedModelId || "");
            fd.append("datasetPath", document.getElementById("ftc-dataset-select")?.value || "");
            fd.append("__RequestVerificationToken", token());
            try {
                const report = await apiPost("/validate-dataset", fd, true);
                renderDatasetReport(report);
            } catch (e) {
                alert(e.message);
            }
        });

        document.getElementById("ftc-upload-btn")?.addEventListener("click", async () => {
            const file = document.getElementById("ftc-dataset-zip")?.files[0];
            if (!file) { alert("Choose a ZIP file."); return; }
            const fd = new FormData();
            fd.append("zipFile", file);
            fd.append("__RequestVerificationToken", token());
            try {
                const uploaded = await apiPost("/upload-dataset", fd, true);
                await refreshDashboard();
                const sel = document.getElementById("ftc-dataset-select");
                if (sel) sel.value = uploaded.path;
            } catch (e) {
                alert(e.message);
            }
        });

        document.getElementById("ftc-start-btn")?.addEventListener("click", async () => {
            if (!state.selectedModelId) { alert("Select a model first."); return; }
            try {
                const req = buildRequest();
                const conf = await apiPost("/confirm-training", req);
                const body = document.getElementById("ftc-confirm-body");
                body.innerHTML = `<dl class="ftc-model-metrics">
                    <dt>Model</dt><dd>${esc(conf.model)}</dd>
                    <dt>Dataset</dt><dd>${esc(conf.dataset)}</dd>
                    <dt>Subset</dt><dd>${conf.subsetSize}</dd>
                    <dt>Epochs</dt><dd>${conf.epochs}</dd>
                    <dt>Learning rate</dt><dd>${conf.learningRate}</dd>
                    <dt>Checkpoint</dt><dd>${esc(conf.checkpointSource)}</dd>
                    <dt>Est. duration</dt><dd>${esc(conf.estimatedDuration)}</dd>
                    <dt>Est. RAM</dt><dd>${esc(conf.estimatedRam)}</dd>
                </dl>`;
                const modal = new bootstrap.Modal(document.getElementById("ftc-confirm-modal"));
                modal.show();
                document.getElementById("ftc-confirm-start").onclick = async () => {
                    modal.hide();
                    const result = await apiPost("/start-training", req);
                    if (result.jobId) {
                        state.activeJobId = result.jobId;
                        startPoll();
                        await pollMonitor();
                    } else {
                        alert(result.error || "Training started via HITL orchestrator.");
                        await refreshDashboard();
                    }
                };
            } catch (e) {
                alert(e.message);
            }
        });

        document.getElementById("ftc-run-compare")?.addEventListener("click", async () => {
            const ids = [...state.compareIds];
            if (ids.length < 2) { alert("Select at least 2 checkpoints."); return; }
            const rows = await apiPost("/compare", { checkpointIds: ids });
            const el = document.getElementById("ftc-compare-table");
            if (!el) return;
            let html = `<table class="table table-sm ftc-table"><thead><tr><th>Metric</th>${ids.map(id => `<th>${esc(id.slice(0, 12))}</th>`).join("")}</tr></thead><tbody>`;
            rows.forEach(r => {
                html += `<tr><td>${esc(r.metric)}</td>`;
                ids.forEach(id => {
                    const val = r.values[id] ?? "—";
                    const best = r.bestCheckpointId === id ? " best-cell" : "";
                    html += `<td class="${best}">${esc(val)}</td>`;
                });
                html += "</tr>";
            });
            el.innerHTML = html + "</tbody></table>";
        });

        document.getElementById("ftc-deploy-btn")?.addEventListener("click", async () => {
            if (!state.selectedCheckpointId || !state.selectedModelId) return;
            if (!confirm("Deploy this checkpoint to production registry?")) return;
            try {
                const res = await apiPost("/deploy", {
                    modelId: state.selectedModelId,
                    checkpointId: state.selectedCheckpointId,
                });
                alert(res.message || "Deployed.");
                await refreshDashboard();
            } catch (e) {
                alert(e.message);
            }
        });

        document.getElementById("ftc-rollback-btn")?.addEventListener("click", async () => {
            if (!state.selectedModelId) { alert("Select a model."); return; }
            if (!confirm("Rollback to previous deployable version?")) return;
            try {
                const fd = { modelId: state.selectedModelId, __RequestVerificationToken: token() };
                const res = await apiPost("/rollback", fd, true);
                alert(res.message || "Rolled back.");
                await refreshDashboard();
            } catch (e) {
                alert(e.message);
            }
        });

        document.getElementById("ftc-deploy-best")?.addEventListener("click", async () => {
            if (!state.selectedModelId) { alert("Select a model."); return; }
            const metric = document.getElementById("ftc-deploy-metric")?.value || "ROC-AUC";
            if (!confirm("Deploy best checkpoint by " + metric + "?")) return;
            try {
                const res = await apiPost("/deploy-best", { modelId: state.selectedModelId, metric });
                alert(res.message || "Deployed.");
                await refreshDashboard();
            } catch (e) { alert(e.message); }
        });

        document.getElementById("ftc-compare-experiments")?.addEventListener("click", async () => {
            const ids = [...document.querySelectorAll(".ftc-exp-pick:checked")].map(cb => cb.value);
            if (ids.length < 2) { alert("Select at least 2 experiments."); return; }
            const res = await apiPost("/experiments/compare", ids);
            const el = document.getElementById("ftc-experiment-compare");
            if (el) el.innerHTML = `<p class="small">${esc(res.summary)}</p>` + (res.metricComparison || []).map(r =>
                `<div class="small"><strong>${esc(r.metric)}</strong></div>`).join("");
        });

        document.getElementById("ftc-infer-run")?.addEventListener("click", async () => {
            const file = document.getElementById("ftc-infer-image")?.files[0];
            if (!file) { alert("Choose an image."); return; }
            const fd = new FormData();
            fd.append("image", file);
            fd.append("source", document.getElementById("ftc-infer-mode")?.value || "compare");
            fd.append("candidateCheckpointPath", document.getElementById("ftc-infer-checkpoint")?.value || "");
            fd.append("__RequestVerificationToken", token());
            try {
                const res = await apiPost("/inference-test", fd, true);
                renderInferResults(res);
            } catch (e) { alert(e.message); }
        });

        document.getElementById("ftc-notif-read-all")?.addEventListener("click", async () => {
            await apiPost("/notifications/read-all", {});
            await refreshDashboard();
        });
        document.getElementById("ftc-notif-clear")?.addEventListener("click", async () => {
            if (!confirm("Clear all notifications?")) return;
            await apiPost("/notifications/clear", {});
            await refreshDashboard();
        });

        document.getElementById("ftc-timeline-model-filter")?.addEventListener("change", renderVersionTimeline);
        document.getElementById("ftc-timeline-status-filter")?.addEventListener("change", renderVersionTimeline);
    }

    function renderInferResults(res) {
        const el = document.getElementById("ftc-infer-results");
        if (!el) return;
        const block = (r, title) => {
            if (!r) return "";
            if (r.error) return `<div class="col-md-6"><div class="ftc-panel"><h4>${title}</h4><p class="text-danger small">${esc(r.error)}</p></div></div>`;
            const preds = (r.predictions || []).slice(0, 5).map(p => `<li>${esc(p.label)}: ${(p.confidence * 100).toFixed(1)}%</li>`).join("");
            return `<div class="col-md-6"><div class="ftc-panel"><h4>${title}</h4>
                <p><strong>${esc(r.label)}</strong> · ${r.inferenceTimeMs?.toFixed(0)} ms</p>
                <ul class="small mb-0">${preds}</ul>
                ${r.heatmapBase64 ? `<img class="img-fluid mt-2" src="data:image/png;base64,${r.heatmapBase64}" alt="heatmap" />` : ""}
            </div></div>`;
        };
        el.innerHTML = block(res.production, "Production") + block(res.candidate, "Candidate");
    }

    function init() {
        renderModels();
        renderDatasets();
        renderCheckpoints();
        renderHistory();
        renderSystem();
        renderQueue();
        renderPresets();
        renderArchive();
        renderExperiments();
        renderDeploymentHistory();
        renderVersionTimeline();
        renderNotifications();
        renderRecommendation();
        renderLiveResources(state.dashboard.liveResources);
        const modelFilter = document.getElementById("ftc-timeline-model-filter");
        if (modelFilter && state.dashboard.models) {
            modelFilter.innerHTML = `<option value="">All models</option>` +
                state.dashboard.models.map(m => `<option value="${esc(m.modelId)}">${esc(m.displayName)}</option>`).join("");
        }
        if (state.dashboard.activeJob) {
            renderMonitor(state.dashboard.activeJob);
            startPoll();
        }
        state.resourceTimer = setInterval(async () => {
            try { renderLiveResources(await apiGet("/live-resources")); } catch { }
        }, state.refreshSec * 1000);
        wireEvents();
    }

    if (document.readyState === "loading")
        document.addEventListener("DOMContentLoaded", init);
    else
        init();

    window.FTC = {
        getState: () => state,
        apiGet,
        apiPost,
        apiDelete,
        refreshDashboard,
        esc,
        fmtDate,
        fmtBytes,
        pct,
        token,
        renderCheckpoints,
        renderRecommendation,
        renderLiveResources,
        startPoll,
    };
})();
