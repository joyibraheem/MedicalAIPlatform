using System;
using System.Collections.Generic;

namespace MedicalAIPlatform.Models;

public sealed class AdminKpiCardVm
{
    public string Title { get; init; } = "";
    public string ValueDisplay { get; init; } = "";
    /// <summary>up | down | steady</summary>
    public string TrendVariant { get; init; } = "steady";
    public string TrendLabel { get; init; } = "";
    public string TrendContext { get; init; } = "";
    public string IconBi { get; init; } = "bi-activity";
}

public sealed class AdminPendingReviewVm
{
    public Guid FeedbackId { get; init; }
    public string CaseLabel { get; init; } = "";
    public string PredictionSnippet { get; init; } = "";
    public string DoctorActionDisplay { get; init; } = "";
    public string ClinicianDisplay { get; init; } = "";
    public string Modality { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class AdminActivityFeedItemVm
{
    public string IconBi { get; init; } = "bi-circle-fill";
    public string Message { get; init; } = "";
    public DateTimeOffset Timestamp { get; init; }
}

public sealed class AdminDashboardAlertVm
{
    public string Severity { get; init; } = "info"; // info | warning | danger
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
}

/// <summary>Serializable chart payloads for Chart.js (JSON serialized into the view).</summary>
public sealed class AdminDashboardChartsVm
{
    public List<string> AccuracyLabels { get; init; } = new();
    public List<double> AccuracyValues { get; init; } = new();

    public List<string> DistributionLabels { get; init; } = new();
    public List<double> DistributionValues { get; init; } = new();

    public List<string> ActivityLabels { get; init; } = new();
    public List<double> ActivityValues { get; init; } = new();

    public bool HasInferenceSamples { get; init; }
}

public class AdminDashboardViewModel
{
    public AdminKpiCardVm DoctorsCard { get; set; } = new();
    public AdminKpiCardVm PatientsCard { get; set; } = new();
    public AdminKpiCardVm AccuracyCard { get; set; } = new();

    /// <summary>Backward-compatible headline accuracy (also shown in KPI).</summary>
    public double AccuracyRate { get; set; }

    public AdminDashboardChartsVm Charts { get; set; } = new();

    public List<ApplicationUser> PendingDoctors { get; set; } = new();
    public List<AdminPendingReviewVm> PendingClinicalReviews { get; set; } = new();
    public List<AdminActivityFeedItemVm> RecentActivity { get; set; } = new();
    public List<AdminDashboardAlertVm> Alerts { get; set; } = new();

    public string TopConditionToday { get; set; } = "—";
    public string TopConditionSubtitle { get; set; } = "";
    public List<string> AiInsightBullets { get; set; } = new();

    public int ActiveDoctorsCount { get; set; }
    public int PatientsCount { get; set; }
    public int PendingDoctorRegistrationsCount { get; set; }
    public int PendingFeedbackReviewsCount { get; set; }
    public int FailedInferenceJobsLast24h { get; set; }

    public bool IsSystemWorking { get; set; } = true;
}
