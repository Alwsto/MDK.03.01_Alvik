using System.Text.Json;

namespace StudentLoad.Desktop.Services;

internal sealed record Session(
    string AccessToken, string Username, string DisplayName, string Role,
    DateTimeOffset ExpiresAtUtc)
{
    public bool IsValid => ExpiresAtUtc > DateTimeOffset.UtcNow;
    public bool CanEdit => Role is "Admin" or "Methodist";

    public string RoleName => Role switch
    {
        "Admin" => "Администратор",
        "Methodist" => "Методист",
        "Dispatcher" => "Диспетчер",
        "Teacher" => "Преподаватель",
        _ => Role
    };
}

internal sealed record CatalogItem(int Id, string Name);

internal sealed record Catalog(
    CatalogItem[] Groups, CatalogItem[] Disciplines, CatalogItem[] Teachers,
    CatalogItem[] Semesters)
{
    public static Catalog Empty { get; } = new([], [], [], []);
}

internal sealed record Workload(
    int Id, int GroupId, string GroupName, int DisciplineId, string DisciplineName,
    int TeacherId, string TeacherName, int SemesterId, string SemesterName,
    int LectureHours, int PracticalHours, int SelfStudyHours, int TotalHours,
    int StudentHours, DateTimeOffset UpdatedAtUtc, int Revision);

internal sealed record WorkloadInput(
    int GroupId, int DisciplineId, int TeacherId, int SemesterId, int LectureHours,
    int PracticalHours, int SelfStudyHours, int Revision);

internal sealed record TeacherReport(
    int TeacherId, string TeacherName, int LectureHours, int PracticalHours,
    int TotalHours, int AssignmentCount);

internal sealed record Problem(string? Title, string? Detail);

internal static class Payload
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
