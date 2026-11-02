/* =====================================================================
   AeroAuth - seed.sql
   Baseline and test data. Run after schema.sql. Safe to run more than once (every insert checks first).
   Names are sample data for testing only.
   ===================================================================== */
USE AeroAuthDb;
GO
SET NOCOUNT ON;
GO

/* ---- Reference lists --------------------------------------------------------------- */
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

INSERT INTO dbo.FreezeReasons (Reason)
SELECT v.Reason
FROM (VALUES
    (N'Authorization under review'),
    (N'Training lapsed'),
    (N'Left the organization'),
    (N'Audit finding'),
    (N'CAASL approval expired')
) AS v (Reason)
WHERE NOT EXISTS (SELECT 1 FROM dbo.FreezeReasons f WHERE f.Reason = v.Reason);

INSERT INTO dbo.AircraftTypes (Name)
SELECT v.Name
FROM (VALUES (N'A320'), (N'A330'), (N'B777')) AS v (Name)
WHERE NOT EXISTS (SELECT 1 FROM dbo.AircraftTypes a WHERE a.Name = v.Name);
GO

/* ---- Organizations and stations ---------------------------------------------------- */
INSERT INTO dbo.Organizations (Name, Abbreviation, QualityContactName, QualityContactEmail, ContractStart)
SELECT v.Name, v.Abbreviation, v.ContactName, v.ContactEmail, v.ContractStart
FROM (VALUES
    (N'Kuwait Airways',                N'KAC',   N'KAC Quality Manager',   N'quality@kac.example',   CAST('2024-01-01' AS date)),
    (N'Royal Aircraft Maintenance Co.', N'RAMCO', N'RAMCO Quality Manager', N'quality@ramco.example', CAST('2024-06-01' AS date))
) AS v (Name, Abbreviation, ContactName, ContactEmail, ContractStart)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Organizations o WHERE o.Abbreviation = v.Abbreviation)
ORDER BY v.Abbreviation;

INSERT INTO dbo.Stations (OrganizationId, StationCode, City)
SELECT o.OrganizationId, v.StationCode, v.City
FROM (VALUES
    (N'KAC',   N'RUH', N'Riyadh'),
    (N'KAC',   N'JED', N'Jeddah'),
    (N'RAMCO', N'DXB', N'Dubai'),
    (N'RAMCO', N'RUH', N'Riyadh')      -- same station code at two organizations: tenants stay separate
) AS v (Org, StationCode, City)
JOIN dbo.Organizations o ON o.Abbreviation = v.Org
WHERE NOT EXISTS (SELECT 1 FROM dbo.Stations s WHERE s.OrganizationId = o.OrganizationId AND s.StationCode = v.StationCode);
GO

/* ---- Users (the identity name is what sign-in looks up) ---------------------------- */
INSERT INTO dbo.Users (IdentityName, DisplayName, Role, OrganizationId)
SELECT v.IdentityName, v.DisplayName, v.Role, o.OrganizationId
FROM (VALUES
    (N'admin@aeroauth.local',  N'QA Admin',     N'AdminQA',     CAST(NULL AS nvarchar(20))),
    (N'viewer@aeroauth.local', N'QA Viewer',    N'InternalQA',  NULL),
    (N'kac.user@aeroauth.local',   N'KAC Quality',   N'ExternalAMO', N'KAC'),
    (N'ramco.user@aeroauth.local', N'RAMCO Quality', N'ExternalAMO', N'RAMCO')
) AS v (IdentityName, DisplayName, Role, Org)
LEFT JOIN dbo.Organizations o ON o.Abbreviation = v.Org
WHERE NOT EXISTS (SELECT 1 FROM dbo.Users u WHERE u.IdentityName = v.IdentityName);
GO

/* ---- Training material: initial active Procedures edition (plus an obsolete one kept on file) */
INSERT INTO dbo.TrainingMaterials (Title, ModuleType, Edition, FilePath, IsCurrent)
SELECT v.Title, v.ModuleType, v.Edition, v.FilePath, v.IsCurrent
FROM (VALUES
    (N'Procedures Training Manual', N'Procedures', N'Rev 4', N'/materials/procedures-rev4.pdf', CAST(0 AS bit)),
    (N'Procedures Training Manual', N'Procedures', N'Rev 5', N'/materials/procedures-rev5.pdf', CAST(1 AS bit))
) AS v (Title, ModuleType, Edition, FilePath, IsCurrent)
WHERE NOT EXISTS (SELECT 1 FROM dbo.TrainingMaterials t WHERE t.Title = v.Title AND t.Edition = v.Edition);
GO

