# Apply Patient Models Migration

## Option 1: Using Entity Framework (Recommended)

If you have `dotnet-ef` tool installed, run:

```bash
cd MedicalAIPlatform/MedicalAIPlatform
dotnet ef database update
```

If `dotnet-ef` is not installed, install it first:
```bash
dotnet tool install --global dotnet-ef
```

## Option 2: Using SQL Script (Direct Database)

If you prefer to apply the migration directly to your SQL Server database:

1. Open SQL Server Management Studio (SSMS) or your preferred SQL client
2. Connect to your database
3. Open and execute the file: `Migrations/ApplyPatientModelsMigration.sql`

The SQL script will:
- Create the `Patients` table
- Create the `PatientHistories` table  
- Create the `PatientScans` table
- Set up all foreign keys and indexes
- Handle existing tables gracefully (won't fail if tables already exist)

## Option 3: Using Package Manager Console (Visual Studio)

In Visual Studio:
1. Open Package Manager Console
2. Run: `Update-Database`

## Verification

After applying the migration, verify the tables were created:

```sql
SELECT TABLE_NAME 
FROM INFORMATION_SCHEMA.TABLES 
WHERE TABLE_NAME IN ('Patients', 'PatientHistories', 'PatientScans');
```

You should see all three tables listed.
