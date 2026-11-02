/* =====================================================================
   AeroAuth - QA Authorization Management Portal
   schema.sql  (Phase 2: physical SQL Server schema)

   - Re-runnable: every object is created only if it does not exist yet.
   - Run it in SSMS, Azure Data Studio or sqlcmd (the GO lines are batch separators).
   - Then run seed.sql.

   Conventions
   - Surrogate INT IDENTITY keys, NVARCHAR text, DATETIME2(3) UTC timestamps, DATE for calendar dates.
   - Tenant isolation: Organizations is the tenant. Staff, TrainingSessions, SessionAttendees, Certificates and
     ReviewQueue carry OrganizationId, and composite foreign keys force it to match the station, trainer, session
     and staff they point to. Every tenant query can therefore filter on OrganizationId alone.
   - Reference data (aircraft types, issuing NAAs, freeze reasons) is switched off with IsActive = 0, never deleted.
   - AdminActionsLog and RemarksLog are append-only (triggers + DENY on the application role).
   ===================================================================== */

IF DB_ID(N'AeroAuthDb') IS NULL
    CREATE DATABASE AeroAuthDb;
GO

USE AeroAuthDb;
GO

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;   -- required for the filtered indexes
SET ANSI_NULLS ON;
GO

/* ---------------------------------------------------------------------
   1. Tenant and master tables
   --------------------------------------------------------------------- */

IF OBJECT_ID(N'dbo.Organizations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Organizations (
        OrganizationId      INT IDENTITY(1,1)  NOT NULL CONSTRAINT PK_Organizations PRIMARY KEY,
        Name                NVARCHAR(200)      NOT NULL,
        Abbreviation        NVARCHAR(20)       NOT NULL,
        QualityContactName  NVARCHAR(150)      NULL,
        QualityContactEmail NVARCHAR(256)      NULL,
        QualityContactPhone NVARCHAR(40)       NULL,
        ContractStart       DATE               NOT NULL,
        ContractEnd         DATE               NULL,
        IsActive            BIT                NOT NULL CONSTRAINT DF_Organizations_IsActive DEFAULT 1,
        CreatedAt           DATETIME2(3)       NOT NULL CONSTRAINT DF_Organizations_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt           DATETIME2(3)       NULL,
        CONSTRAINT UQ_Organizations_Name         UNIQUE (Name),
        CONSTRAINT UQ_Organizations_Abbreviation UNIQUE (Abbreviation),
        CONSTRAINT CK_Organizations_ContractDates CHECK (ContractEnd IS NULL OR ContractEnd >= ContractStart)
    );
END
GO

IF OBJECT_ID(N'dbo.Stations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Stations (
        StationId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Stations PRIMARY KEY,
        OrganizationId INT               NOT NULL,
        StationCode    NVARCHAR(10)      NOT NULL,   -- RUH, JED, DXB ...
        City           NVARCHAR(100)     NOT NULL,
        IsActive       BIT               NOT NULL CONSTRAINT DF_Stations_IsActive DEFAULT 1,
        CreatedAt      DATETIME2(3)      NOT NULL CONSTRAINT DF_Stations_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Stations_Organizations FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations (OrganizationId),
        CONSTRAINT UQ_Stations_Org_Code UNIQUE (OrganizationId, StationCode),
        CONSTRAINT UQ_Stations_Org_Id   UNIQUE (OrganizationId, StationId)      -- target of composite foreign keys
    );
END
GO

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users (
        UserId         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        IdentityName   NVARCHAR(256)     NOT NULL,   -- signed-in identity (UPN or email)
        DisplayName    NVARCHAR(150)     NULL,
        Role           NVARCHAR(12)      NOT NULL,
        OrganizationId INT               NULL,       -- required for ExternalAMO, NULL for internal QA
        IsActive       BIT               NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1,
        CreatedAt      DATETIME2(3)      NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_Users_IdentityName UNIQUE (IdentityName),
        CONSTRAINT FK_Users_Organizations FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations (OrganizationId),
        CONSTRAINT CK_Users_Role CHECK (Role IN ('AdminQA', 'InternalQA', 'ExternalAMO')),
        CONSTRAINT CK_Users_RoleOrganization CHECK (
               (Role = 'ExternalAMO' AND OrganizationId IS NOT NULL)
            OR (Role IN ('AdminQA', 'InternalQA') AND OrganizationId IS NULL))
    );
    CREATE INDEX IX_Users_OrganizationId ON dbo.Users (OrganizationId) WHERE OrganizationId IS NOT NULL;
