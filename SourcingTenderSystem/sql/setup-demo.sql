USE [master];
GO

IF DB_ID(N'SourcingTenderSystem_Demo') IS NULL
    CREATE DATABASE [SourcingTenderSystem_Demo];
GO

USE [SourcingTenderSystem_Demo];
GO

SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] nvarchar(450) NOT NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id] nvarchar(450) NOT NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(128) NOT NULL,
        [ProviderKey] nvarchar(128) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] nvarchar(450) NOT NULL,
        [RoleId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] nvarchar(450) NOT NULL,
        [LoginProvider] nvarchar(128) NOT NULL,
        [Name] nvarchar(128) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'00000000000000_CreateIdentitySchema', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [Departments] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(450) NOT NULL,
        [Description] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Departments] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [SupplierProfiles] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [CompanyName] nvarchar(max) NOT NULL,
        [TaxNumber] nvarchar(450) NOT NULL,
        [CommercialRegNo] nvarchar(max) NOT NULL,
        [ContactPhone] nvarchar(max) NOT NULL,
        [Address] nvarchar(max) NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [StatusReason] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SupplierProfiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SupplierProfiles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [EmployeeProfiles] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [FullName] nvarchar(max) NOT NULL,
        [DepartmentId] int NULL,
        [JobTitle] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_EmployeeProfiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmployeeProfiles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EmployeeProfiles_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [SupplierDocuments] (
        [Id] int NOT NULL IDENTITY,
        [SupplierProfileId] int NOT NULL,
        [DocumentType] nvarchar(max) NOT NULL,
        [FilePath] nvarchar(max) NOT NULL,
        [ExpiryDate] datetime2 NULL,
        [UploadedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SupplierDocuments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SupplierDocuments_SupplierProfiles_SupplierProfileId] FOREIGN KEY ([SupplierProfileId]) REFERENCES [SupplierProfiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] int NOT NULL IDENTITY,
        [EmployeeProfileId] int NULL,
        [SupplierProfileId] int NULL,
        [Action] nvarchar(max) NOT NULL,
        [EntityName] nvarchar(max) NOT NULL,
        [EntityId] int NOT NULL,
        [Details] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AuditLogs_EmployeeProfiles_EmployeeProfileId] FOREIGN KEY ([EmployeeProfileId]) REFERENCES [EmployeeProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AuditLogs_SupplierProfiles_SupplierProfileId] FOREIGN KEY ([SupplierProfileId]) REFERENCES [SupplierProfiles] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [Notifications] (
        [Id] int NOT NULL IDENTITY,
        [EmployeeProfileId] int NULL,
        [SupplierProfileId] int NULL,
        [Title] nvarchar(max) NOT NULL,
        [Message] nvarchar(max) NOT NULL,
        [Link] nvarchar(max) NULL,
        [IsRead] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Notifications_EmployeeProfiles_EmployeeProfileId] FOREIGN KEY ([EmployeeProfileId]) REFERENCES [EmployeeProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Notifications_SupplierProfiles_SupplierProfileId] FOREIGN KEY ([SupplierProfileId]) REFERENCES [SupplierProfiles] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [PurchaseRequisitions] (
        [Id] int NOT NULL IDENTITY,
        [Title] nvarchar(max) NOT NULL,
        [Reason] nvarchar(max) NOT NULL,
        [Priority] nvarchar(max) NOT NULL,
        [TargetDate] datetime2 NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [DepartmentId] int NOT NULL,
        [CreatedByEmployeeProfileId] int NOT NULL,
        [ReviewedByEmployeeProfileId] int NULL,
        [ReviewNotes] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_PurchaseRequisitions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PurchaseRequisitions_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchaseRequisitions_EmployeeProfiles_CreatedByEmployeeProfileId] FOREIGN KEY ([CreatedByEmployeeProfileId]) REFERENCES [EmployeeProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchaseRequisitions_EmployeeProfiles_ReviewedByEmployeeProfileId] FOREIGN KEY ([ReviewedByEmployeeProfileId]) REFERENCES [EmployeeProfiles] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [PurchaseRequisitionItems] (
        [Id] int NOT NULL IDENTITY,
        [PurchaseRequisitionId] int NOT NULL,
        [ItemName] nvarchar(max) NOT NULL,
        [Quantity] int NOT NULL,
        [Unit] nvarchar(max) NOT NULL,
        [Specifications] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_PurchaseRequisitionItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PurchaseRequisitionItems_PurchaseRequisitions_PurchaseRequisitionId] FOREIGN KEY ([PurchaseRequisitionId]) REFERENCES [PurchaseRequisitions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [Tenders] (
        [Id] int NOT NULL IDENTITY,
        [PurchaseRequisitionId] int NOT NULL,
        [Title] nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [Terms] nvarchar(max) NOT NULL,
        [OpenDate] datetime2 NOT NULL,
        [CloseDate] datetime2 NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [CreatedByEmployeeProfileId] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [counter] int NOT NULL DEFAULT 1,
        CONSTRAINT [PK_Tenders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Tenders_EmployeeProfiles_CreatedByEmployeeProfileId] FOREIGN KEY ([CreatedByEmployeeProfileId]) REFERENCES [EmployeeProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Tenders_PurchaseRequisitions_PurchaseRequisitionId] FOREIGN KEY ([PurchaseRequisitionId]) REFERENCES [PurchaseRequisitions] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [Bids] (
        [Id] int NOT NULL IDENTITY,
        [TenderId] int NOT NULL,
        [SupplierProfileId] int NOT NULL,
        [TotalPrice] decimal(18,2) NOT NULL,
        [Currency] nvarchar(max) NOT NULL,
        [DeliveryDays] int NOT NULL,
        [ValidityDays] int NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [ComplianceNotes] nvarchar(max) NULL,
        [SubmittedAt] datetime2 NULL,
        CONSTRAINT [PK_Bids] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Bids_SupplierProfiles_SupplierProfileId] FOREIGN KEY ([SupplierProfileId]) REFERENCES [SupplierProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Bids_Tenders_TenderId] FOREIGN KEY ([TenderId]) REFERENCES [Tenders] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [EvaluationCriteria] (
        [Id] int NOT NULL IDENTITY,
        [TenderId] int NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Type] nvarchar(max) NOT NULL,
        [WeightPercent] decimal(18,2) NOT NULL,
        [MaxScore] int NOT NULL,
        CONSTRAINT [PK_EvaluationCriteria] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EvaluationCriteria_Tenders_TenderId] FOREIGN KEY ([TenderId]) REFERENCES [Tenders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [Invitations] (
        [Id] int NOT NULL IDENTITY,
        [TenderId] int NOT NULL,
        [SupplierProfileId] int NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [SentAt] datetime2 NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [RespondedAt] datetime2 NULL,
        [DeclineReason] nvarchar(max) NULL,
        CONSTRAINT [PK_Invitations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Invitations_SupplierProfiles_SupplierProfileId] FOREIGN KEY ([SupplierProfileId]) REFERENCES [SupplierProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Invitations_Tenders_TenderId] FOREIGN KEY ([TenderId]) REFERENCES [Tenders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [TenderCommitteeMembers] (
        [Id] int NOT NULL IDENTITY,
        [TenderId] int NOT NULL,
        [EmployeeProfileId] int NOT NULL,
        [CommitteeType] nvarchar(max) NOT NULL,
        [MemberRole] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_TenderCommitteeMembers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TenderCommitteeMembers_EmployeeProfiles_EmployeeProfileId] FOREIGN KEY ([EmployeeProfileId]) REFERENCES [EmployeeProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TenderCommitteeMembers_Tenders_TenderId] FOREIGN KEY ([TenderId]) REFERENCES [Tenders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [BidDocuments] (
        [Id] int NOT NULL IDENTITY,
        [BidId] int NOT NULL,
        [DocumentType] nvarchar(max) NOT NULL,
        [FileName] nvarchar(max) NOT NULL,
        [FilePath] nvarchar(max) NOT NULL,
        [UploadedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_BidDocuments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BidDocuments_Bids_BidId] FOREIGN KEY ([BidId]) REFERENCES [Bids] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [BidItems] (
        [Id] int NOT NULL IDENTITY,
        [BidId] int NOT NULL,
        [PurchaseRequisitionItemId] int NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [Quantity] int NOT NULL,
        [Notes] nvarchar(max) NULL,
        CONSTRAINT [PK_BidItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BidItems_Bids_BidId] FOREIGN KEY ([BidId]) REFERENCES [Bids] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_BidItems_PurchaseRequisitionItems_PurchaseRequisitionItemId] FOREIGN KEY ([PurchaseRequisitionItemId]) REFERENCES [PurchaseRequisitionItems] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [ClarificationRequests] (
        [Id] int NOT NULL IDENTITY,
        [SenderEmployeeProfileId] int NULL,
        [SenderSupplierProfileId] int NULL,
        [ReceiverEmployeeProfileId] int NULL,
        [ReceiverSupplierProfileId] int NULL,
        [Subject] nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [Response] nvarchar(max) NULL,
        [Status] nvarchar(max) NOT NULL,
        [PurchaseRequisitionId] int NULL,
        [TenderId] int NULL,
        [BidId] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        [AnsweredAt] datetime2 NULL,
        CONSTRAINT [PK_ClarificationRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ClarificationRequests_Bids_BidId] FOREIGN KEY ([BidId]) REFERENCES [Bids] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ClarificationRequests_EmployeeProfiles_ReceiverEmployeeProfileId] FOREIGN KEY ([ReceiverEmployeeProfileId]) REFERENCES [EmployeeProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ClarificationRequests_EmployeeProfiles_SenderEmployeeProfileId] FOREIGN KEY ([SenderEmployeeProfileId]) REFERENCES [EmployeeProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ClarificationRequests_PurchaseRequisitions_PurchaseRequisitionId] FOREIGN KEY ([PurchaseRequisitionId]) REFERENCES [PurchaseRequisitions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ClarificationRequests_SupplierProfiles_ReceiverSupplierProfileId] FOREIGN KEY ([ReceiverSupplierProfileId]) REFERENCES [SupplierProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ClarificationRequests_SupplierProfiles_SenderSupplierProfileId] FOREIGN KEY ([SenderSupplierProfileId]) REFERENCES [SupplierProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ClarificationRequests_Tenders_TenderId] FOREIGN KEY ([TenderId]) REFERENCES [Tenders] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [EvaluationReports] (
        [Id] int NOT NULL IDENTITY,
        [TenderId] int NOT NULL,
        [RecommendedBidId] int NOT NULL,
        [Summary] nvarchar(max) NOT NULL,
        [SupplierComparison] nvarchar(max) NOT NULL,
        [Recommendation] nvarchar(max) NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [PreparedByEmployeeProfileId] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_EvaluationReports] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EvaluationReports_Bids_RecommendedBidId] FOREIGN KEY ([RecommendedBidId]) REFERENCES [Bids] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_EvaluationReports_EmployeeProfiles_PreparedByEmployeeProfileId] FOREIGN KEY ([PreparedByEmployeeProfileId]) REFERENCES [EmployeeProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_EvaluationReports_Tenders_TenderId] FOREIGN KEY ([TenderId]) REFERENCES [Tenders] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [FinancialEvaluations] (
        [Id] int NOT NULL IDENTITY,
        [BidId] int NOT NULL,
        [EvaluationCriteriaId] int NOT NULL,
        [EvaluatorEmployeeProfileId] int NOT NULL,
        [TotalCost] decimal(18,2) NOT NULL,
        [TaxesAndFees] decimal(18,2) NOT NULL,
        [ExtraCosts] decimal(18,2) NOT NULL,
        [Score] decimal(18,2) NOT NULL,
        [Notes] nvarchar(max) NULL,
        CONSTRAINT [PK_FinancialEvaluations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FinancialEvaluations_Bids_BidId] FOREIGN KEY ([BidId]) REFERENCES [Bids] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_FinancialEvaluations_EmployeeProfiles_EvaluatorEmployeeProfileId] FOREIGN KEY ([EvaluatorEmployeeProfileId]) REFERENCES [EmployeeProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_FinancialEvaluations_EvaluationCriteria_EvaluationCriteriaId] FOREIGN KEY ([EvaluationCriteriaId]) REFERENCES [EvaluationCriteria] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [TechnicalEvaluations] (
        [Id] int NOT NULL IDENTITY,
        [BidId] int NOT NULL,
        [EvaluationCriteriaId] int NOT NULL,
        [EvaluatorEmployeeProfileId] int NOT NULL,
        [Score] decimal(18,2) NOT NULL,
        [Notes] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_TechnicalEvaluations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TechnicalEvaluations_Bids_BidId] FOREIGN KEY ([BidId]) REFERENCES [Bids] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TechnicalEvaluations_EmployeeProfiles_EvaluatorEmployeeProfileId] FOREIGN KEY ([EvaluatorEmployeeProfileId]) REFERENCES [EmployeeProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TechnicalEvaluations_EvaluationCriteria_EvaluationCriteriaId] FOREIGN KEY ([EvaluationCriteriaId]) REFERENCES [EvaluationCriteria] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE TABLE [TenderApprovals] (
        [Id] int NOT NULL IDENTITY,
        [EvaluationReportId] int NOT NULL,
        [ApproverEmployeeProfileId] int NOT NULL,
        [Decision] nvarchar(max) NOT NULL,
        [SelectedBidId] int NULL,
        [Comments] nvarchar(max) NULL,
        [DecidedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_TenderApprovals] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TenderApprovals_Bids_SelectedBidId] FOREIGN KEY ([SelectedBidId]) REFERENCES [Bids] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TenderApprovals_EmployeeProfiles_ApproverEmployeeProfileId] FOREIGN KEY ([ApproverEmployeeProfileId]) REFERENCES [EmployeeProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TenderApprovals_EvaluationReports_EvaluationReportId] FOREIGN KEY ([EvaluationReportId]) REFERENCES [EvaluationReports] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_EmployeeProfileId] ON [AuditLogs] ([EmployeeProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_SupplierProfileId] ON [AuditLogs] ([SupplierProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_BidDocuments_BidId] ON [BidDocuments] ([BidId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_BidItems_BidId] ON [BidItems] ([BidId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_BidItems_PurchaseRequisitionItemId] ON [BidItems] ([PurchaseRequisitionItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_Bids_SupplierProfileId] ON [Bids] ([SupplierProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Bids_TenderId_SupplierProfileId] ON [Bids] ([TenderId], [SupplierProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_ClarificationRequests_BidId] ON [ClarificationRequests] ([BidId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_ClarificationRequests_PurchaseRequisitionId] ON [ClarificationRequests] ([PurchaseRequisitionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_ClarificationRequests_ReceiverEmployeeProfileId] ON [ClarificationRequests] ([ReceiverEmployeeProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_ClarificationRequests_ReceiverSupplierProfileId] ON [ClarificationRequests] ([ReceiverSupplierProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_ClarificationRequests_SenderEmployeeProfileId] ON [ClarificationRequests] ([SenderEmployeeProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_ClarificationRequests_SenderSupplierProfileId] ON [ClarificationRequests] ([SenderSupplierProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_ClarificationRequests_TenderId] ON [ClarificationRequests] ([TenderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Departments_Name] ON [Departments] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_EmployeeProfiles_DepartmentId] ON [EmployeeProfiles] ([DepartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EmployeeProfiles_UserId] ON [EmployeeProfiles] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_EvaluationCriteria_TenderId] ON [EvaluationCriteria] ([TenderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_EvaluationReports_PreparedByEmployeeProfileId] ON [EvaluationReports] ([PreparedByEmployeeProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_EvaluationReports_RecommendedBidId] ON [EvaluationReports] ([RecommendedBidId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EvaluationReports_TenderId] ON [EvaluationReports] ([TenderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_FinancialEvaluations_BidId] ON [FinancialEvaluations] ([BidId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_FinancialEvaluations_EvaluationCriteriaId] ON [FinancialEvaluations] ([EvaluationCriteriaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_FinancialEvaluations_EvaluatorEmployeeProfileId] ON [FinancialEvaluations] ([EvaluatorEmployeeProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_Invitations_SupplierProfileId] ON [Invitations] ([SupplierProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_Invitations_TenderId] ON [Invitations] ([TenderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_Notifications_EmployeeProfileId] ON [Notifications] ([EmployeeProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_Notifications_SupplierProfileId] ON [Notifications] ([SupplierProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_PurchaseRequisitionItems_PurchaseRequisitionId] ON [PurchaseRequisitionItems] ([PurchaseRequisitionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_PurchaseRequisitions_CreatedByEmployeeProfileId] ON [PurchaseRequisitions] ([CreatedByEmployeeProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_PurchaseRequisitions_DepartmentId] ON [PurchaseRequisitions] ([DepartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_PurchaseRequisitions_ReviewedByEmployeeProfileId] ON [PurchaseRequisitions] ([ReviewedByEmployeeProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_SupplierDocuments_SupplierProfileId] ON [SupplierDocuments] ([SupplierProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SupplierProfiles_TaxNumber] ON [SupplierProfiles] ([TaxNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SupplierProfiles_UserId] ON [SupplierProfiles] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_TechnicalEvaluations_BidId] ON [TechnicalEvaluations] ([BidId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_TechnicalEvaluations_EvaluationCriteriaId] ON [TechnicalEvaluations] ([EvaluationCriteriaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_TechnicalEvaluations_EvaluatorEmployeeProfileId] ON [TechnicalEvaluations] ([EvaluatorEmployeeProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_TenderApprovals_ApproverEmployeeProfileId] ON [TenderApprovals] ([ApproverEmployeeProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_TenderApprovals_EvaluationReportId] ON [TenderApprovals] ([EvaluationReportId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_TenderApprovals_SelectedBidId] ON [TenderApprovals] ([SelectedBidId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_TenderCommitteeMembers_EmployeeProfileId] ON [TenderCommitteeMembers] ([EmployeeProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_TenderCommitteeMembers_TenderId] ON [TenderCommitteeMembers] ([TenderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE INDEX [IX_Tenders_CreatedByEmployeeProfileId] ON [Tenders] ([CreatedByEmployeeProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tenders_PurchaseRequisitionId] ON [Tenders] ([PurchaseRequisitionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112204850_FirstMigration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261112204850_FirstMigration', N'10.0.14');
END;

COMMIT;
GO