/* ---- Counters ---------------------------------------------------------------------- */
-- Procedures training certificates per organization (KAC/ULPROC/2026/0001) ...
INSERT INTO dbo.SystemCounters (OrganizationId, CounterKey, CounterYear, LastValue)
SELECT o.OrganizationId, N'ULPROC', 2026, 0
FROM dbo.Organizations o
WHERE NOT EXISTS (SELECT 1 FROM dbo.SystemCounters c WHERE c.OrganizationId = o.OrganizationId AND c.CounterKey = N'ULPROC' AND c.CounterYear = 2026);

-- ... and one global counter for the QA/RPT certifying staff register numbers
INSERT INTO dbo.SystemCounters (OrganizationId, CounterKey, CounterYear, LastValue)
SELECT NULL, N'QARPT', 2026, 0
WHERE NOT EXISTS (SELECT 1 FROM dbo.SystemCounters c WHERE c.OrganizationId IS NULL AND c.CounterKey = N'QARPT' AND c.CounterYear = 2026);
GO

/* ---- Test staff: one approved trainer and one pending person per organization ------- */
INSERT INTO dbo.Staff (OrganizationId, StationId, StaffNumber, FullName, Email, IsTrainer, ReviewStatus, ReviewedByUserId, ReviewedAt)
SELECT o.OrganizationId, st.StationId, v.StaffNumber, v.FullName, v.Email, 1, N'Approved', a.UserId, SYSUTCDATETIME()
FROM (VALUES
    (N'KAC',   N'RUH', N'K-1021', N'Omar Haddad', N'omar.haddad@kac.example'),
    (N'RAMCO', N'DXB', N'R-2007', N'Priya Menon', N'priya.menon@ramco.example')
) AS v (Org, StationCode, StaffNumber, FullName, Email)
JOIN dbo.Organizations o ON o.Abbreviation = v.Org
JOIN dbo.Stations st ON st.OrganizationId = o.OrganizationId AND st.StationCode = v.StationCode
CROSS JOIN (SELECT UserId FROM dbo.Users WHERE IdentityName = N'admin@aeroauth.local') a
WHERE NOT EXISTS (SELECT 1 FROM dbo.Staff s WHERE s.OrganizationId = o.OrganizationId AND s.StaffNumber = v.StaffNumber)
ORDER BY o.OrganizationId;

INSERT INTO dbo.Staff (OrganizationId, StationId, StaffNumber, FullName, Email, ReviewStatus, CreatedByUserId)
SELECT o.OrganizationId, st.StationId, v.StaffNumber, v.FullName, v.Email, N'Pending', u.UserId
FROM (VALUES
    (N'KAC',   N'JED', N'K-1090', N'Faisal Nasser', N'faisal.nasser@kac.example',  N'kac.user@aeroauth.local'),
    (N'RAMCO', N'RUH', N'R-2031', N'Karim Aziz',    N'karim.aziz@ramco.example',   N'ramco.user@aeroauth.local')
) AS v (Org, StationCode, StaffNumber, FullName, Email, SubmittedBy)
JOIN dbo.Organizations o ON o.Abbreviation = v.Org
JOIN dbo.Stations st ON st.OrganizationId = o.OrganizationId AND st.StationCode = v.StationCode
JOIN dbo.Users u ON u.IdentityName = v.SubmittedBy
WHERE NOT EXISTS (SELECT 1 FROM dbo.Staff s WHERE s.OrganizationId = o.OrganizationId AND s.StaffNumber = v.StaffNumber)
ORDER BY o.OrganizationId;
GO

