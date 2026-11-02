using System.ComponentModel.DataAnnotations;

namespace AeroAuth.Api.Models;

public class DevLoginRequest { [Required] public string Identity { get; set; } = ""; }

public class AmlRequest
{
    [Required] public int NaaId { get; set; }
    [Required, StringLength(50)] public string AmlNumber { get; set; } = "";
    public DateTime? IssueDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
}

public class ScopeRequest
{
    [Required] public int AircraftTypeId { get; set; }
    [Required] public string ScopeBasis { get; set; } = "";       // AMLEndorsed or MOETaskBased
    public int? AmlIndex { get; set; }                             // position in the Aml list, for AMLEndorsed
    [Required, StringLength(50)] public string Category { get; set; } = "";
    [Required, StringLength(30)] public string Level { get; set; } = "";
    [StringLength(100)] public string? Location { get; set; }
    [Required] public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
}

public class SubmitStaffRequest
{
    /// <summary>Ignored for External AMO users: their own organization is always used.</summary>
    public int? OrganizationId { get; set; }
    [Required] public int StationId { get; set; }
    [Required, StringLength(30)] public string StaffNumber { get; set; } = "";
    [Required, StringLength(150)] public string FullName { get; set; } = "";
    [EmailAddress, StringLength(256)] public string? Email { get; set; }
    public List<AmlRequest> Aml { get; set; } = new();
    public List<ScopeRequest> Scope { get; set; } = new();
}

public class UpdateStaffRequest
{
    [Required] public int StationId { get; set; }
    [Required, StringLength(150)] public string FullName { get; set; } = "";
    [EmailAddress, StringLength(256)] public string? Email { get; set; }
    public string? SignaturePath { get; set; }
    public string? StampPath { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsTrainer { get; set; }
}

public class DecisionRequest
{
    [Required] public string Decision { get; set; } = "";         // Approved or Rejected
    [StringLength(500)] public string? Comment { get; set; }
}

public class FreezeRequest
{
    [Required] public int FreezeReasonId { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
}

public class NotesRequest { [StringLength(500)] public string? Notes { get; set; } }

public class SubmitSessionRequest
{
    public int? OrganizationId { get; set; }
    [Required] public int StationId { get; set; }
    [Required] public int TrainerStaffId { get; set; }
    [Required] public DateTime SessionDate { get; set; }
    [StringLength(200)] public string? Venue { get; set; }
    [StringLength(500)] public string? AttendanceSheetPath { get; set; }
    [Required, MinLength(1)] public List<int> MaterialIds { get; set; } = new();
    [Required, MinLength(1)] public List<int> AttendeeStaffIds { get; set; } = new();
}
