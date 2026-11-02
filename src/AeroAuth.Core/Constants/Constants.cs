namespace AeroAuth.Core.Constants;

public static class Roles
{
    public const string AdminQA = "AdminQA";
    public const string InternalQA = "InternalQA";
    public const string ExternalAMO = "ExternalAMO";
}

public static class ReviewStatus
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public static class ReviewItemType
{
    public const string Staff = "Staff";
    public const string TrainingSession = "TrainingSession";
}

public static class ScopeBasis
{
    public const string AmlEndorsed = "AMLEndorsed";
    public const string MoeTaskBased = "MOETaskBased";
}

public static class AdminActionType
{
    public const string Freeze = "Freeze";
    public const string Unfreeze = "Unfreeze";
    public const string DirectEdit = "DirectEdit";
    public const string Override = "Override";
}

public static class CounterKeys
{
    /// <summary>Procedures training certificates, per organization: KAC/ULPROC/2026/0001.</summary>
    public const string ProceduresCertificate = "ULPROC";
    /// <summary>Certifying staff register, global: QA/RPT/2026/0001.</summary>
    public const string StaffRegister = "QARPT";
}
