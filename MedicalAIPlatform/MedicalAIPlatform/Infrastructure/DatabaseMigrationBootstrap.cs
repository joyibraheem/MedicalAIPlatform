using MedicalAIPlatform.Data;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Infrastructure;

internal static class DatabaseMigrationBootstrap
{
    private const string ProductVersion = "9.0.0-preview.2.24128.4";

    /// <summary>
    /// Repairs legacy databases where schema was created manually but migration history was not recorded.
    /// </summary>
    public static void PrepareLegacyDatabase(ApplicationDbContext context)
    {
        if (!context.Database.CanConnect())
        {
            return;
        }

        context.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'Patients', N'U') IS NOT NULL
            AND NOT EXISTS (
                SELECT 1 FROM __EFMigrationsHistory
                WHERE MigrationId = N'20260208000000_AddPatientModels')
            INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
            VALUES (N'20260208000000_AddPatientModels', {0});
            """, ProductVersion);
    }
}
