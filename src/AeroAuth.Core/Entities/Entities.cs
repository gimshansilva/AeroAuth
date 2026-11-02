namespace AeroAuth.Core.Entities;

/// <summary>Marks an entity that belongs to one organization (tenant).</summary>
public interface ITenantEntity { int OrganizationId { get; } }

public class Organization
{
    public int OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public string Abbreviation { get; set; } = "";
    public string? QualityContactName { get; set; }
    public string? QualityContactEmail { get; set; }
    public DateTime ContractStart { get; set; }
    public DateTime? ContractEnd { get; set; }
    public bool IsActive { get; set; }
}

public class Station : ITenantEntity
{
    public int StationId { get; set; }
    public int OrganizationId { get; set; }
    public string StationCode { get; set; } = "";
    public string City { get; set; } = "";
    public bool IsActive { get; set; }
}

public class Staff : ITenantEntity
{
    public int StaffId { get; set; }
    public int OrganizationId { get; set; }
    public int StationId { get; set; }
    public string StationCode { get; set; } = "";          // filled by the join, read only
    public string StaffNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string? SignaturePath { get; set; }
    public string? StampPath { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsFrozen { get; set; }
    public bool IsTrainer { get; set; }
    public string ReviewStatus { get; set; } = Constants.ReviewStatus.Pending;
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Child records, filled by StaffRepository.GetWithDetailsAsync (Dapper multi-mapping)
    public List<StaffAml> AmlRecords { get; set; } = new();
    public List<StaffScope> ScopeRecords { get; set; } = new();
}

public class StaffAml
{
    public int StaffAmlId { get; set; }
    public int StaffId { get; set; }
    public int NaaId { get; set; }
    public string NaaCode { get; set; } = "";               // filled by the join
    public string AmlNumber { get; set; } = "";
    public DateTime? IssueDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
}

public class StaffScope
{
    public int ScopeId { get; set; }
    public int StaffId { get; set; }
    public int AircraftTypeId { get; set; }
    public string AircraftTypeName { get; set; } = "";      // filled by the join
    public string ScopeBasis { get; set; } = Constants.ScopeBasis.AmlEndorsed;
    public int? StaffAmlId { get; set; }
    public int? BackingNaaId { get; set; }
    public string Category { get; set; } = "";
    public string Level { get; set; } = "";
    public string? Location { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
}

public class TrainingSession : ITenantEntity
{
    public int SessionId { get; set; }
    public int OrganizationId { get; set; }
    public int StationId { get; set; }
    public int TrainerStaffId { get; set; }
    public DateTime SessionDate { get; set; }
    public string? Venue { get; set; }
    public string? AttendanceSheetPath { get; set; }
    public string Status { get; set; } = Constants.ReviewStatus.Pending;
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string OrganizationAbbreviation { get; set; } = "";   // filled by the join

    public List<SessionModule> Modules { get; set; } = new();
    public List<SessionAttendee> Attendees { get; set; } = new();
}

public class SessionModule
{
    public int SessionModuleId { get; set; }
    public int SessionId { get; set; }
    public int MaterialId { get; set; }
    public string ModuleType { get; set; } = "";
}

public class SessionAttendee : ITenantEntity
{
    public int AttendeeId { get; set; }
    public int OrganizationId { get; set; }
    public int SessionId { get; set; }
    public int StaffId { get; set; }
    public bool MissingEmail { get; set; }
    public bool RefresherGap { get; set; }
}

public class ReviewQueueItem : ITenantEntity
{
    public int ReviewQueueId { get; set; }
    public int OrganizationId { get; set; }
    public string ItemType { get; set; } = "";
    public int ItemId { get; set; }
    public string? Summary { get; set; }
    public string Status { get; set; } = Constants.ReviewStatus.Pending;
    public int? SubmittedByUserId { get; set; }
    public DateTime SubmittedAt { get; set; }
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
}

public class Certificate : ITenantEntity
{
    public int CertificateId { get; set; }
    public int OrganizationId { get; set; }
    public int SessionId { get; set; }
    public int AttendeeId { get; set; }
    public string CertificateNumber { get; set; } = "";
    public string Status { get; set; } = "Issued";
    public DateTime IssuedAt { get; set; }
    public int? IssuedByUserId { get; set; }
}

public class CaaslApproval : ITenantEntity
{
    public int ApprovalId { get; set; }
    public int OrganizationId { get; set; }
    public string ReferenceNumber { get; set; } = "";
    public DateTime ValidTo { get; set; }
    public string Status { get; set; } = "Active";
}

public class AdminActionLogEntry
{
    public long ActionId { get; set; }
    public int UserId { get; set; }
    public string ActionType { get; set; } = "";
    public int? StaffId { get; set; }
    public int? FreezeReasonId { get; set; }
    public string? TargetTable { get; set; }
    public string? TargetKey { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Notes { get; set; }
    public DateTime ActionAt { get; set; }
}

public class ReferenceItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
}
