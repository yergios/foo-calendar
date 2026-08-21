namespace Api.Services;

/// <summary>Columns the admin people list can be sorted by, over the wire from the
/// frontend. Vacation/sick/other are gated behind ReportsRead in PeopleEndpoints, not here
/// - they carry the same data the stats endpoint already restricts, computed the same way
/// (see AbsenceService.GetAllStatsAsync), so sorting by one can't become a way around that
/// gate for someone who only has PeopleManage.</summary>
public static class PersonSortColumn
{
    public const string Name = "name";
    public const string Username = "username";
    public const string HomeCity = "homeCity";
    public const string Vacation = "vacation";
    public const string Sick = "sick";
    public const string Other = "other";

    public static readonly string[] StatsColumns = [Vacation, Sick, Other];
}
