(function () {
    function chartColors() {
        var muted =
            document.documentElement.getAttribute("data-theme") === "dark" ||
            document.body.classList.contains("dark-mode")
                ? "rgba(148,163,184,0.85)"
                : "#64748b";
        var grid =
            document.documentElement.getAttribute("data-theme") === "dark" ||
            document.body.classList.contains("dark-mode")
                ? "rgba(148,163,184,0.14)"
                : "rgba(148,163,184,0.35)";
        var text =
            document.documentElement.getAttribute("data-theme") === "dark" ||
            document.body.classList.contains("dark-mode")
                ? "#e2e8f0"
                : "#0f172a";
        return { muted: muted, grid: grid, text: text };
    }

    function renderCharts(cfg) {
        if (typeof Chart === "undefined" || !cfg) return;

        var colors = chartColors();
        Chart.defaults.font.family =
            "'Outfit','Inter',system-ui,sans-serif";
        Chart.defaults.color = colors.muted;

        var lineCtx = document.getElementById("adminChartAccuracy");
        if (lineCtx && cfg.accuracyLabels && cfg.accuracyValues) {
            var grad = lineCtx.getContext("2d").createLinearGradient(0, 0, 0, 260);
            grad.addColorStop(0, "rgba(37,99,235,0.35)");
            grad.addColorStop(1, "rgba(37,99,235,0)");
            new Chart(lineCtx, {
                type: "line",
                data: {
                    labels: cfg.accuracyLabels,
                    datasets: [
                        {
                            label: "Calibration %",
                            data: cfg.accuracyValues,
                            tension: 0.35,
                            fill: true,
                            backgroundColor: grad,
                            borderColor: "rgba(37,99,235,1)",
                            borderWidth: 2,
                            pointRadius: 4,
                            pointHoverRadius: 6,
                            pointBackgroundColor: "#fff",
                            pointBorderColor: "rgba(37,99,246,1)"
                        }
                    ]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            callbacks: {
                                label: function (ctx) {
                                    return (
                                        (ctx.parsed.y &&
                                            ctx.parsed.y.toFixed
                                            ? ctx.parsed.y.toFixed(2)
                                            : ctx.parsed.y) + "%"
                                    );
                                }
                            }
                        }
                    },
                    scales: {
                        x: {
                            grid: { color: colors.grid },
                            ticks: { color: colors.muted }
                        },
                        y: {
                            min: 85,
                            max: 100,
                            grid: { color: colors.grid },
                            ticks: {
                                color: colors.muted,
                                callback: function (v) {
                                    return v + "%";
                                }
                            }
                        }
                    }
                }
            });
        }

        var pieCtx = document.getElementById("adminChartDistribution");
        if (
            pieCtx &&
            cfg.hasInferenceSamples &&
            cfg.distributionLabels &&
            cfg.distributionValues
        ) {
            new Chart(pieCtx, {
                type: "doughnut",
                data: {
                    labels: cfg.distributionLabels,
                    datasets: [
                        {
                            data: cfg.distributionValues,
                            backgroundColor: [
                                "rgba(239,68,68,0.85)",
                                "rgba(16,185,129,0.85)",
                                "rgba(245,158,11,0.85)"
                            ],
                            borderWidth: 2,
                            borderColor: colors.text === "#e2e8f0" ? "#0f172a" : "#fff",
                            hoverOffset: 10
                        }
                    ]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    cutout: "62%",
                    plugins: {
                        legend: {
                            position: "bottom",
                            labels: { color: colors.muted, padding: 14 }
                        }
                    }
                }
            });
        }

        var barCtx = document.getElementById("adminChartActivity");
        if (barCtx && cfg.activityLabels && cfg.activityValues) {
            new Chart(barCtx, {
                type: "bar",
                data: {
                    labels: cfg.activityLabels,
                    datasets: [
                        {
                            label: "Analyses",
                            data: cfg.activityValues,
                            borderRadius: 10,
                            backgroundColor: "rgba(59,130,246,0.75)",
                            borderSkipped: false
                        }
                    ]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { display: false }
                    },
                    scales: {
                        x: {
                            grid: { display: false },
                            ticks: { color: colors.muted }
                        },
                        y: {
                            beginAtZero: true,
                            ticks: {
                                precision: 0,
                                color: colors.muted
                            },
                            grid: { color: colors.grid }
                        }
                    }
                }
            });
        }
    }

    document.addEventListener("DOMContentLoaded", function () {
        var el = document.getElementById("admin-chart-data");
        if (!el) return;
        try {
            var cfg = JSON.parse(el.textContent || "{}");
            renderCharts(cfg);
        } catch (e) {
            console.warn("Admin dashboard chart config:", e);
        }
    });
})();
