using MedicalAIPlatform.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<DoctorVerificationCode> DoctorVerificationCodes { get; set; }
    public DbSet<Patient> Patients { get; set; }
    public DbSet<PatientHistory> PatientHistories { get; set; }
    public DbSet<PatientScan> PatientScans { get; set; }

    public DbSet<ScanAiAnalysis> ScanAiAnalyses { get; set; }

    public DbSet<ChestAiBackgroundJob> ChestAiBackgroundJobs { get; set; }

    public DbSet<ChestAiChatMessageEntity> ChestAiChatMessages { get; set; }

    public DbSet<PredictionFeedback> PredictionFeedbacks { get; set; }

    public DbSet<PredictionFeedbackAuditEntry> PredictionFeedbackAuditEntries { get; set; }

    public DbSet<ClinicalMedicalReport> ClinicalMedicalReports { get; set; }

    public DbSet<MedicalReportRevision> MedicalReportRevisions { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Configure DoctorVerificationCode
        builder.Entity<DoctorVerificationCode>(entity =>
        {
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.IsUsed).HasDefaultValue(false);
        });

        builder.Entity<DoctorVerificationCode>().HasData(
            new DoctorVerificationCode 
            { 
                Id = 1, 
                Code = "DOC-2026-001", 
                IsUsed = false, 
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) 
            },
            new DoctorVerificationCode 
            { 
                Id = 2, 
                Code = "DOC-2026-002", 
                IsUsed = false, 
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) 
            },
            new DoctorVerificationCode 
            { 
                Id = 3, 
                Code = "DOC-2026-003", 
                IsUsed = false, 
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) 
            }
        );

        // Configure Patient relationships
        builder.Entity<Patient>(entity =>
        {
            entity.HasIndex(e => e.PatientId).IsUnique().HasFilter("[PatientId] IS NOT NULL");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // Configure PatientHistory relationships
        builder.Entity<PatientHistory>(entity =>
        {
            entity.HasOne(e => e.Patient)
                .WithMany(p => p.HistoryEntries)
                .HasForeignKey(e => e.PatientId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // Configure PatientScan relationships
        builder.Entity<PatientScan>(entity =>
        {
            entity.HasOne(e => e.Patient)
                .WithMany(p => p.Scans)
                .HasForeignKey(e => e.PatientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.PatientHistory)
                .WithMany()
                .HasForeignKey(e => e.PatientHistoryId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.AiAnalysis)
                .WithOne(a => a.PatientScan)
                .HasForeignKey<ScanAiAnalysis>(a => a.PatientScanId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        builder.Entity<ScanAiAnalysis>(entity =>
        {
            entity.HasIndex(e => e.PatientScanId).IsUnique();
            entity.HasIndex(e => e.Status);
            entity.Property(e => e.Status).HasMaxLength(32).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.BackgroundJob)
                .WithMany()
                .HasForeignKey(e => e.BackgroundJobId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.MedicalReport)
                .WithOne(r => r.ScanAiAnalysis)
                .HasForeignKey<ClinicalMedicalReport>(r => r.ScanAiAnalysisId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ChestAiBackgroundJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });
            entity.HasIndex(e => e.PatientScanId);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.Kind).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(32).IsRequired();
            entity.Property(e => e.UserId).HasMaxLength(450).IsRequired();
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.PatientScan)
                .WithMany()
                .HasForeignKey(e => e.PatientScanId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ChestAiChatMessageEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.Role).HasMaxLength(32).IsRequired();
            entity.Property(e => e.UserId).HasMaxLength(450).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PredictionFeedback>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.SubmittingDoctorUserId, e.CreatedAt });
            entity.HasIndex(e => e.ReviewStatus);
            entity.HasIndex(e => new { e.SubmittingDoctorUserId, e.ClientSessionCorrelationId, e.ModelKey });
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.SubmittingDoctorUserId).HasMaxLength(450).IsRequired();
            entity.Property(e => e.Modality).HasMaxLength(32).IsRequired();
            entity.Property(e => e.ModelKey).HasMaxLength(64).IsRequired();
            entity.Property(e => e.DoctorAction).HasMaxLength(16).IsRequired();
            entity.Property(e => e.ReviewStatus).HasMaxLength(24).IsRequired();
            entity.Property(e => e.OriginalPredictionJson).IsRequired();
            entity.Property(e => e.StudyInstanceUid).HasMaxLength(128);
            entity.Property(e => e.SeriesInstanceUid).HasMaxLength(128);
            entity.Property(e => e.CorrectedPrimaryLabel).HasMaxLength(512);
            entity.Property(e => e.TrainingExportBatchId).HasMaxLength(128);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(e => e.SubmittingDoctorUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<ChestAiBackgroundJob>()
                .WithMany()
                .HasForeignKey(e => e.RelatedJobId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<PredictionFeedbackAuditEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PredictionFeedbackId);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.ActorUserId).HasMaxLength(450).IsRequired();
            entity.Property(e => e.Action).HasMaxLength(64).IsRequired();

            entity.HasOne<PredictionFeedback>()
                .WithMany()
                .HasForeignKey(e => e.PredictionFeedbackId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(e => e.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ClinicalMedicalReport>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.PatientId, e.GeneratedAt });
            entity.HasIndex(e => e.ScanAiAnalysisId).IsUnique().HasFilter("[ScanAiAnalysisId] IS NOT NULL");
            entity.Property(e => e.AiSnapshotJson).IsRequired();
            entity.Property(e => e.FindingsDisplay).IsRequired();
            entity.Property(e => e.ImpressionDisplay).IsRequired();
            entity.Property(e => e.RecommendationsDisplay).IsRequired();
            entity.Property(e => e.GeneratedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(e => e.ModelVersion).HasMaxLength(128);

            entity.HasOne(e => e.Patient)
                .WithMany(p => p.MedicalReports)
                .HasForeignKey(e => e.PatientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MedicalReportRevision>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ReportId);
            entity.Property(e => e.Source).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Findings).IsRequired();
            entity.Property(e => e.Impression).IsRequired();
            entity.Property(e => e.Recommendations).IsRequired();
            entity.Property(e => e.ActorUserId).HasMaxLength(450);

            entity.HasOne(e => e.Report)
                .WithMany(r => r.Revisions)
                .HasForeignKey(e => e.ReportId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}


