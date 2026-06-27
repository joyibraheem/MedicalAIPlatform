(function () {
    "use strict";

    const API = "/Admin/TrainingCenter";
    const extCharts = {};
    let wizardValidated = false;
    let wizardStarted = false;
    let wizardCompleted = false;
    let detailCheckpointId = null;

    function ftc() { return window.FTC || null; }

    function esc(s) {
        if (s == null) return "";
        return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");
    }

    async function apiGet(path) {
        const core = ftc();
        if (core?.apiGet) return core.apiGet(path);
        const r = await fetch(API + path);
        if (!r.ok) throw new Error(await r.text());
        return r.json();
    }

    async function apiPost(path, body) {
        const core = ftc();
        if (core?.apiPost) return core.apiPost(path, body);
        const r = await fetch(API + path, {
            method: "POST",
            headers: { "Content-Type": "application/json", RequestVerificationToken: document.querySelector('input[name="__RequestVerificationToken"]')?.value || "" },
            body: JSON.stringify(body),
        });
        const json = await r.json().catch(() => ({}));
        if (!r.ok) throw new Error(json.error || r.statusText);
        return json;
    }

    function dashboard() {
        return ftc()?.getState()?.dashboard || window.trainingCenterInitial || {};
    }

    function selectedDatasetPath() {
        return document.getElementById("ftc-dataset-select")?.value || "brax-default";
    }

    function selectedModelId() {
        return ftc()?.getState()?.selectedModelId || dashboard().models?.[0]?.modelId;
    }

    function renderHomeCards(summary) {
        const el = document.getElementById("ftc-home-cards");
        if (!el || !summary) return;
        const cards = [
            ["Models", summary.modelCount],
            ["Datasets", summary.datasetCount],
            ["Running Jobs", summary.runningJobs],
            ["Queued Jobs", summary.queuedJobs],
            ["Finished Jobs", summary.finishedJobs],
            ["Deployable Models", summary.deployableModels],
            ["Production Models", summary.productionModels],
            ["Storage Used", (summary.storageUsedGb ?? 0).toFixed(2) + " GB"],
            ["Latest Training", summary.latestTraining || "—"],
            ["Latest Deployment", summary.latestDeployment || "—"],
        ];
        el.innerHTML = cards.map(([label, value]) =>
            `<div class="col-6 col-md-4 col-xl-2"><div class="ftc-home-card"><div class="ftc-home-card-label">${esc(label)}</div><div class="ftc-home-card-value">${esc(String(value))}</div></div></div>`
        ).join("");
    }

    function renderWorkflow(workflow) {
        const el = document.getElementById("ftc-workflow-rail");
        if (!el || !workflow?.stages) return;
        el.innerHTML = workflow.stages.map((s, i) => {
            const cls = s.status === "completed" ? "completed" : s.status === "current" ? "current" : "";
            const arrow = i < workflow.stages.length - 1 ? `<span class="ftc-workflow-arrow">↓</span>` : "";
            return `<div class="ftc-workflow-step ${cls}"><span>${esc(s.label)}</span>${arrow}</div>`;
        }).join("");
    }

    async function refreshWizard() {
        const modelId = selectedModelId();
        const ds = selectedDatasetPath();
        const deployed = (dashboard().checkpoints || []).some(c => c.isProduction);
        const q = new URLSearchParams({
            modelId: modelId || "",
            datasetPath: ds || "",
            validated: String(wizardValidated),
            started: String(wizardStarted),
            completed: String(wizardCompleted),
            deployed: String(deployed),
        });
        try {
            const wizard = await apiGet("/wizard?" + q.toString());
            const el = document.getElementById("ftc-wizard-steps");
            if (!el) return;
            el.innerHTML = wizard.steps.map(s => {
                const cls = s.status === "completed" ? "completed" : s.status === "current" ? "current" : "";
                return `<div class="ftc-wizard-step ${cls}"><span class="ftc-wizard-num">${s.step}</span><span>${esc(s.title)}</span><span class="ftc-wizard-status">${esc(s.status)}</span></div>`;
            }).join("");
        } catch { }
    }

    function renderPlugins(plugins) {
        const el = document.getElementById("ftc-plugin-registry");
        if (!el || !plugins) return;
        el.innerHTML = plugins.map(p => `<div class="col-md-4"><div class="ftc-panel ftc-plugin-card">
            <strong>${esc(p.displayName)}</strong><div class="small text-muted">${esc(p.pipelineKind)}</div>
            <div class="small mt-1">Training: ${p.hasTrainingPipeline ? "✓" : "—"} · Dataset: ${p.hasDatasetHandler ? "✓" : "—"} · Architecture: ${p.hasArchitecture ? "✓" : "—"}</div>
        </div></div>`).join("");
    }

    async function loadArchitecture() {
        const modelId = selectedModelId();
        const panel = document.getElementById("ftc-architecture-panel");
        if (!panel || !modelId) return;
        try {
            const arch = await apiGet("/architecture/" + encodeURIComponent(modelId));
            panel.innerHTML = `<div class="row g-3">
                <div class="col-md-6"><dl class="ftc-model-metrics">
                    <dt>Model</dt><dd>${esc(arch.modelName)}</dd>
                    <dt>Backbone</dt><dd>${esc(arch.backbone)}</dd>
                    <dt>Classifier</dt><dd>${esc(arch.classifier)}</dd>
                    <dt>Input</dt><dd>${esc(arch.inputSize)}</dd>
                    <dt>Output classes</dt><dd>${arch.outputClasses}</dd>
                    <dt>Trainable params</dt><dd>${arch.trainableParameters?.toLocaleString() ?? "—"}</dd>
                    <dt>Frozen params</dt><dd>${arch.frozenParameters?.toLocaleString() ?? "—"}</dd>
                    <dt>Total params</dt><dd>${arch.totalParameters?.toLocaleString() ?? "—"}</dd>
                    <dt>Strategy</dt><dd>${esc(arch.trainingStrategy)}</dd>
                </dl></div>
                <div class="col-md-6"><div class="ftc-arch-diagram">${(arch.diagramLayers || []).map(l => `<div>${esc(l)}</div>`).join("")}</div></div>
            </div>`;
        } catch (e) {
            panel.innerHTML = `<p class="text-muted small">${esc(e.message)}</p>`;
        }
    }

    async function loadDatasetPreview() {
        const path = selectedDatasetPath();
        const modelId = selectedModelId();
        const panel = document.getElementById("ftc-dataset-preview-panel");
        if (!panel) return;
        try {
            const preview = await apiGet(`/dataset-preview?datasetPath=${encodeURIComponent(path)}&modelId=${encodeURIComponent(modelId || "")}`);
            panel.innerHTML = `<h4 class="ftc-panel-title">${esc(preview.name)} <small class="text-muted">${esc(preview.version)}</small></h4>
                <dl class="ftc-model-metrics">
                    <dt>Size</dt><dd>${(preview.sizeBytes / 1048576).toFixed(1)} MB</dd>
                    <dt>Images</dt><dd>${preview.imageCount}</dd>
                    <dt>Patients</dt><dd>${preview.patientCount}</dd>
                    <dt>Normal</dt><dd>${preview.normalCount}</dd>
                    <dt>Positive</dt><dd>${preview.positiveCount}</dd>
                    <dt>Diseases</dt><dd>${preview.diseaseCount}</dd>
                    <dt>Created</dt><dd>${preview.createdDate ? new Date(preview.createdDate).toLocaleString() : "—"}</dd>
                    <dt>Hash</dt><dd><code class="small">${esc((preview.contentHash || "").slice(0, 16))}…</code></dd>
                </dl>
                <div class="table-responsive mt-2"><table class="table table-sm ftc-table"><thead><tr>
                    <th>Patient</th><th>Study</th><th>Image</th><th>Labels</th><th>View</th></tr></thead><tbody>
                    ${(preview.sampleRows || []).map(r => `<tr><td>${esc(r.patientId)}</td><td>${esc(r.studyId)}</td><td>${esc(r.imageName)}</td><td>${esc(r.labels)}</td><td>${esc(r.viewPosition)}</td></tr>`).join("")}
                </tbody></table></div>`;
        } catch (e) {
            panel.innerHTML = `<p class="text-danger small">${esc(e.message)}</p>`;
        }
    }

    function upsertChart(id, label, labels, data, type) {
        const canvas = document.getElementById(id);
        if (!canvas || typeof Chart === "undefined") return;
        if (extCharts[id]) extCharts[id].destroy();
        extCharts[id] = new Chart(canvas, {
            type: type || "bar",
            data: { labels, datasets: [{ label, data, backgroundColor: "rgba(13,110,253,0.55)" }] },
            options: { responsive: true, plugins: { legend: { display: false } }, scales: { y: { beginAtZero: true } } },
        });
    }

    async function loadDatasetStatistics() {
        const path = selectedDatasetPath();
        const modelId = selectedModelId();
        try {
            const stats = await apiGet(`/dataset-statistics?datasetPath=${encodeURIComponent(path)}&modelId=${encodeURIComponent(modelId || "")}`);
            const cards = document.getElementById("ftc-dataset-stats-cards");
            if (cards) {
                cards.innerHTML = (stats.summaryCards || []).map(c =>
                    `<div class="col-6 col-md-4"><div class="ftc-home-card"><div class="ftc-home-card-label">${esc(c.label)}</div><div class="ftc-home-card-value">${esc(c.value)}</div></div></div>`
                ).join("");
            }
            const dist = stats.diseaseDistribution || {};
            upsertChart("chart-disease-dist", "Cases", Object.keys(dist).slice(0, 10), Object.keys(dist).slice(0, 10).map(k => dist[k]));
            upsertChart("chart-normal-positive", "Count", ["Normal", "Positive"], [stats.normalCount, stats.positiveCount], "doughnut");
            upsertChart("chart-label-freq", "Frequency", (stats.labelFrequency || []).map((_, i) => "L" + (i + 1)), (stats.labelFrequency || []).map(p => p.value));
            upsertChart("chart-train-val", "Split", ["Train", "Validation"], (stats.trainVsValidation || []).map(p => p.value), "pie");
        } catch (e) {
            alert(e.message);
        }
    }

    async function searchExplorer() {
        const query = {
            datasetPath: selectedDatasetPath(),
            patientId: document.getElementById("ftc-exp-patient")?.value || null,
            study: document.getElementById("ftc-exp-study")?.value || null,
            disease: document.getElementById("ftc-exp-disease")?.value || null,
            image: document.getElementById("ftc-exp-image")?.value || null,
            label: document.getElementById("ftc-exp-label")?.value || null,
            take: 50,
        };
        try {
            const res = await apiPost("/dataset-explorer", query);
            const tbody = document.querySelector("#ftc-explorer-table tbody");
            if (!tbody) return;
            tbody.innerHTML = (res.rows || []).map(r =>
                `<tr><td>${esc(r.patientId)}</td><td>${esc(r.studyId)}</td><td>${esc(r.imageName)}</td><td>${esc(r.labels)}</td><td>${esc(r.viewPosition)}</td><td class="text-truncate" title="${esc(r.dicomPath)}">${esc(r.dicomPath)}</td><td class="text-truncate" title="${esc(r.pngPath)}">${esc(r.pngPath)}</td></tr>`
            ).join("") || `<tr><td colspan="7" class="text-muted">No matches (${res.totalMatches || 0})</td></tr>`;
        } catch (e) {
            alert(e.message);
        }
    }

    async function loadDatasetVersions() {
        try {
            const rows = await apiGet("/dataset-versions");
            const tbody = document.querySelector("#ftc-dataset-versions-table tbody");
            if (!tbody) return;
            tbody.innerHTML = rows.map(r =>
                `<tr><td>${esc(r.version)}</td><td><code class="small">${esc((r.contentHash || "").slice(0, 12))}…</code></td><td>${r.createdDate ? new Date(r.createdDate).toLocaleString() : "—"}</td><td>${esc(r.validationStatus)}</td><td class="text-truncate" title="${esc(r.path)}">${esc(r.path)}</td></tr>`
            ).join("");
        } catch { }
    }

    async function pollConsole() {
        const state = ftc()?.getState?.();
        const jobId = state?.activeJobId || dashboard().activeJob?.jobId;
        if (!jobId) return;
        try {
            const data = await apiGet("/console/" + jobId);
            const el = document.getElementById("ftc-console-output");
            if (!el) return;
            el.textContent = data.logText || "Waiting for output…";
            el.scrollTop = el.scrollHeight;
        } catch { }
    }

    function populateThresholdCheckpoints() {
        const sel = document.getElementById("ftc-threshold-checkpoint");
        if (!sel) return;
        const cps = (dashboard().checkpoints || []).filter(c => c.modelId === "CheXNet" || c.modelId === selectedModelId());
        sel.innerHTML = cps.map(c => `<option value="${esc(c.id)}">${esc(c.version)}</option>`).join("");
    }

    async function runThreshold() {
        const cp = document.getElementById("ftc-threshold-checkpoint")?.value;
        const slider = document.getElementById("ftc-threshold-slider");
        if (!cp || !slider) return;
        const threshold = slider.value / 100;
        document.getElementById("ftc-threshold-value").textContent = threshold.toFixed(2);
        try {
            const res = await apiGet(`/threshold/${encodeURIComponent(cp)}?threshold=${threshold}`);
            const metrics = document.getElementById("ftc-threshold-metrics");
            if (metrics) {
                const pct = v => v == null ? "—" : (v <= 1 ? (v * 100).toFixed(1) + "%" : v.toFixed(2));
                metrics.innerHTML = [
                    ["Accuracy", pct(res.accuracy)], ["Precision", pct(res.precision)], ["Recall", pct(res.recall)],
                    ["F1", pct(res.f1)], ["Specificity", pct(res.specificity)], ["Sensitivity", pct(res.sensitivity)],
                    ["Pred +", res.predictedPositives], ["FP", res.falsePositives], ["FN", res.falseNegatives],
                ].map(([k, v]) => `<div class="col-6 col-md-4 col-lg-3"><div class="ftc-home-card"><div class="ftc-home-card-label">${k}</div><div class="ftc-home-card-value">${v}</div></div></div>`).join("");
            }
            upsertChart("chart-threshold-f1", "F1", (res.f1Curve || []).map(p => (p.epoch / 100).toFixed(2)), (res.f1Curve || []).map(p => p.value), "line");
        } catch (e) {
            console.warn(e);
        }
    }

    window.ftcOpenCheckpointDetails = async function (checkpointId) {
        detailCheckpointId = checkpointId;
        try {
            const details = await apiGet("/checkpoint-details/" + encodeURIComponent(checkpointId));
            const body = document.getElementById("ftc-checkpoint-modal-body");
            if (!body) return;
            const cp = details.checkpoint || {};
            const flags = details.flags || {};
            body.innerHTML = `<div class="row g-3">
                <div class="col-md-6"><dl class="ftc-model-metrics">
                    <dt>Version</dt><dd>${esc(cp.version)}</dd>
                    <dt>Dataset</dt><dd>${esc(details.dataset)} (${esc(details.datasetVersion || "—")})</dd>
                    <dt>Epochs</dt><dd>${details.epochs ?? "—"}</dd>
                    <dt>Learning rate</dt><dd>${details.learningRate ?? "—"}</dd>
                    <dt>Optimizer</dt><dd>${esc(details.optimizer || "—")}</dd>
                    <dt>Training time</dt><dd>${cp.trainingTimeSeconds ? cp.trainingTimeSeconds.toFixed(0) + "s" : "—"}</dd>
                </dl></div>
                <div class="col-md-6">
                    <div class="mb-2">${flags.isFavorite ? "⭐" : "☆"} Favorite · ${flags.isPinned ? "📌" : ""} Pinned · ${flags.isProductionCandidate ? "🎯 Candidate" : ""} · ${flags.isRecommended ? "✓ Recommended" : ""}</div>
                    <textarea class="form-control form-control-sm mb-2" id="ftc-cp-notes" rows="3" placeholder="Experiment notes">${esc(details.notes || "")}</textarea>
                    <button type="button" class="btn btn-sm btn-outline-primary" id="ftc-save-cp-notes">Save notes</button>
                    <div class="d-flex gap-1 flex-wrap mt-2">
                        <button type="button" class="btn btn-sm btn-outline-secondary ftc-flag-btn" data-flag="isFavorite">⭐ Favorite</button>
                        <button type="button" class="btn btn-sm btn-outline-secondary ftc-flag-btn" data-flag="isPinned">📌 Pin</button>
                        <button type="button" class="btn btn-sm btn-outline-secondary ftc-flag-btn" data-flag="isProductionCandidate">🎯 Candidate</button>
                        <button type="button" class="btn btn-sm btn-outline-secondary ftc-flag-btn" data-flag="isRecommended">✓ Recommend</button>
                    </div>
                    <div class="mt-3 small">Downloads: ${Object.keys(details.downloadLinks || {}).map(k =>
                        `<a href="${API}/checkpoint-artifact/${encodeURIComponent(checkpointId)}/${encodeURIComponent(k)}" class="me-2">${esc(k)}</a>`).join("") || "—"}</div>
                </div>
            </div>`;
            body.querySelector("#ftc-save-cp-notes")?.addEventListener("click", async () => {
                await apiPost("/checkpoint-notes", { checkpointId, notes: document.getElementById("ftc-cp-notes")?.value || "" });
                ftc()?.refreshDashboard?.();
            });
            body.querySelectorAll(".ftc-flag-btn").forEach(btn => {
                btn.addEventListener("click", async () => {
                    const flag = btn.dataset.flag;
                    const payload = { checkpointId };
                    payload[flag] = true;
                    await apiPost("/checkpoint-flags", payload);
                    ftc()?.renderCheckpoints?.();
                    window.ftcOpenCheckpointDetails(checkpointId);
                });
            });
            bootstrap.Modal.getOrCreateInstance(document.getElementById("ftc-checkpoint-modal")).show();
        } catch (e) {
            alert(e.message);
        }
    };

    async function runProductionTest() {
        const file = document.getElementById("ftc-prod-test-image")?.files[0];
        if (!file) { alert("Choose an image."); return; }
        const fd = new FormData();
        fd.append("image", file);
        fd.append("__RequestVerificationToken", ftc()?.token?.() || document.querySelector('input[name="__RequestVerificationToken"]')?.value || "");
        const r = await fetch(API + "/production-test", { method: "POST", headers: { RequestVerificationToken: fd.get("__RequestVerificationToken") }, body: fd });
        const res = await r.json();
        const el = document.getElementById("ftc-prod-test-result");
        if (!el) return;
        if (res.error) { el.innerHTML = `<p class="text-danger">${esc(res.error)}</p>`; return; }
        el.innerHTML = `<p><strong>${esc(res.topPrediction)}</strong> · Confidence ${(res.confidence * 100).toFixed(1)}%</p>
            <p class="small mb-0">Inference: ${res.inferenceTimeMs?.toFixed(0)} ms · Model version: ${esc(res.modelVersion)}</p>`;
    }

    function wireExtensions() {
        document.getElementById("ftc-preview-load")?.addEventListener("click", loadDatasetPreview);
        document.getElementById("ftc-stats-load")?.addEventListener("click", loadDatasetStatistics);
        document.getElementById("ftc-exp-search")?.addEventListener("click", searchExplorer);
        document.getElementById("ftc-threshold-slider")?.addEventListener("input", runThreshold);
        document.getElementById("ftc-threshold-checkpoint")?.addEventListener("change", runThreshold);
        document.getElementById("ftc-prod-test-run")?.addEventListener("click", () => runProductionTest().catch(e => alert(e.message)));
        document.getElementById("ftc-checkpoint-download-report")?.addEventListener("click", () => {
            if (detailCheckpointId) window.open(API + "/report/" + encodeURIComponent(detailCheckpointId), "_blank");
        });

        document.getElementById("ftc-validate-btn")?.addEventListener("click", () => { wizardValidated = true; refreshWizard(); });
        document.getElementById("ftc-start-btn")?.addEventListener("click", () => { wizardStarted = true; refreshWizard(); });

        document.getElementById("ftc-model-cards")?.addEventListener("click", () => {
            setTimeout(loadArchitecture, 50);
        });
    }

    function initExtensions() {
        const dash = dashboard();
        renderHomeCards(dash.homeSummary);
        renderWorkflow(dash.workflow);
        renderPlugins(dash.registeredPlugins);
        loadArchitecture();
        loadDatasetVersions();
        populateThresholdCheckpoints();
        refreshWizard();
        wireExtensions();
        setInterval(pollConsole, (window.trainingCenterRefreshSec || 5) * 1000);
        setInterval(async () => {
            try {
                const dash2 = await apiGet("/dashboard");
                renderHomeCards(dash2.homeSummary);
                renderWorkflow(dash2.workflow);
                if (dash2.activeJob?.status === "Completed") wizardCompleted = true;
                refreshWizard();
            } catch { }
        }, (window.trainingCenterRefreshSec || 5) * 1000);
    }

    if (document.readyState === "loading")
        document.addEventListener("DOMContentLoaded", () => setTimeout(initExtensions, 100));
    else
        setTimeout(initExtensions, 100);
})();
