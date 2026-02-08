-- Migration: AddPatientModels
-- This script creates the Patient, PatientHistory, and PatientScan tables

SET QUOTED_IDENTIFIER ON;
GO

-- Create Patients table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Patients]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Patients] (
        [Id] int IDENTITY(1,1) NOT NULL,
        [FirstName] nvarchar(100) NOT NULL,
        [LastName] nvarchar(100) NOT NULL,
        [DateOfBirth] date NOT NULL,
        [Gender] nvarchar(10) NOT NULL,
        [PhoneNumber] nvarchar(20) NULL,
        [Email] nvarchar(200) NULL,
        [Address] nvarchar(500) NULL,
        [PatientId] nvarchar(50) NULL,
        [MedicalHistorySummary] nvarchar(500) NULL,
        [Allergies] nvarchar(500) NULL,
        [CurrentMedications] nvarchar(500) NULL,
        [ProfileImagePath] nvarchar(1000) NULL,
        [ProfileImageData] varbinary(max) NULL,
        [ProfileImageContentType] nvarchar(50) NULL,
        [CreatedByUserId] nvarchar(450) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_Patients] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Patients_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AspNetUsers] ([Id])
    );

    -- Create unique index on PatientId
    CREATE UNIQUE NONCLUSTERED INDEX [IX_Patients_PatientId] ON [dbo].[Patients] ([PatientId]) WHERE [PatientId] IS NOT NULL;
END
GO

-- Create PatientHistories table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[PatientHistories]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[PatientHistories] (
        [Id] int IDENTITY(1,1) NOT NULL,
        [PatientId] int NOT NULL,
        [VisitDate] datetime2 NOT NULL,
        [VisitType] nvarchar(200) NULL,
        [ChiefComplaint] nvarchar(1000) NULL,
                    [ClinicalNotes] nvarchar(max) NULL,
                    [Diagnosis] nvarchar(max) NULL,
                    [TreatmentPlan] nvarchar(max) NULL,
                    [ConditionDescription] nvarchar(max) NULL,
        [CreatedByUserId] nvarchar(450) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_PatientHistories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PatientHistories_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AspNetUsers] ([Id]),
        CONSTRAINT [FK_PatientHistories_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [dbo].[Patients] ([Id]) ON DELETE CASCADE
    );

    CREATE NONCLUSTERED INDEX [IX_PatientHistories_PatientId] ON [dbo].[PatientHistories] ([PatientId]);
END
GO

-- Create PatientScans table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[PatientScans]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[PatientScans] (
        [Id] int IDENTITY(1,1) NOT NULL,
        [PatientId] int NOT NULL,
        [ScanType] nvarchar(50) NOT NULL,
        [ScanDate] datetime2 NOT NULL,
        [FileName] nvarchar(200) NULL,
        [ContentType] nvarchar(100) NULL,
        [ImageData] varbinary(max) NULL,
        [ImagePath] nvarchar(1000) NULL,
        [ImageDataUrl] nvarchar(max) NULL,
        [PatientHistoryId] int NULL,
        [CheXNetResults] nvarchar(max) NULL,
        [BioBertResults] nvarchar(max) NULL,
        [LungAIResults] nvarchar(max) NULL,
        [LinkedModels] nvarchar(200) NULL,
        [GeneratedResult] nvarchar(max) NULL,
        [ResultGeneratedAt] datetime2 NULL,
        [CreatedByUserId] nvarchar(450) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_PatientScans] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PatientScans_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AspNetUsers] ([Id]),
        CONSTRAINT [FK_PatientScans_PatientHistories_PatientHistoryId] FOREIGN KEY ([PatientHistoryId]) REFERENCES [dbo].[PatientHistories] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_PatientScans_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [dbo].[Patients] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION
    );

    CREATE NONCLUSTERED INDEX [IX_PatientScans_PatientId] ON [dbo].[PatientScans] ([PatientId]);
    CREATE NONCLUSTERED INDEX [IX_PatientScans_PatientHistoryId] ON [dbo].[PatientScans] ([PatientHistoryId]);
END
GO

PRINT 'Migration completed successfully! Patient tables have been created.';
