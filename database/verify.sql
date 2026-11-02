/* =====================================================================
   AeroAuth - verify.sql
   Proves the database rules with real statements. Run after schema.sql and seed.sql.
   Everything runs inside one transaction that is rolled back at the end, so no data is changed.
   Each test either expects the database to REFUSE a bad statement (PASS = it was refused with the expected error)
   or expects a good statement to succeed.
   ===================================================================== */
USE AeroAuthDb;
GO
SET NOCOUNT ON;
GO

DECLARE @r TABLE (Id INT IDENTITY(1,1), Test NVARCHAR(200), Result NVARCHAR(4), Detail NVARCHAR(250));

DECLARE @kac INT    = (SELECT OrganizationId FROM dbo.Organizations WHERE Abbreviation = N'KAC');
DECLARE @ramco INT  = (SELECT OrganizationId FROM dbo.Organizations WHERE Abbreviation = N'RAMCO');
DECLARE @kacStn INT   = (SELECT StationId FROM dbo.Stations WHERE OrganizationId = @kac   AND StationCode = N'RUH');
DECLARE @ramcoStn INT = (SELECT StationId FROM dbo.Stations WHERE OrganizationId = @ramco AND StationCode = N'DXB');
DECLARE @admin INT  = (SELECT UserId FROM dbo.Users WHERE IdentityName = N'admin@aeroauth.local');
DECLARE @kacTrainer INT   = (SELECT StaffId FROM dbo.Staff WHERE OrganizationId = @kac   AND StaffNumber = N'K-1021');
DECLARE @ramcoTrainer INT = (SELECT StaffId FROM dbo.Staff WHERE OrganizationId = @ramco AND StaffNumber = N'R-2007');
DECLARE @kacAml INT = (SELECT TOP (1) StaffAmlId FROM dbo.StaffAml WHERE StaffId = @kacTrainer);
DECLARE @kacNaa INT = (SELECT TOP (1) NaaId FROM dbo.StaffAml WHERE StaffId = @kacTrainer);
DECLARE @type INT   = (SELECT AircraftTypeId FROM dbo.AircraftTypes WHERE Name = N'B777');
DECLARE @reason INT = (SELECT TOP (1) FreezeReasonId FROM dbo.FreezeReasons ORDER BY FreezeReasonId);
DECLARE @material INT = (SELECT MaterialId FROM dbo.TrainingMaterials WHERE Title = N'Procedures Training Manual' AND IsCurrent = 1);
DECLARE @sess INT, @att INT, @n1 INT, @n2 INT;

IF @kac IS NULL OR @ramco IS NULL OR @kacTrainer IS NULL OR @ramcoTrainer IS NULL OR @kacAml IS NULL OR @material IS NULL
BEGIN
    RAISERROR('Run schema.sql and seed.sql first.', 16, 1);
    RETURN;
END

BEGIN TRANSACTION;

-- 1. Tenant isolation: a station of another organization cannot be used for a staff member
BEGIN TRY
    INSERT dbo.Staff (OrganizationId, StationId, StaffNumber, FullName) VALUES (@kac, @ramcoStn, N'V-001', N'Cross tenant station');
    INSERT @r (Test, Result, Detail) VALUES (N'Staff cannot use a station of another organization', N'FAIL', N'The insert was accepted.');
END TRY
BEGIN CATCH
    INSERT @r (Test, Result, Detail) VALUES (N'Staff cannot use a station of another organization', CASE WHEN ERROR_NUMBER() = 547 THEN N'PASS' ELSE N'FAIL' END, LEFT(ERROR_MESSAGE(), 250));
END CATCH;

-- 2. The same staff number may exist in two organizations, but not twice in one
BEGIN TRY
    INSERT dbo.Staff (OrganizationId, StationId, StaffNumber, FullName) VALUES (@kac, @kacStn, N'V-002', N'Same number, KAC');
    INSERT dbo.Staff (OrganizationId, StationId, StaffNumber, FullName) VALUES (@ramco, @ramcoStn, N'V-002', N'Same number, RAMCO');
    INSERT @r (Test, Result, Detail) VALUES (N'Same staff number allowed in two organizations', N'PASS', N'Both inserts accepted.');
END TRY
BEGIN CATCH
    INSERT @r (Test, Result, Detail) VALUES (N'Same staff number allowed in two organizations', N'FAIL', LEFT(ERROR_MESSAGE(), 250));
END CATCH;
BEGIN TRY
    INSERT dbo.Staff (OrganizationId, StationId, StaffNumber, FullName) VALUES (@kac, @kacStn, N'V-002', N'Duplicate in KAC');
    INSERT @r (Test, Result, Detail) VALUES (N'Duplicate staff number in one organization is refused', N'FAIL', N'The insert was accepted.');
