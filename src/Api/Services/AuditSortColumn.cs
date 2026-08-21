namespace Api.Services;

/// <summary>Columns the admin audit log can be sorted by, over the wire from the frontend.
/// Same shape as PersonSortColumn.</summary>
public static class AuditSortColumn
{
    public const string OccurredAt = "occurredAt";
    public const string Actor = "actor";
    public const string Action = "action";
}
