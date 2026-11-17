using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace StudentLoad.Api;

internal sealed record CatalogItem(int Id, string Name);
internal sealed record LookupCatalog(
    IReadOnlyList<CatalogItem> Groups, IReadOnlyList<CatalogItem> Disciplines,
    IReadOnlyList<CatalogItem> Teachers, IReadOnlyList<CatalogItem> Semesters);

internal sealed record WorkloadV1(int Id, string Group, string Discipline, string Teacher, string Semester, int Hours);

internal sealed record WorkloadDetails(
    int Id, int GroupId, string GroupName, int DisciplineId, string DisciplineName, int TeacherId, string TeacherName,
    int SemesterId, string SemesterName, int LectureHours, int PracticalHours, int SelfStudyHours,
    int TotalHours, int StudentHours, DateTime UpdatedAtUtc, int Revision);

internal sealed record WorkloadInput(
    int GroupId, int DisciplineId, int TeacherId, int SemesterId, int LectureHours, int PracticalHours,
    int SelfStudyHours, int Revision);

internal sealed record TeacherReport(
    int TeacherId, string TeacherName, int LectureHours, int PracticalHours, int TotalHours, int AssignmentCount);

internal sealed class WorkloadFilters
{
    [FromQuery] public int? SemesterId { get; init; }
    [FromQuery] public int? GroupId { get; init; }
    [FromQuery] public int? TeacherId { get; init; }
    [FromQuery] public string? Query { get; init; }
}

internal static class WorkloadEndpoints
{
    private const string V2Workloads = "/api/v2/workloads";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Map(IEndpointRouteBuilder routes)
    {
        var read = routes.MapGroup("/api").RequireAuthorization("read-workloads");
        read.MapGet("/v1/workloads", GetV1Async);
        read.MapGet("/v2/workloads", GetV2Async);
        read.MapGet("/v2/workloads/{id:int:min(1)}", GetDetailsAsync);
        read.MapGet("/v2/catalog", GetCatalogAsync);
        read.MapGet("/v2/reports/teachers", GetTeacherReportAsync);

        var write = routes.MapGroup(V2Workloads).RequireAuthorization("manage-workloads");
        write.MapPost("", CreateAsync);
        write.MapPut("/{id:int:min(1)}", UpdateAsync);
        write.MapDelete("/{id:int:min(1)}", DeleteAsync);
    }

    private static async Task<IResult> GetV1Async([AsParameters] WorkloadFilters filters, ClaimsPrincipal user,
        StudentLoadDb database, CancellationToken cancellationToken)
    {
        var rows = await GetRowsAsync(filters, user, database, cancellationToken);
        return Results.Ok(rows.Select(row => new WorkloadV1(row.Id, row.GroupName, row.DisciplineName, row.TeacherName,
            row.SemesterName, row.TotalHours)));
    }

    private static async Task<IResult> GetV2Async([AsParameters] WorkloadFilters filters, ClaimsPrincipal user,
        StudentLoadDb database, CancellationToken cancellationToken) =>
        Results.Ok(await GetRowsAsync(filters, user, database, cancellationToken));

    private static async Task<IResult> GetDetailsAsync(int id, ClaimsPrincipal user, StudentLoadDb database,
        CancellationToken cancellationToken)
    {
        var row = await Details(VisibleWorkloads(database, user).Where(row => row.Id == id))
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? Results.Problem("Запись о нагрузке не найдена.", statusCode: 404) : Results.Ok(row);
    }