END TRY
BEGIN CATCH
    INSERT @r (Test, Result, Detail) VALUES (N'Duplicate staff number in one organization is refused', CASE WHEN ERROR_NUMBER() IN (2627, 2601) THEN N'PASS' ELSE N'FAIL' END, LEFT(ERROR_MESSAGE(), 250));
END CATCH;

-- 3. Scope rules
BEGIN TRY
    INSERT dbo.StaffScope (StaffId, AircraftTypeId, ScopeBasis, Category, Level, EffectiveFrom) VALUES (@kacTrainer, @type, N'AMLEndorsed', N'B1', N'Line', '2026-01-01');
    INSERT @r (Test, Result, Detail) VALUES (N'AML endorsed scope without an AML is refused', N'FAIL', N'The insert was accepted.');
END TRY
BEGIN CATCH
    INSERT @r (Test, Result, Detail) VALUES (N'AML endorsed scope without an AML is refused', CASE WHEN ERROR_NUMBER() = 547 THEN N'PASS' ELSE N'FAIL' END, LEFT(ERROR_MESSAGE(), 250));
END CATCH;
BEGIN TRY
    INSERT dbo.StaffScope (StaffId, AircraftTypeId, ScopeBasis, StaffAmlId, BackingNaaId, Category, Level, EffectiveFrom) VALUES (@kacTrainer, @type, N'MOETaskBased', @kacAml, @kacNaa, N'B1', N'Line', '2026-01-01');
    INSERT @r (Test, Result, Detail) VALUES (N'MOE task-based scope with an AML is refused', N'FAIL', N'The insert was accepted.');
END TRY
BEGIN CATCH
    INSERT @r (Test, Result, Detail) VALUES (N'MOE task-based scope with an AML is refused', CASE WHEN ERROR_NUMBER() = 547 THEN N'PASS' ELSE N'FAIL' END, LEFT(ERROR_MESSAGE(), 250));
END CATCH;
BEGIN TRY
    INSERT dbo.StaffScope (StaffId, AircraftTypeId, ScopeBasis, StaffAmlId, BackingNaaId, Category, Level, EffectiveFrom) VALUES (@ramcoTrainer, @type, N'AMLEndorsed', @kacAml, @kacNaa, N'B1', N'Line', '2026-01-01');
    INSERT @r (Test, Result, Detail) VALUES (N'Scope cannot use the AML of another staff member', N'FAIL', N'The insert was accepted.');
END TRY
BEGIN CATCH
    INSERT @r (Test, Result, Detail) VALUES (N'Scope cannot use the AML of another staff member', CASE WHEN ERROR_NUMBER() = 547 THEN N'PASS' ELSE N'FAIL' END, LEFT(ERROR_MESSAGE(), 250));
END CATCH;

-- 4. Review rules
BEGIN TRY
    INSERT dbo.ReviewQueue (OrganizationId, ItemType, ItemId, Status) VALUES (@kac, N'Staff', @kacTrainer, N'Approved');
    INSERT @r (Test, Result, Detail) VALUES (N'Approved review item without a reviewer is refused', N'FAIL', N'The insert was accepted.');
END TRY
BEGIN CATCH
    INSERT @r (Test, Result, Detail) VALUES (N'Approved review item without a reviewer is refused', CASE WHEN ERROR_NUMBER() = 547 THEN N'PASS' ELSE N'FAIL' END, LEFT(ERROR_MESSAGE(), 250));
END CATCH;
BEGIN TRY
    INSERT dbo.ReviewQueue (OrganizationId, ItemType, ItemId) VALUES (@kac, N'Staff', @kacTrainer);
    INSERT dbo.ReviewQueue (OrganizationId, ItemType, ItemId) VALUES (@kac, N'Staff', @kacTrainer);
    INSERT @r (Test, Result, Detail) VALUES (N'Only one open review item per record', N'FAIL', N'The second insert was accepted.');
END TRY
BEGIN CATCH
    INSERT @r (Test, Result, Detail) VALUES (N'Only one open review item per record', CASE WHEN ERROR_NUMBER() IN (2601, 2627) THEN N'PASS' ELSE N'FAIL' END, LEFT(ERROR_MESSAGE(), 250));
END CATCH;

-- 5. Training: trainer and attendee must belong to the session's organization
BEGIN TRY
    INSERT dbo.TrainingSessions (OrganizationId, StationId, TrainerStaffId, SessionDate) VALUES (@kac, @kacStn, @ramcoTrainer, '2026-10-30');
    INSERT @r (Test, Result, Detail) VALUES (N'Session trainer from another organization is refused', N'FAIL', N'The insert was accepted.');
END TRY
BEGIN CATCH
    INSERT @r (Test, Result, Detail) VALUES (N'Session trainer from another organization is refused', CASE WHEN ERROR_NUMBER() = 547 THEN N'PASS' ELSE N'FAIL' END, LEFT(ERROR_MESSAGE(), 250));
END CATCH;