END
GO

/* ---------------------------------------------------------------------
   2. Reference lists (managed by Admin QA)
   --------------------------------------------------------------------- */

IF OBJECT_ID(N'dbo.IssuingNaas', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IssuingNaas (
        NaaId    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_IssuingNaas PRIMARY KEY,
        Code     NVARCHAR(20)      NOT NULL,
        Name     NVARCHAR(200)     NOT NULL,
        IsActive BIT               NOT NULL CONSTRAINT DF_IssuingNaas_IsActive DEFAULT 1,
        CONSTRAINT UQ_IssuingNaas_Code UNIQUE (Code)
    );
END
GO

IF OBJECT_ID(N'dbo.AircraftTypes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AircraftTypes (
        AircraftTypeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AircraftTypes PRIMARY KEY,
        Name           NVARCHAR(100)     NOT NULL,
        IsActive       BIT               NOT NULL CONSTRAINT DF_AircraftTypes_IsActive DEFAULT 1,
        CONSTRAINT UQ_AircraftTypes_Name UNIQUE (Name)
    );
END
GO

IF OBJECT_ID(N'dbo.FreezeReasons', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FreezeReasons (
        FreezeReasonId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FreezeReasons PRIMARY KEY,
        Reason         NVARCHAR(200)     NOT NULL,
        IsActive       BIT               NOT NULL CONSTRAINT DF_FreezeReasons_IsActive DEFAULT 1,
        CONSTRAINT UQ_FreezeReasons_Reason UNIQUE (Reason)
    );
END
GO

-- Pre-populated authorities (re-runnable)
INSERT INTO dbo.IssuingNaas (Code, Name)
SELECT v.Code, v.Name
FROM (VALUES
    (N'CAASL', N'Civil Aviation Authority of Sri Lanka'),
    (N'GACA',  N'General Authority of Civil Aviation (Saudi Arabia)'),
    (N'EASA',  N'European Union Aviation Safety Agency'),
    (N'FAA',   N'Federal Aviation Administration (United States)'),
    (N'DGCA',  N'Directorate General of Civil Aviation'),
    (N'GCAA',  N'General Civil Aviation Authority (United Arab Emirates)'),
    (N'PCAA',  N'Pakistan Civil Aviation Authority')
) AS v (Code, Name)
WHERE NOT EXISTS (SELECT 1 FROM dbo.IssuingNaas n WHERE n.Code = v.Code);
GO

/* ---------------------------------------------------------------------
   3. Staff, licenses and scope
   --------------------------------------------------------------------- */

IF OBJECT_ID(N'dbo.Staff', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Staff (
        StaffId          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Staff PRIMARY KEY,
        OrganizationId   INT               NOT NULL,
        StationId        INT               NOT NULL,
        StaffNumber      NVARCHAR(30)      NOT NULL,
        FullName         NVARCHAR(150)     NOT NULL,
        Email            NVARCHAR(256)     NULL,
        SignaturePath    NVARCHAR(500)     NULL,
        StampPath        NVARCHAR(500)     NULL,
        IsActive         BIT               NOT NULL CONSTRAINT DF_Staff_IsActive  DEFAULT 1,
        IsFrozen         BIT               NOT NULL CONSTRAINT DF_Staff_IsFrozen  DEFAULT 0,
        IsTrainer        BIT               NOT NULL CONSTRAINT DF_Staff_IsTrainer DEFAULT 0,
        ReviewStatus     NVARCHAR(10)      NOT NULL CONSTRAINT DF_Staff_ReviewStatus DEFAULT 'Pending',
        ReviewedByUserId INT               NULL,
        ReviewedAt       DATETIME2(3)      NULL,
        ReviewComment    NVARCHAR(500)     NULL,
        CreatedByUserId  INT               NULL,
        CreatedAt        DATETIME2(3)      NOT NULL CONSTRAINT DF_Staff_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt        DATETIME2(3)      NULL,
        -- the station must belong to the same organization as the staff member
        CONSTRAINT FK_Staff_Stations FOREIGN KEY (OrganizationId, StationId) REFERENCES dbo.Stations (OrganizationId, StationId),
        CONSTRAINT FK_Staff_ReviewedBy FOREIGN KEY (ReviewedByUserId) REFERENCES dbo.Users (UserId),
        CONSTRAINT FK_Staff_CreatedBy  FOREIGN KEY (CreatedByUserId)  REFERENCES dbo.Users (UserId),
        CONSTRAINT UQ_Staff_Org_Number UNIQUE (OrganizationId, StaffNumber),
        CONSTRAINT UQ_Staff_Org_Id     UNIQUE (OrganizationId, StaffId),
        CONSTRAINT CK_Staff_ReviewStatus CHECK (ReviewStatus IN ('Pending', 'Approved', 'Rejected')),
        CONSTRAINT CK_Staff_Review CHECK (
               (ReviewStatus = 'Pending' AND ReviewedAt IS NULL AND ReviewedByUserId IS NULL)
            OR (ReviewStatus IN ('Approved', 'Rejected') AND ReviewedAt IS NOT NULL AND ReviewedByUserId IS NOT NULL))
    );
    CREATE UNIQUE INDEX UX_Staff_Org_Email ON dbo.Staff (OrganizationId, Email) WHERE Email IS NOT NULL;
    CREATE INDEX IX_Staff_Station    ON dbo.Staff (StationId);
    CREATE INDEX IX_Staff_Org_Review ON dbo.Staff (OrganizationId, ReviewStatus);
END
GO

IF OBJECT_ID(N'dbo.StaffAml', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StaffAml (
        StaffAmlId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StaffAml PRIMARY KEY,
        StaffId    INT               NOT NULL,
        NaaId      INT               NOT NULL,      -- issuing authority
        AmlNumber  NVARCHAR(50)      NOT NULL,
        IssueDate  DATE              NULL,
        ExpiryDate DATE              NULL,
        CreatedAt  DATETIME2(3)      NOT NULL CONSTRAINT DF_StaffAml_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_StaffAml_Staff FOREIGN KEY (StaffId) REFERENCES dbo.Staff (StaffId),
        CONSTRAINT FK_StaffAml_Naa   FOREIGN KEY (NaaId)   REFERENCES dbo.IssuingNaas (NaaId),
        CONSTRAINT UQ_StaffAml_Naa_Number UNIQUE (NaaId, AmlNumber),
        CONSTRAINT UQ_StaffAml_Staff_Id   UNIQUE (StaffId, StaffAmlId),   -- target: the AML must belong to the same staff member
        CONSTRAINT UQ_StaffAml_Id_Naa     UNIQUE (StaffAmlId, NaaId),     -- target: the backing NAA must match the AML
        CONSTRAINT CK_StaffAml_Dates CHECK (ExpiryDate IS NULL OR IssueDate IS NULL OR ExpiryDate >= IssueDate)
    );
    CREATE INDEX IX_StaffAml_Staff ON dbo.StaffAml (StaffId);
END
GO

IF OBJECT_ID(N'dbo.StaffScope', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StaffScope (
        ScopeId        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StaffScope PRIMARY KEY,
        StaffId        INT               NOT NULL,
        AircraftTypeId INT               NOT NULL,
        ScopeBasis     NVARCHAR(12)      NOT NULL,  -- AMLEndorsed or MOETaskBased
        StaffAmlId     INT               NULL,      -- required when AMLEndorsed
        BackingNaaId   INT               NULL,      -- required when AMLEndorsed
        Category       NVARCHAR(50)      NOT NULL,
        Level          NVARCHAR(30)      NOT NULL,
        Location       NVARCHAR(100)     NULL,
        EffectiveFrom  DATE              NOT NULL,
        EffectiveTo    DATE              NULL,
        CreatedAt      DATETIME2(3)      NOT NULL CONSTRAINT DF_StaffScope_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt      DATETIME2(3)      NULL,
        CONSTRAINT FK_StaffScope_Staff        FOREIGN KEY (StaffId)        REFERENCES dbo.Staff (StaffId),
        CONSTRAINT FK_StaffScope_AircraftType FOREIGN KEY (AircraftTypeId) REFERENCES dbo.AircraftTypes (AircraftTypeId),
        CONSTRAINT FK_StaffScope_StaffAml     FOREIGN KEY (StaffId, StaffAmlId)       REFERENCES dbo.StaffAml (StaffId, StaffAmlId),
        CONSTRAINT FK_StaffScope_BackingNaa   FOREIGN KEY (StaffAmlId, BackingNaaId)  REFERENCES dbo.StaffAml (StaffAmlId, NaaId),
        CONSTRAINT CK_StaffScope_Basis CHECK (ScopeBasis IN ('AMLEndorsed', 'MOETaskBased')),
        CONSTRAINT CK_StaffScope_BasisLinks CHECK (
               (ScopeBasis = 'AMLEndorsed'  AND StaffAmlId IS NOT NULL AND BackingNaaId IS NOT NULL)
            OR (ScopeBasis = 'MOETaskBased' AND StaffAmlId IS NULL     AND BackingNaaId IS NULL)),
        CONSTRAINT CK_StaffScope_Dates CHECK (EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom),
        CONSTRAINT UQ_StaffScope_Row UNIQUE (StaffId, AircraftTypeId, Category, Level, Location, EffectiveFrom)
    );
    CREATE INDEX IX_StaffScope_Staff    ON dbo.StaffScope (StaffId);
    CREATE INDEX IX_StaffScope_StaffAml ON dbo.StaffScope (StaffAmlId) WHERE StaffAmlId IS NOT NULL;
END
GO

/* ---------------------------------------------------------------------
   4. Training and workflow
   --------------------------------------------------------------------- */

IF OBJECT_ID(N'dbo.TrainingMaterials', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TrainingMaterials (
        MaterialId       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TrainingMaterials PRIMARY KEY,
        Title            NVARCHAR(200)     NOT NULL,
        ModuleType       NVARCHAR(10)      NOT NULL,
        Edition          NVARCHAR(30)      NOT NULL,
        FilePath         NVARCHAR(500)     NOT NULL,
        IsCurrent        BIT               NOT NULL CONSTRAINT DF_TrainingMaterials_IsCurrent DEFAULT 1,
        UploadedByUserId INT               NULL,
        CreatedAt        DATETIME2(3)      NOT NULL CONSTRAINT DF_TrainingMaterials_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_TrainingMaterials_UploadedBy FOREIGN KEY (UploadedByUserId) REFERENCES dbo.Users (UserId),
        CONSTRAINT CK_TrainingMaterials_Type CHECK (ModuleType IN ('Procedures', 'Refresher', 'Adhoc')),
        CONSTRAINT UQ_TrainingMaterials_Title_Edition UNIQUE (Title, Edition),
        CONSTRAINT UQ_TrainingMaterials_Id_Type UNIQUE (MaterialId, ModuleType)
    );
    -- one current edition per title; older editions stay on file with IsCurrent = 0
    CREATE UNIQUE INDEX UX_TrainingMaterials_CurrentTitle ON dbo.TrainingMaterials (Title) WHERE IsCurrent = 1;
END
GO

IF OBJECT_ID(N'dbo.TrainingSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TrainingSessions (
        SessionId           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TrainingSessions PRIMARY KEY,
        OrganizationId      INT               NOT NULL,
        StationId           INT               NOT NULL,
        TrainerStaffId      INT               NOT NULL,
        SessionDate         DATE              NOT NULL,
        Venue               NVARCHAR(200)     NULL,
        AttendanceSheetPath NVARCHAR(500)     NULL,
        Status              NVARCHAR(10)      NOT NULL CONSTRAINT DF_TrainingSessions_Status DEFAULT 'Pending',
        ReviewedByUserId    INT               NULL,
        ReviewedAt          DATETIME2(3)      NULL,
        ReviewComment       NVARCHAR(500)     NULL,
        CreatedByUserId     INT               NULL,
        CreatedAt           DATETIME2(3)      NOT NULL CONSTRAINT DF_TrainingSessions_CreatedAt DEFAULT SYSUTCDATETIME(),
        -- station and trainer must both belong to the session's organization
        CONSTRAINT FK_TrainingSessions_Stations FOREIGN KEY (OrganizationId, StationId)      REFERENCES dbo.Stations (OrganizationId, StationId),
        CONSTRAINT FK_TrainingSessions_Trainer  FOREIGN KEY (OrganizationId, TrainerStaffId) REFERENCES dbo.Staff (OrganizationId, StaffId),
        CONSTRAINT FK_TrainingSessions_ReviewedBy FOREIGN KEY (ReviewedByUserId) REFERENCES dbo.Users (UserId),
        CONSTRAINT FK_TrainingSessions_CreatedBy  FOREIGN KEY (CreatedByUserId)  REFERENCES dbo.Users (UserId),
        CONSTRAINT UQ_TrainingSessions_Org_Id UNIQUE (OrganizationId, SessionId),
        CONSTRAINT CK_TrainingSessions_Status CHECK (Status IN ('Pending', 'Approved', 'Rejected')),
        CONSTRAINT CK_TrainingSessions_Review CHECK (
               (Status = 'Pending' AND ReviewedAt IS NULL AND ReviewedByUserId IS NULL)
            OR (Status IN ('Approved', 'Rejected') AND ReviewedAt IS NOT NULL AND ReviewedByUserId IS NOT NULL))
    );
    CREATE INDEX IX_TrainingSessions_Org_Status ON dbo.TrainingSessions (OrganizationId, Status, SessionDate);
    CREATE INDEX IX_TrainingSessions_Trainer    ON dbo.TrainingSessions (TrainerStaffId);
END
GO

IF OBJECT_ID(N'dbo.SessionModules', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SessionModules (
        SessionModuleId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SessionModules PRIMARY KEY,
        SessionId       INT               NOT NULL,
        MaterialId      INT               NOT NULL,
        ModuleType      NVARCHAR(10)      NOT NULL,
        CONSTRAINT FK_SessionModules_Session  FOREIGN KEY (SessionId) REFERENCES dbo.TrainingSessions (SessionId),
        -- the module type must match the type of the material edition used
        CONSTRAINT FK_SessionModules_Material FOREIGN KEY (MaterialId, ModuleType) REFERENCES dbo.TrainingMaterials (MaterialId, ModuleType),
        CONSTRAINT UQ_SessionModules_Session_Material UNIQUE (SessionId, MaterialId),
        CONSTRAINT UQ_SessionModules_Session_Id UNIQUE (SessionId, SessionModuleId)
    );
    CREATE INDEX IX_SessionModules_Material ON dbo.SessionModules (MaterialId);
END
GO

IF OBJECT_ID(N'dbo.SessionAttendees', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SessionAttendees (
        AttendeeId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SessionAttendees PRIMARY KEY,
        OrganizationId INT               NOT NULL,
        SessionId      INT               NOT NULL,
        StaffId        INT               NOT NULL,
        MissingEmail   BIT               NOT NULL CONSTRAINT DF_SessionAttendees_MissingEmail DEFAULT 0,
        RefresherGap   BIT               NOT NULL CONSTRAINT DF_SessionAttendees_RefresherGap DEFAULT 0,
        -- session and attendee must belong to the same organization
        CONSTRAINT FK_SessionAttendees_Session FOREIGN KEY (OrganizationId, SessionId) REFERENCES dbo.TrainingSessions (OrganizationId, SessionId),
        CONSTRAINT FK_SessionAttendees_Staff   FOREIGN KEY (OrganizationId, StaffId)   REFERENCES dbo.Staff (OrganizationId, StaffId),
        CONSTRAINT UQ_SessionAttendees_Session_Staff UNIQUE (SessionId, StaffId),
        CONSTRAINT UQ_SessionAttendees_Session_Id    UNIQUE (SessionId, AttendeeId)
    );
    CREATE INDEX IX_SessionAttendees_Staff ON dbo.SessionAttendees (StaffId);
END
GO

IF OBJECT_ID(N'dbo.AttendeeModuleAcknowledgments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AttendeeModuleAcknowledgments (
        AckId           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttendeeModuleAcknowledgments PRIMARY KEY,
        SessionId       INT               NOT NULL,
        AttendeeId      INT               NOT NULL,
        SessionModuleId INT               NOT NULL,
        AcknowledgedAt  DATETIME2(3)      NOT NULL CONSTRAINT DF_AttendeeModuleAck_At DEFAULT SYSUTCDATETIME(),
        -- the attendee and the module must belong to the same session
        CONSTRAINT FK_AttendeeModuleAck_Attendee FOREIGN KEY (SessionId, AttendeeId)      REFERENCES dbo.SessionAttendees (SessionId, AttendeeId),
        CONSTRAINT FK_AttendeeModuleAck_Module   FOREIGN KEY (SessionId, SessionModuleId) REFERENCES dbo.SessionModules (SessionId, SessionModuleId),
        CONSTRAINT UQ_AttendeeModuleAck UNIQUE (AttendeeId, SessionModuleId)
    );
    CREATE INDEX IX_AttendeeModuleAck_Module ON dbo.AttendeeModuleAcknowledgments (SessionModuleId);
END
GO

-- Certificate records created when a training session is approved. A void keeps the row; a reissue adds a new one.
IF OBJECT_ID(N'dbo.Certificates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Certificates (
        CertificateId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Certificates PRIMARY KEY,
        OrganizationId    INT               NOT NULL,
        SessionId         INT               NOT NULL,
        AttendeeId        INT               NOT NULL,
        CertificateNumber NVARCHAR(60)      NOT NULL,   -- e.g. KAC/ULPROC/2026/0001
        Status            NVARCHAR(10)      NOT NULL CONSTRAINT DF_Certificates_Status DEFAULT 'Issued',
        IssuedAt          DATETIME2(3)      NOT NULL CONSTRAINT DF_Certificates_IssuedAt DEFAULT SYSUTCDATETIME(),
        IssuedByUserId    INT               NULL,
        VoidedAt          DATETIME2(3)      NULL,
        VoidReason        NVARCHAR(500)     NULL,
        CONSTRAINT FK_Certificates_Session  FOREIGN KEY (OrganizationId, SessionId) REFERENCES dbo.TrainingSessions (OrganizationId, SessionId),
        CONSTRAINT FK_Certificates_Attendee FOREIGN KEY (SessionId, AttendeeId)     REFERENCES dbo.SessionAttendees (SessionId, AttendeeId),
        CONSTRAINT FK_Certificates_IssuedBy FOREIGN KEY (IssuedByUserId)            REFERENCES dbo.Users (UserId),
        CONSTRAINT UQ_Certificates_Number UNIQUE (CertificateNumber),
        CONSTRAINT CK_Certificates_Status CHECK (Status IN ('Issued', 'Voided')),
        CONSTRAINT CK_Certificates_Void CHECK (
               (Status = 'Issued' AND VoidedAt IS NULL)
            OR (Status = 'Voided' AND VoidedAt IS NOT NULL AND VoidReason IS NOT NULL))
    );
    -- only one live certificate per attendee
    CREATE UNIQUE INDEX UX_Certificates_LiveAttendee ON dbo.Certificates (AttendeeId) WHERE Status = 'Issued';
    CREATE INDEX IX_Certificates_Org ON dbo.Certificates (OrganizationId, SessionId);
END
GO

-- QA review queue: one open row per submitted record. ItemId points at Staff or TrainingSessions according to ItemType
-- (polymorphic, so it is enforced by the application, not by a foreign key).
IF OBJECT_ID(N'dbo.ReviewQueue', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReviewQueue (
        ReviewQueueId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReviewQueue PRIMARY KEY,
        OrganizationId    INT               NOT NULL,
        ItemType          NVARCHAR(20)      NOT NULL,
        ItemId            INT               NOT NULL,
        Summary           NVARCHAR(300)     NULL,
        Status            NVARCHAR(10)      NOT NULL CONSTRAINT DF_ReviewQueue_Status DEFAULT 'Pending',
        SubmittedByUserId INT               NULL,
        SubmittedAt       DATETIME2(3)      NOT NULL CONSTRAINT DF_ReviewQueue_SubmittedAt DEFAULT SYSUTCDATETIME(),
        ReviewedByUserId  INT               NULL,
        ReviewedAt        DATETIME2(3)      NULL,
        ReviewComment     NVARCHAR(500)     NULL,
        CONSTRAINT FK_ReviewQueue_Organizations FOREIGN KEY (OrganizationId)    REFERENCES dbo.Organizations (OrganizationId),
        CONSTRAINT FK_ReviewQueue_SubmittedBy   FOREIGN KEY (SubmittedByUserId) REFERENCES dbo.Users (UserId),
        CONSTRAINT FK_ReviewQueue_ReviewedBy    FOREIGN KEY (ReviewedByUserId)  REFERENCES dbo.Users (UserId),
        CONSTRAINT CK_ReviewQueue_ItemType CHECK (ItemType IN ('Staff', 'TrainingSession')),
        CONSTRAINT CK_ReviewQueue_Status   CHECK (Status IN ('Pending', 'Approved', 'Rejected')),
        CONSTRAINT CK_ReviewQueue_Review CHECK (
               (Status = 'Pending' AND ReviewedAt IS NULL AND ReviewedByUserId IS NULL)
            OR (Status IN ('Approved', 'Rejected') AND ReviewedAt IS NOT NULL AND ReviewedByUserId IS NOT NULL))
    );
    -- a record can only have one open review item at a time
    CREATE UNIQUE INDEX UX_ReviewQueue_OpenItem ON dbo.ReviewQueue (ItemType, ItemId) WHERE Status = 'Pending';
    CREATE INDEX IX_ReviewQueue_Org_Status ON dbo.ReviewQueue (OrganizationId, Status, SubmittedAt);
END
GO

/* ---------------------------------------------------------------------
   5. CAASL approvals, remarks, audit and counters
   --------------------------------------------------------------------- */

IF OBJECT_ID(N'dbo.CaaslApprovals', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CaaslApprovals (
        ApprovalId         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CaaslApprovals PRIMARY KEY,
        OrganizationId     INT               NOT NULL,
        ReferenceNumber    NVARCHAR(60)      NOT NULL,
        ValidTo            DATE              NOT NULL,
        Status             NVARCHAR(12)      NOT NULL CONSTRAINT DF_CaaslApprovals_Status DEFAULT 'Active',
        LastReminderSentAt DATETIME2(3)      NULL,   -- written by the scheduled expiry job
        StopWorkNotifiedAt DATETIME2(3)      NULL,   -- written by the scheduled expiry job
        CreatedAt          DATETIME2(3)      NOT NULL CONSTRAINT DF_CaaslApprovals_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt          DATETIME2(3)      NULL,
        CONSTRAINT FK_CaaslApprovals_Organizations FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations (OrganizationId),
        CONSTRAINT UQ_CaaslApprovals_Org_Reference UNIQUE (OrganizationId, ReferenceNumber),
        CONSTRAINT CK_CaaslApprovals_Status CHECK (Status IN ('Active', 'Discontinued'))
    );
    CREATE INDEX IX_CaaslApprovals_ActiveExpiry ON dbo.CaaslApprovals (ValidTo) INCLUDE (OrganizationId) WHERE Status = 'Active';
    CREATE INDEX IX_CaaslApprovals_Org ON dbo.CaaslApprovals (OrganizationId);
END
GO

IF OBJECT_ID(N'dbo.RemarksLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RemarksLog (
        RemarkId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RemarksLog PRIMARY KEY,
        ApprovalId INT               NOT NULL,
        UserId     INT               NOT NULL,
        Remark     NVARCHAR(1000)    NOT NULL,
        CreatedAt  DATETIME2(3)      NOT NULL CONSTRAINT DF_RemarksLog_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_RemarksLog_Approval FOREIGN KEY (ApprovalId) REFERENCES dbo.CaaslApprovals (ApprovalId),
        CONSTRAINT FK_RemarksLog_User     FOREIGN KEY (UserId)     REFERENCES dbo.Users (UserId)
    );
    CREATE INDEX IX_RemarksLog_Approval ON dbo.RemarksLog (ApprovalId, CreatedAt);
END
GO

IF OBJECT_ID(N'dbo.AdminActionsLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdminActionsLog (
        ActionId       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdminActionsLog PRIMARY KEY,
        UserId         INT                  NOT NULL,   -- who performed the action
        ActionType     NVARCHAR(12)         NOT NULL,
        StaffId        INT                  NULL,       -- target staff member, when there is one
        FreezeReasonId INT                  NULL,       -- mandatory for Freeze
        TargetTable    NVARCHAR(100)        NULL,       -- for edits and overrides on other tables
        TargetKey      NVARCHAR(50)         NULL,
        OldValue       NVARCHAR(MAX)        NULL,
        NewValue       NVARCHAR(MAX)        NULL,
        Notes          NVARCHAR(500)        NULL,
        ActionAt       DATETIME2(3)         NOT NULL CONSTRAINT DF_AdminActionsLog_ActionAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_AdminActionsLog_User   FOREIGN KEY (UserId)         REFERENCES dbo.Users (UserId),
        CONSTRAINT FK_AdminActionsLog_Staff  FOREIGN KEY (StaffId)        REFERENCES dbo.Staff (StaffId),
        CONSTRAINT FK_AdminActionsLog_Reason FOREIGN KEY (FreezeReasonId) REFERENCES dbo.FreezeReasons (FreezeReasonId),
        CONSTRAINT CK_AdminActionsLog_Type CHECK (ActionType IN ('Freeze', 'Unfreeze', 'DirectEdit', 'Override')),
        CONSTRAINT CK_AdminActionsLog_Freeze   CHECK (ActionType <> 'Freeze'   OR (FreezeReasonId IS NOT NULL AND StaffId IS NOT NULL)),
        CONSTRAINT CK_AdminActionsLog_Unfreeze CHECK (ActionType <> 'Unfreeze' OR StaffId IS NOT NULL)
    );
    CREATE INDEX IX_AdminActionsLog_Staff ON dbo.AdminActionsLog (StaffId, ActionAt) WHERE StaffId IS NOT NULL;
    CREATE INDEX IX_AdminActionsLog_User  ON dbo.AdminActionsLog (UserId, ActionAt);
END
GO

IF OBJECT_ID(N'dbo.SystemCounters', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SystemCounters (
        CounterId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SystemCounters PRIMARY KEY,
        OrganizationId INT               NULL,          -- NULL = global counter
        CounterKey     NVARCHAR(40)      NOT NULL,      -- e.g. ULPROC, QARPT
        CounterYear    INT               NOT NULL,
        LastValue      INT               NOT NULL CONSTRAINT DF_SystemCounters_LastValue DEFAULT 0,
        UpdatedAt      DATETIME2(3)      NULL,
        CONSTRAINT FK_SystemCounters_Organizations FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations (OrganizationId),
        CONSTRAINT CK_SystemCounters_LastValue CHECK (LastValue >= 0)
    );
    CREATE UNIQUE INDEX UX_SystemCounters_Global ON dbo.SystemCounters (CounterKey, CounterYear) WHERE OrganizationId IS NULL;
    CREATE UNIQUE INDEX UX_SystemCounters_Org    ON dbo.SystemCounters (OrganizationId, CounterKey, CounterYear) WHERE OrganizationId IS NOT NULL;
END
GO

/* ---------------------------------------------------------------------
   6. Triggers (append-only audit), stored procedure (counters)
   --------------------------------------------------------------------- */

CREATE OR ALTER TRIGGER dbo.trg_AdminActionsLog_AppendOnly ON dbo.AdminActionsLog
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51000, 'AdminActionsLog is append-only. Updates and deletes are not allowed.', 1;
END;
GO

CREATE OR ALTER TRIGGER dbo.trg_RemarksLog_AppendOnly ON dbo.RemarksLog
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51001, 'RemarksLog is append-only. Updates and deletes are not allowed.', 1;
END;
GO

-- Returns the next number for a counter and creates the counter on first use. @OrganizationId = NULL means a global counter.
-- Call it inside the caller's transaction so a rollback also gives the number back.
CREATE OR ALTER PROCEDURE dbo.usp_GetNextCounter
    @OrganizationId INT = NULL,
    @CounterKey     NVARCHAR(40),
    @CounterYear    INT,
    @NextValue      INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    UPDATE dbo.SystemCounters WITH (UPDLOCK, HOLDLOCK)
       SET LastValue  = LastValue + 1,
           @NextValue = LastValue + 1,
           UpdatedAt  = SYSUTCDATETIME()
     WHERE CounterKey = @CounterKey
       AND CounterYear = @CounterYear
       AND (OrganizationId = @OrganizationId OR (OrganizationId IS NULL AND @OrganizationId IS NULL));

    IF @@ROWCOUNT = 0
    BEGIN
        INSERT INTO dbo.SystemCounters (OrganizationId, CounterKey, CounterYear, LastValue)
        VALUES (@OrganizationId, @CounterKey, @CounterYear, 1);
        SET @NextValue = 1;
    END;

    COMMIT TRANSACTION;
END;
GO

/* ---------------------------------------------------------------------
   7. Application role (secondary data protection)
   Add the application's SQL login or Entra identity to this role.
   --------------------------------------------------------------------- */

IF DATABASE_PRINCIPAL_ID(N'AeroAuthApp') IS NULL
    CREATE ROLE AeroAuthApp;
GO

GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::dbo TO AeroAuthApp;
DENY  UPDATE, DELETE ON dbo.AdminActionsLog TO AeroAuthApp;
DENY  UPDATE, DELETE ON dbo.RemarksLog      TO AeroAuthApp;
GO

PRINT 'AeroAuthDb schema is ready.';
GO