    private static async Task<IReadOnlyList<WorkloadDetails>> GetRowsAsync(WorkloadFilters filters, ClaimsPrincipal user,
        StudentLoadDb database, CancellationToken cancellationToken)
    {
        var rows = VisibleWorkloads(database, user);
        if (filters.SemesterId is int semester)
            rows = rows.Where(row => row.SemesterId == semester);
        if (filters.GroupId is int group)
            rows = rows.Where(row => row.GroupId == group);
        if (filters.TeacherId is int teacher)
            rows = rows.Where(row => row.TeacherId == teacher);

        var result = await Details(rows.OrderBy(row => row.Id)).ToListAsync(cancellationToken);
        var term = filters.Query?.Trim();
        if (string.IsNullOrEmpty(term))
            return result;

        return result.Where(row => row.GroupName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            row.DisciplineName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            row.TeacherName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            row.SemesterName.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private static async Task<IResult> GetCatalogAsync(StudentLoadDb database, ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var teacherId = ClaimId(user, SessionAuthentication.TeacherIdClaim);
        var teachers = user.IsInRole(Roles.Teacher) ? database.Teachers.Where(row => row.Id == teacherId) : database.Teachers;
        return Results.Ok(new LookupCatalog(
            await database.Groups.OrderBy(row => row.Id).Select(row => new CatalogItem(row.Id, row.Name)).ToListAsync(cancellationToken),
            await database.Disciplines.OrderBy(row => row.Id).Select(row => new CatalogItem(row.Id, row.Name)).ToListAsync(cancellationToken),
            await teachers.OrderBy(row => row.Id).Select(row => new CatalogItem(row.Id, row.Name)).ToListAsync(cancellationToken),
            await database.Semesters.OrderBy(row => row.Id).Select(row => new CatalogItem(row.Id, row.Name)).ToListAsync(cancellationToken)));
    }

    private static async Task<IResult> GetTeacherReportAsync(int? semesterId, StudentLoadDb database, ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var rows = VisibleWorkloads(database, user);
        if (semesterId is int semester)
            rows = rows.Where(row => row.SemesterId == semester);

        var report = await rows.GroupBy(row => new { row.TeacherId, row.Teacher.Name })
            .OrderBy(group => group.Key.TeacherId)
            .Select(group => new TeacherReport(group.Key.TeacherId, group.Key.Name, group.Sum(row => row.LectureHours),
                group.Sum(row => row.PracticalHours), group.Sum(row => row.LectureHours + row.PracticalHours), group.Count()))
            .ToListAsync(cancellationToken);
        return Results.Ok(report);
    }

    private static IQueryable<Workload> VisibleWorkloads(StudentLoadDb database, ClaimsPrincipal user)
    {
        var rows = database.Workloads.AsNoTracking();
        if (!user.IsInRole(Roles.Teacher))
            return rows;

        var teacherId = ClaimId(user, SessionAuthentication.TeacherIdClaim);
        return rows.Where(row => row.TeacherId == teacherId);
    }

    private static IQueryable<WorkloadDetails> Details(IQueryable<Workload> rows) => rows.Select(row => new WorkloadDetails(
        row.Id, row.GroupId, row.Group.Name, row.DisciplineId, row.Discipline.Name, row.TeacherId, row.Teacher.Name,
        row.SemesterId, row.Semester.Name, row.LectureHours, row.PracticalHours, row.SelfStudyHours,
        row.LectureHours + row.PracticalHours, row.LectureHours + row.PracticalHours + row.SelfStudyHours,
        row.UpdatedAtUtc, row.Revision));

    private static Task<IResult> CreateAsync(WorkloadInput input, StudentLoadDb database, HttpContext context,
        CancellationToken cancellationToken) => MutateAsync(database, context, new MutationCommand("create", null, input, null),
            cancellationToken);

    private static Task<IResult> UpdateAsync(int id, WorkloadInput input, StudentLoadDb database, HttpContext context,
        CancellationToken cancellationToken) => MutateAsync(database, context, new MutationCommand("update", id, input, input.Revision),
            cancellationToken);

    private static Task<IResult> DeleteAsync(int id, int revision, StudentLoadDb database, HttpContext context,
        CancellationToken cancellationToken) => MutateAsync(database, context, new MutationCommand("delete", id, null, revision),
            cancellationToken);

    private static async Task<IResult> MutateAsync(StudentLoadDb database, HttpContext context, MutationCommand command,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(context.Request.Headers["Idempotency-Key"].ToString(), out var key) || key == Guid.Empty)
            return Results.Problem("Для изменения укажите уникальный Idempotency-Key в формате GUID.", statusCode: 400);

        var accountId = ClaimId(context.User, ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated session has no account identifier.");
        var digest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(command, JsonOptions)));
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var receipt = await database.MutationReceipts.SingleOrDefaultAsync(row => row.AccountId == accountId && row.Key == key,
            cancellationToken);
        if (receipt is not null)
        {
            if (receipt.RequestHash != digest)
                return Results.Problem("Этот ключ уже использован с другим запросом.", statusCode: 409);

            return WriteReceipt(context, receipt);
        }

        var outcome = await ApplyMutationAsync(database, command, cancellationToken);
        if (outcome.Error is not null)
            return Results.Problem(outcome.Error, statusCode: outcome.StatusCode);

        receipt = new MutationReceipt
        {
            AccountId = accountId, Key = key, RequestHash = digest, StatusCode = outcome.StatusCode,
            ResponseJson = outcome.Data is null ? null : JsonSerializer.Serialize(outcome.Data, JsonOptions),
            Location = outcome.StatusCode == 201 ? $"{V2Workloads}/{outcome.Data!.Id}" : null,
            CreatedAtUtc = DateTime.UtcNow
        };
        database.MutationReceipts.Add(receipt);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return WriteReceipt(context, receipt);
    }