INSERT dbo.TrainingSessions (OrganizationId, StationId, TrainerStaffId, SessionDate) VALUES (@kac, @kacStn, @kacTrainer, '2026-10-30');
SET @sess = SCOPE_IDENTITY();
INSERT dbo.SessionModules (SessionId, MaterialId, ModuleType) SELECT @sess, MaterialId, ModuleType FROM dbo.TrainingMaterials WHERE MaterialId = @material;
INSERT dbo.SessionAttendees (OrganizationId, SessionId, StaffId) VALUES (@kac, @sess, @kacTrainer);
SET @att = SCOPE_IDENTITY();

BEGIN TRY
    INSERT dbo.SessionAttendees (OrganizationId, SessionId, StaffId) VALUES (@kac, @sess, @ramcoTrainer);
    INSERT @r (Test, Result, Detail) VALUES (N'Attendee from another organization is refused', N'FAIL', N'The insert was accepted.');
END TRY
BEGIN CATCH
    INSERT @r (Test, Result, Detail) VALUES (N'Attendee from another organization is refused', CASE WHEN ERROR_NUMBER() = 547 THEN N'PASS' ELSE N'FAIL' END, LEFT(ERROR_MESSAGE(), 250));
END CATCH;

-- 6. Certificates: only one live certificate per attendee
BEGIN TRY
    INSERT dbo.Certificates (OrganizationId, SessionId, AttendeeId, CertificateNumber) VALUES (@kac, @sess, @att, N'TEST/ULPROC/2026/9001');
    INSERT dbo.Certificates (OrganizationId, SessionId, AttendeeId, CertificateNumber) VALUES (@kac, @sess, @att, N'TEST/ULPROC/2026/9002');
    INSERT @r (Test, Result, Detail) VALUES (N'Second live certificate for one attendee is refused', N'FAIL', N'The second insert was accepted.');
END TRY
BEGIN CATCH
    INSERT @r (Test, Result, Detail) VALUES (N'Second live certificate for one attendee is refused', CASE WHEN ERROR_NUMBER() IN (2601, 2627) THEN N'PASS' ELSE N'FAIL' END, LEFT(ERROR_MESSAGE(), 250));
END CATCH;

-- 7. Counters give consecutive numbers
BEGIN TRY
    EXEC dbo.usp_GetNextCounter @OrganizationId = @kac, @CounterKey = N'ULPROC', @CounterYear = 2026, @NextValue = @n1 OUTPUT;
    EXEC dbo.usp_GetNextCounter @OrganizationId = @kac, @CounterKey = N'ULPROC', @CounterYear = 2026, @NextValue = @n2 OUTPUT;
    INSERT @r (Test, Result, Detail) VALUES (N'Counter returns consecutive numbers', CASE WHEN @n2 = @n1 + 1 THEN N'PASS' ELSE N'FAIL' END, CONCAT(N'Got ', @n1, N' then ', @n2));
END TRY
BEGIN CATCH
    INSERT @r (Test, Result, Detail) VALUES (N'Counter returns consecutive numbers', N'FAIL', LEFT(ERROR_MESSAGE(), 250));
END CATCH;

-- 8. Audit rules (last, because a trigger error may doom the transaction)
BEGIN TRY
    INSERT dbo.AdminActionsLog (UserId, ActionType, StaffId) VALUES (@admin, N'Freeze', @kacTrainer);
    INSERT @r (Test, Result, Detail) VALUES (N'Freeze without a freeze reason is refused', N'FAIL', N'The insert was accepted.');
END TRY
BEGIN CATCH
    INSERT @r (Test, Result, Detail) VALUES (N'Freeze without a freeze reason is refused', CASE WHEN ERROR_NUMBER() = 547 THEN N'PASS' ELSE N'FAIL' END, LEFT(ERROR_MESSAGE(), 250));
END CATCH;

IF XACT_STATE() = 1
BEGIN
    INSERT dbo.AdminActionsLog (UserId, ActionType, StaffId, FreezeReasonId) VALUES (@admin, N'Freeze', @kacTrainer, @reason);
    BEGIN TRY
        UPDATE dbo.AdminActionsLog SET Notes = N'tampered' WHERE StaffId = @kacTrainer;
        INSERT @r (Test, Result, Detail) VALUES (N'Admin Actions Log cannot be updated', N'FAIL', N'The update was accepted.');
    END TRY
    BEGIN CATCH
        INSERT @r (Test, Result, Detail) VALUES (N'Admin Actions Log cannot be updated', CASE WHEN ERROR_NUMBER() = 51000 THEN N'PASS' ELSE N'FAIL' END, LEFT(ERROR_MESSAGE(), 250));
    END CATCH;
END

IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- nothing above is kept

SELECT Id, Test, Result, Detail FROM @r ORDER BY Id;
SELECT SUM(CASE WHEN Result = N'PASS' THEN 1 ELSE 0 END) AS Passed, SUM(CASE WHEN Result = N'FAIL' THEN 1 ELSE 0 END) AS Failed FROM @r;
GO
