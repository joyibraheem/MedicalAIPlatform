using System.Globalization;
using System.Text.Json;
using MedicalAIPlatform.Models.TrainingCenter;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MedicalAIPlatform.Services.TrainingCenter;

public sealed class TrainingReportPdfService
{
    static TrainingReportPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private readonly CheXNetPathResolver _paths;
    private readonly TrainingMonitorReader _monitor;
    private readonly ModelPluginMetadataRegistry _plugins;

    public TrainingReportPdfService(
        CheXNetPathResolver paths,
        TrainingMonitorReader monitor,
        ModelPluginMetadataRegistry plugins)
    {
        _paths = paths;
        _monitor = monitor;
        _plugins = plugins;
    }

    public async Task<string?> GenerateAsync(
        string checkpointId,
        string modelId,
        string? datasetVersion,
        string? datasetHash,
        TrainingRecommendationDto? recommendation,
        CancellationToken ct = default)
    {
        var versionsDir = _paths.ResolveModelVersionsDirectory();
        var dir = Path.Combine(versionsDir, checkpointId);
        if (!Directory.Exists(dir)) return null;

        var metricsPath = Path.Combine(dir, "metrics.json");
        if (!File.Exists(metricsPath)) return null;

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(metricsPath, ct).ConfigureAwait(false));
        var root = doc.RootElement;
        var final = root.TryGetProperty("final_metrics", out var fm) ? fm : default;
        var charts = _monitor.ReadCharts(dir);
        var architecture = _plugins.GetArchitecture(modelId);
        var reportsDir = Path.Combine(_paths.ResolveCheXNetMasterDirectory(), "reports", "training");
        Directory.CreateDirectory(reportsDir);
        var reportPath = Path.Combine(reportsDir, $"{checkpointId}_report.pdf");

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(h =>
                {
                    h.Item().Text("Training Report").SemiBold().FontSize(18).FontColor(Colors.Blue.Darken3);
                    h.Item().Text($"Checkpoint: {checkpointId} · Generated (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm}")
                        .FontSize(9).FontColor(Colors.Grey.Darken2);
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(8);
                    Section(col, "Model Information", () =>
                    {
                        col.Item().Text($"Model: {architecture?.ModelName ?? modelId}");
                        col.Item().Text($"Backbone: {architecture?.Backbone ?? "—"}");
                        col.Item().Text($"Classifier: {architecture?.Classifier ?? "—"}");
                        col.Item().Text($"Input size: {architecture?.InputSize ?? "—"}");
                        col.Item().Text($"Output classes: {architecture?.OutputClasses.ToString(CultureInfo.InvariantCulture) ?? "—"}");
                    });

                    Section(col, "Hyperparameters", () =>
                    {
                        if (root.TryGetProperty("epochs", out var ep)) col.Item().Text($"Epochs: {ep}");
                        if (root.TryGetProperty("batch_size", out var bs)) col.Item().Text($"Batch size: {bs}");
                        if (root.TryGetProperty("learning_rate", out var lr)) col.Item().Text($"Learning rate: {lr}");
                        if (root.TryGetProperty("train_samples", out var ts)) col.Item().Text($"Train samples: {ts}");
                        if (root.TryGetProperty("val_samples", out var vs)) col.Item().Text($"Validation samples: {vs}");
                    });

                    Section(col, "Dataset", () =>
                    {
                        col.Item().Text($"Dataset version: {datasetVersion ?? "—"}");
                        col.Item().Text($"Dataset hash: {datasetHash ?? "—"}");
                    });

                    Section(col, "Final Metrics", () =>
                    {
                        WriteMetric(col, "Accuracy", final, "accuracy");
                        WriteMetric(col, "Precision (micro)", final, "precision_micro");
                        WriteMetric(col, "Recall (micro)", final, "recall_micro");
                        WriteMetric(col, "F1 (micro)", final, "f1_micro");
                        WriteMetric(col, "ROC-AUC (macro)", final, "roc_auc_macro");
                        WriteMetric(col, "Val loss", final, "val_loss");
                        if (root.TryGetProperty("training_time_seconds", out var tt))
                            col.Item().Text($"Training time: {tt.GetDouble():F0} seconds");
                    });

                    Section(col, "Training Curves Summary", () =>
                    {
                        col.Item().Text($"Loss epochs recorded: {charts.TrainLoss.Count}");
                        col.Item().Text($"Best train loss: {charts.TrainLoss.MinBy(p => p.Value)?.Value.ToString("F4", CultureInfo.InvariantCulture) ?? "—"}");
                        col.Item().Text($"Best val loss: {charts.ValLoss.MinBy(p => p.Value)?.Value.ToString("F4", CultureInfo.InvariantCulture) ?? "—"}");
                        col.Item().Text($"Peak F1: {charts.F1.MaxBy(p => p.Value)?.Value.ToString("P2", CultureInfo.InvariantCulture) ?? "—"}");
                        col.Item().Text($"Peak ROC-AUC: {charts.RocAuc.MaxBy(p => p.Value)?.Value.ToString("P2", CultureInfo.InvariantCulture) ?? "—"}");
                    });

                    Section(col, "Hardware", () =>
                    {
                        col.Item().Text($"Host: {Environment.MachineName}");
                        col.Item().Text($"OS: {Environment.OSVersion}");
                        col.Item().Text($"Processors: {Environment.ProcessorCount}");
                    });

                    Section(col, "Recommendation", () =>
                    {
                        col.Item().Text(recommendation?.Recommendation ?? "Review metrics before deployment.");
                        col.Item().Text(recommendation?.Explanation ?? "");
                    });
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Medical AI Platform · Admin Fine-Tuning Center");
                    x.Span(" · Page ");
                    x.CurrentPageNumber();
                });
            });
        }).GeneratePdf();

        await File.WriteAllBytesAsync(reportPath, pdf, ct).ConfigureAwait(false);
        return reportPath;
    }

    private static void Section(ColumnDescriptor col, string title, Action fill)
    {
        col.Item().Background(Colors.Grey.Lighten3).Padding(6).Text(title).SemiBold();
        fill();
    }

    private static void WriteMetric(ColumnDescriptor col, string label, JsonElement metrics, string key)
    {
        if (metrics.ValueKind == JsonValueKind.Object && metrics.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
            col.Item().Text($"{label}: {v.GetDouble():P2}");
    }
}