-- Licenses
INSERT INTO dbo.StaffAml (StaffId, NaaId, AmlNumber, IssueDate, ExpiryDate)
SELECT s.StaffId, n.NaaId, v.AmlNumber, v.IssueDate, v.ExpiryDate
FROM (VALUES
    (N'K-1021', N'GACA',  N'GACA-44710', CAST('2024-08-01' AS date), CAST('2027-08-01' AS date)),
    (N'R-2007', N'GCAA',  N'GCAA-90822', CAST('2025-02-01' AS date), CAST('2028-02-01' AS date)),
    (N'K-1090', N'CAASL', N'CAASL-5120', CAST('2025-03-15' AS date), CAST('2028-03-15' AS date))
) AS v (StaffNumber, NaaCode, AmlNumber, IssueDate, ExpiryDate)
JOIN dbo.Staff s ON s.StaffNumber = v.StaffNumber
JOIN dbo.IssuingNaas n ON n.Code = v.NaaCode
WHERE NOT EXISTS (SELECT 1 FROM dbo.StaffAml a WHERE a.NaaId = n.NaaId AND a.AmlNumber = v.AmlNumber);
GO

-- Scope: AML endorsed for the trainers, MOE task-based for the pending staff member without an AML link
INSERT INTO dbo.StaffScope (StaffId, AircraftTypeId, ScopeBasis, StaffAmlId, BackingNaaId, Category, Level, Location, EffectiveFrom, EffectiveTo)
SELECT s.StaffId, t.AircraftTypeId, N'AMLEndorsed', a.StaffAmlId, a.NaaId, N'B1', N'Line', st.StationCode, '2025-01-01', a.ExpiryDate
FROM (VALUES (N'K-1021', N'A320'), (N'R-2007', N'A330')) AS v (StaffNumber, AircraftType)
JOIN dbo.Staff s ON s.StaffNumber = v.StaffNumber
JOIN dbo.Stations st ON st.StationId = s.StationId
JOIN dbo.StaffAml a ON a.StaffId = s.StaffId
JOIN dbo.AircraftTypes t ON t.Name = v.AircraftType
WHERE NOT EXISTS (SELECT 1 FROM dbo.StaffScope x WHERE x.StaffId = s.StaffId AND x.AircraftTypeId = t.AircraftTypeId AND x.Category = N'B1' AND x.Level = N'Line' AND x.EffectiveFrom = '2025-01-01');

INSERT INTO dbo.StaffScope (StaffId, AircraftTypeId, ScopeBasis, Category, Level, Location, EffectiveFrom, EffectiveTo)
SELECT s.StaffId, t.AircraftTypeId, N'MOETaskBased', N'A', N'Transit', st.StationCode, '2026-10-01', '2027-10-01'
FROM dbo.Staff s
JOIN dbo.Stations st ON st.StationId = s.StationId
CROSS JOIN dbo.AircraftTypes t
WHERE s.StaffNumber IN (N'K-1090', N'R-2031') AND t.Name = N'A320'
  AND NOT EXISTS (SELECT 1 FROM dbo.StaffScope x WHERE x.StaffId = s.StaffId AND x.AircraftTypeId = t.AircraftTypeId AND x.Category = N'A' AND x.Level = N'Transit' AND x.EffectiveFrom = '2026-10-01');
GO

-- Review queue rows for the two pending staff members
INSERT INTO dbo.ReviewQueue (OrganizationId, ItemType, ItemId, Summary, SubmittedByUserId)
SELECT s.OrganizationId, N'Staff', s.StaffId, N'New staff: ' + s.FullName + N' (' + s.StaffNumber + N')', s.CreatedByUserId
FROM dbo.Staff s
WHERE s.ReviewStatus = N'Pending'
  AND NOT EXISTS (SELECT 1 FROM dbo.ReviewQueue q WHERE q.ItemType = N'Staff' AND q.ItemId = s.StaffId AND q.Status = N'Pending');
GO

/* ---- CAASL approvals --------------------------------------------------------------- */
INSERT INTO dbo.CaaslApprovals (OrganizationId, ReferenceNumber, ValidTo)
SELECT o.OrganizationId, v.ReferenceNumber, v.ValidTo
FROM (VALUES
    (N'KAC',   N'CAASL/AMO/118', CAST('2026-11-14' AS date)),   -- expiring soon: shows up in the expiry alerts
    (N'RAMCO', N'CAASL/AMO/203', CAST('2027-04-30' AS date))
) AS v (Org, ReferenceNumber, ValidTo)
JOIN dbo.Organizations o ON o.Abbreviation = v.Org
WHERE NOT EXISTS (SELECT 1 FROM dbo.CaaslApprovals a WHERE a.OrganizationId = o.OrganizationId AND a.ReferenceNumber = v.ReferenceNumber);
GO

PRINT 'AeroAuthDb seed data is ready.';
GO