    private static IResult WriteReceipt(HttpContext context, MutationReceipt receipt)
    {
        if (receipt.Location is not null)
            context.Response.Headers.Location = receipt.Location;

        return receipt.ResponseJson is null
            ? Results.StatusCode(receipt.StatusCode)
            : Results.Content(receipt.ResponseJson, "application/json; charset=utf-8", statusCode: receipt.StatusCode);
    }

    private static async Task<MutationOutcome> ApplyMutationAsync(StudentLoadDb database, MutationCommand command,
        CancellationToken cancellationToken)
    {
        var isExisting = command.Id.HasValue;
        Workload? row = null;
        if (isExisting)
        {
            if (command.Revision is null or < 1)
                return new(400, "Укажите положительную редакцию записи.");

            row = await database.Workloads.SingleOrDefaultAsync(item => item.Id == command.Id, cancellationToken);
            if (row is null)
                return new(404, "Запись о нагрузке не найдена.");
            if (row.Revision != command.Revision)
                return new(409, "Запись была изменена. Обновите данные и повторите операцию.");

            if (command.Name == "delete")
            {
                database.Workloads.Remove(row);
                await database.SaveChangesAsync(cancellationToken);
                return new(204);
            }
        }

        var input = command.Input!;
        var validation = await ValidateAsync(database, input, command.Id, cancellationToken);
        if (validation is not null)
            return validation;

        row ??= new Workload();
        row.GroupId = input.GroupId;
        row.DisciplineId = input.DisciplineId;
        row.TeacherId = input.TeacherId;
        row.SemesterId = input.SemesterId;
        row.LectureHours = input.LectureHours;
        row.PracticalHours = input.PracticalHours;
        row.SelfStudyHours = input.SelfStudyHours;
        row.UpdatedAtUtc = DateTime.UtcNow;
        row.Revision = checked(row.Revision + 1);
        if (!isExisting)
            database.Workloads.Add(row);

        await database.SaveChangesAsync(cancellationToken);
        var data = await Details(database.Workloads.AsNoTracking().Where(item => item.Id == row.Id)).SingleAsync(cancellationToken);
        return new(isExisting ? 200 : 201, Data: data);
    }

    private static async Task<MutationOutcome?> ValidateAsync(StudentLoadDb database, WorkloadInput input, int? id,
        CancellationToken cancellationToken)
    {
        if (input.LectureHours is < 0 or > 1000 || input.PracticalHours is < 0 or > 1000 ||
            input.SelfStudyHours is < 0 or > 1000 || input.LectureHours + input.PracticalHours == 0)
        {
            return new(400, "Для каждого вида занятий укажите 0–1000 часов и хотя бы один час лекций или практики.");
        }

        if (!await database.Groups.AnyAsync(row => row.Id == input.GroupId, cancellationToken) ||
            !await database.Disciplines.AnyAsync(row => row.Id == input.DisciplineId, cancellationToken) ||
            !await database.Teachers.AnyAsync(row => row.Id == input.TeacherId, cancellationToken) ||
            !await database.Semesters.AnyAsync(row => row.Id == input.SemesterId, cancellationToken))
        {
            return new(400, "Выберите группу, дисциплину, преподавателя и семестр из справочников.");
        }

        var duplicate = await database.Workloads.AnyAsync(row => row.Id != id && row.GroupId == input.GroupId &&
            row.DisciplineId == input.DisciplineId && row.TeacherId == input.TeacherId && row.SemesterId == input.SemesterId,
            cancellationToken);
        return duplicate ? new(409, "Такая строка учебного плана уже существует.") : null;
    }

    private static int? ClaimId(ClaimsPrincipal user, string name) =>
        int.TryParse(user.FindFirstValue(name), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    private sealed record MutationCommand(string Name, int? Id, WorkloadInput? Input, int? Revision);
    private sealed record MutationOutcome(int StatusCode, string? Error = null, WorkloadDetails? Data = null);
}
