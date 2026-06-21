using MedicalAIPlatform.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MedicalAIPlatform.Services;

public sealed class MedicalReportPdfService
{
    static MedicalReportPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] BuildPdf(MedicalReportResponseDto dto)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10).LineHeight(1.35f));

                page.Header().Column(h =>
                {
                    h.Item().Text("Structured Medical Report").SemiBold().FontSize(18).FontColor(Colors.Blue.Darken3);
                    h.Item().PaddingTop(4).Text($"Generated (UTC): {dto.GeneratedAt:yyyy-MM-dd HH:mm}")
                        .FontSize(9).FontColor(Colors.Grey.Darken2);
                    h.Item().Text($"Model version: {dto.ModelVersion} · Confidence: {dto.Confidence:P0}")
                        .FontSize(9).FontColor(Colors.Grey.Darken2);
                    if (!string.IsNullOrWhiteSpace(dto.ConfidenceMethod))
                        h.Item().Text($"Confidence derivation: {dto.ConfidenceMethod}")
                            .FontSize(9).FontColor(Colors.Grey.Darken2);
                    if (dto.DoctorModifiedAt is { } dm)
                        h.Item().Text($"Last physician edit (UTC): {dm:yyyy-MM-dd HH:mm}").FontSize(9).Italic();
                });

                page.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Background(Colors.Grey.Lighten3).Padding(8).Text("Patient").SemiBold().FontSize(11);
                    col.Item().Text($"{dto.PatientDisplayName} · Chart #{dto.PatientId}");

                    col.Item().Background(Colors.Grey.Lighten3).Padding(8).Text("Medical history (structured)").SemiBold().FontSize(11);
                    foreach (var kv in dto.MedicalHistory.Demographics.OrderBy(k => k.Key))
                        col.Item().Text($"• {kv.Key}: {kv.Value}");

                    foreach (var line in dto.MedicalHistory.SummaryLines)
                        col.Item().Text($"• {line}");

                    foreach (var v in dto.MedicalHistory.Visits.Take(12))
                    {
                        col.Item().Text($"{v.VisitDate} · {v.VisitType ?? "Visit"}").SemiBold();
                        if (!string.IsNullOrWhiteSpace(v.ChiefComplaint))
                            col.Item().Text($"Chief complaint: {v.ChiefComplaint}");
                        if (!string.IsNullOrWhiteSpace(v.Diagnosis))
                            col.Item().Text($"Diagnosis: {v.Diagnosis}");
                        if (!string.IsNullOrWhiteSpace(v.ClinicalNotes))
                            col.Item().Text($"Notes: {v.ClinicalNotes}");
                    }

                    col.Item().Background(Colors.Grey.Lighten3).Padding(8).Text("AI analysis").SemiBold().FontSize(11);
                    var ai = dto.AiAnalysis;
                    if (ai.ChexnetProbabilities.Count + ai.CtProbabilities.Count == 0 &&
                        string.IsNullOrWhiteSpace(ai.TopConditionLabel))
                        col.Item().Text(
                                "No CheXNet or LungAI probabilities on this snapshot (regenerate after merging studies or session analytics).")
                            .Italic().FontColor(Colors.Grey.Darken1).FontSize(9);
                    else
                    {
                        col.Item().Text($"Risk tier (modeled): {(ai.RiskTier ?? "").ToUpperInvariant()} · Primary class: {ai.TopConditionLabel} ({ai.TopConditionProbability:P0})").FontSize(9);
                        if (!string.IsNullOrWhiteSpace(dto.AiAnalysis.ConfidenceCaption))
                            col.Item().Text(dto.AiAnalysis.ConfidenceCaption).FontSize(8).FontColor(Colors.Grey.Darken2).Italic();

                        col.Item().Text($"CheXNet (merged): {FormatProbabilityRows(ai.ChexnetProbabilities)}").FontSize(9);
                        col.Item().Text($"LungAI CT (merged): {FormatProbabilityRows(ai.CtProbabilities)}").FontSize(9);
                        if (ai.ClinicalEntities.Count > 0)
                            col.Item().Text(
                                    $"BioBERT highlights: {string.Join("; ", ai.ClinicalEntities.Take(8).Select(e => $"{e.Text} ({e.Score:P0})"))}")
                                .FontSize(9);
                        if (!string.IsNullOrWhiteSpace(ai.HeatmapNote))
                            col.Item().Text(ai.HeatmapNote).FontSize(8).FontColor(Colors.Grey.Darken2).Italic();
                    }

                    col.Item().Background(Colors.Grey.Lighten3).Padding(8).Text("Findings").SemiBold().FontSize(11);
                    col.Item().Text(dto.Findings).AlignLeft();

                    col.Item().Background(Colors.Grey.Lighten3).Padding(8).Text("Impression").SemiBold().FontSize(11);
                    col.Item().Text(dto.Impression).AlignLeft();

                    col.Item().Background(Colors.Grey.Lighten3).Padding(8).Text("Recommendations").SemiBold().FontSize(11);
                    col.Item().Text(dto.Recommendations).AlignLeft();

                    col.Item().PaddingTop(12).Text(
                            "This document contains AI-assisted drafting for clinical workflow support only. It is not a substitute for professional medical judgment, formal reporting standards, or institutional policy.")
                        .FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
                });

                page.Footer()
                    .AlignCenter()
                    .DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Medium))
                    .Text(t =>
                    {
                        t.Span("Page ");
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
            });
        }).GeneratePdf();
    }

    private static string FormatProbabilityRows(List<MedicalReportProbabilityRowDto> rows)
    {
        if (rows.Count == 0)
            return "(none)";
        return string.Join(" · ", rows.Take(10).Select(r => $"{r.Label} {r.Probability:P0}"));
    }
}
