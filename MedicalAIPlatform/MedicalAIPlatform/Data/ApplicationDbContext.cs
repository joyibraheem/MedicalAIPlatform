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
                .OnDelete(DeleteBehavior.Restrict); // Changed to Restrict to avoid multiple cascade paths
            
            entity.HasOne(e => e.PatientHistory)
                .WithMany()
                .HasForeignKey(e => e.PatientHistoryId)
                .OnDelete(DeleteBehavior.SetNull);
            
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });
    }
}


