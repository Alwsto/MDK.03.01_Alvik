using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

Console.OutputEncoding = Encoding.UTF8;
var output = args switch
{
    [] => Path.Combine(AppContext.BaseDirectory, "Evidence"),
    ["--output", var path] => Path.GetFullPath(path),
    _ => throw new ArgumentException("Использование: StudentLoad.Checks [--output <каталог>]")
};
Directory.CreateDirectory(output);

var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
var assembly = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../StudentLoad.Api/bin",
    configuration, "net8.0/StudentLoad.Api.dll"));
if (!File.Exists(assembly))
    throw new FileNotFoundException("Сначала соберите StudentLoad.Api в той же конфигурации.", assembly);

var database = Path.Combine(output, $"checks-{Guid.NewGuid():N}.sqlite");
try
{
    await CheckMigrationsAsync(assembly, database);
    await CheckHttpAsync(assembly, database, output);
    Console.WriteLine("PASS | Все интеграционные проверки завершены.");
}
finally
{
    SqliteConnection.ClearAllPools();
    foreach (var suffix in new[] { "", "-shm", "-wal", "-journal" })
        File.Delete(database + suffix);
}

static async Task CheckMigrationsAsync(string assembly, string database)
{
    await ApiProcess.MigrateAsync(assembly, database, "20261117000100_Baseline");
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource = database, ForeignKeys = true, Pooling = false, Mode = SqliteOpenMode.ReadWrite
    }.ToString());
    connection.Open();
    Require(QueryLong(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory;") == 1, "Базовая версия миграции");
    Require(QueryLong(connection, "SELECT COUNT(*) FROM pragma_table_info('Workloads') WHERE name IN ('SelfStudyHours','Revision');") == 0,
        "Базовая таблица не содержит колонки второй версии");
    const string legacyColumns = """
        SELECT Id, GroupId, DisciplineId, TeacherId, SemesterId, LectureHours, PracticalHours, UpdatedAtUtc
        FROM Workloads ORDER BY Id;
        """;
    var before = ReadRows(connection, legacyColumns);
    Require(before.Length == 6, "Шесть исходных строк");
    connection.Close();

    await ApiProcess.MigrateAsync(assembly, database);
    connection.Open();
    var after = ReadRows(connection, legacyColumns);
    Require(before.SequenceEqual(after), "Исходные строки и значения сохранены");
    Require(QueryLong(connection, "SELECT COUNT(*) FROM Workloads WHERE SelfStudyHours=0 AND Revision=1;") == 6,
        "Все сохранённые строки получили ожидаемые значения новых колонок");
    Require(QueryLong(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory;") == 2, "Применены две миграции");
    Require(QueryLong(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;") == 0, "Внешние ключи согласованы");
    connection.Close();

    await ApiProcess.MigrateAsync(assembly, database);
    connection.Open();
    Require(QueryLong(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory;") == 2 &&
        before.SequenceEqual(ReadRows(connection, legacyColumns)), "Повторный запуск миграций является no-op");
    Console.WriteLine("PASS | SQLite 20261117000100_Baseline -> 20261117000200_WorkloadV2: 6 -> 6 строк, " +
        "исходные поля сохранены, новые 0/1, нарушений FK 0, история 1 -> 2 -> 2.");
}

static async Task CheckHttpAsync(string assembly, string database, string output)
{
    int id;
    Guid deleteKey;
    await using (var host = await ApiProcess.StartAsync(assembly, database))
    {
        using var client = NewClient(host.Address);
        using (var anonymous = await client.GetAsync("/api/v1/workloads"))
            Require(anonymous.StatusCode == HttpStatusCode.Unauthorized, "Анонимный запрос возвращает 401");
        using (var badLogin = await client.PostAsJsonAsync("/api/auth/login", new { username = "methodist", password = "WrongPassword" }))
            Require(badLogin.StatusCode == HttpStatusCode.Unauthorized, "Ошибочный пароль возвращает 401");

        await LoginAsync(client, "methodist", "Methodist");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database, Pooling = false, Mode = SqliteOpenMode.ReadOnly
        }.ToString()))
        {
            connection.Open();
            Require(QueryLong(connection, "SELECT COUNT(*) FROM Accounts WHERE PasswordHash LIKE '$2%' AND LENGTH(PasswordHash)=60;") == 4,
                "В SQLite хранятся BCrypt-хеши четырёх демонстрационных учётных записей");
        }
        await CheckCompatibilityAsync(client, output);
        await CheckRolesAndReportsAsync(client, host.Address);
        (id, deleteKey) = await CheckCrudAsync(client);
    }

    await using (var restarted = await ApiProcess.StartAsync(assembly, database))
    {
        using var client = NewClient(restarted.Address);
        await LoginAsync(client, "methodist", "Methodist");
        var repeatedDelete = await SendAsync(client, HttpMethod.Delete, $"/api/v2/workloads/{id}?revision=2", deleteKey);
        Require(repeatedDelete.Status == HttpStatusCode.NoContent, "Ответ повторного DELETE после перезапуска");
        using var detail = await client.GetAsync($"/api/v2/workloads/{id}");
        Require(detail.StatusCode == HttpStatusCode.NotFound, "Удалённая запись отсутствует после перезапуска");
        Console.WriteLine("PASS | Операции и ответы Idempotency-Key сохраняются после рестарта API в SQLite.");
    }
}

static async Task CheckCompatibilityAsync(HttpClient client, string output)
{
    const string legacyPath = "/api/v1/workloads?semesterId=1";
    const string currentPath = "/api/v2/workloads?semesterId=1";
    var legacyJson = await client.GetStringAsync(legacyPath);
    var currentJson = await client.GetStringAsync(currentPath);

    var strictLegacy = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false
    };
    var legacy = JsonSerializer.Deserialize<LegacyClientRow[]>(legacyJson, strictLegacy)!;
    var current = JsonSerializer.Deserialize<CurrentClientRow[]>(currentJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    Require(legacy.Length == 5 && current.Length == legacy.Length, "Версии возвращают пять строк первого семестра");
    var byId = current.ToDictionary(row => row.Id);
    foreach (var row in legacy)
    {
        Require(byId.TryGetValue(row.Id, out var mapped) &&
            row.Group == mapped.GroupName && row.Discipline == mapped.DisciplineName && row.Teacher == mapped.TeacherName &&
            row.Semester == mapped.SemesterName && row.Hours == mapped.TotalHours &&
            mapped.TotalHours == mapped.LectureHours + mapped.PracticalHours &&
            mapped.StudentHours == mapped.TotalHours + mapped.SelfStudyHours && mapped.Revision == 1,
            $"Совместимость строки {row.Id}");
    }
    Require(legacy[0].Id == 1 && legacy[0].Hours == 54, "Контрольное значение v1 hours=54");
    Require(byId[1].Revision == 1 && byId[1].SelfStudyHours == 0 && byId[1].UpdatedAtUtc.Kind == DateTimeKind.Utc,
        "Расширенные поля v2");

    await SaveJsonAsync(Path.Combine(output, "payload-v1.json"), legacyJson);
    await SaveJsonAsync(Path.Combine(output, "payload-v2.json"), currentJson);
    Console.WriteLine("PASS | GET /api/v1/workloads?semesterId=1: HTTP 200, 5 строк, строгий DTO из 6 полей, id=1 hours=54.");
    Console.WriteLine("PASS | GET /api/v2/workloads?semesterId=1: HTTP 200, 5 строк; совпадают ключи и семантика часов v1, добавлены поля v2.");
}

static async Task CheckRolesAndReportsAsync(HttpClient methodist, Uri address)
{
    using var admin = NewClient(address);
    await LoginAsync(admin, "admin", "Admin");
    using (var catalog = JsonDocument.Parse(await admin.GetStringAsync("/api/v2/catalog")))
    {
        Require(catalog.RootElement.GetProperty("groups").GetArrayLength() == 3 &&
            catalog.RootElement.GetProperty("disciplines").GetArrayLength() == 5 &&
            catalog.RootElement.GetProperty("teachers").GetArrayLength() == 4 &&
            catalog.RootElement.GetProperty("semesters").GetArrayLength() == 2, "Каталоги администратора");
    }

    using var teacher = NewClient(address);
    await LoginAsync(teacher, "teacher", "Teacher");
    using (var rows = JsonDocument.Parse(await teacher.GetStringAsync("/api/v2/workloads")))
        Require(rows.RootElement.GetArrayLength() == 2 &&
            rows.RootElement.EnumerateArray().All(row => row.GetProperty("teacherId").GetInt32() == 1), "Преподаватель видит свои строки");
    using (var legacy = JsonDocument.Parse(await teacher.GetStringAsync("/api/v1/workloads?groupId=1")))
        Require(legacy.RootElement.GetArrayLength() == 1, "Прежняя версия также соблюдает ограничения преподавателя");
    using (var hidden = await teacher.GetAsync("/api/v2/workloads/2"))
        Require(hidden.StatusCode == HttpStatusCode.NotFound, "Чужая строка по PK скрыта");
    using (var report = JsonDocument.Parse(await teacher.GetStringAsync("/api/v2/reports/teachers?semesterId=1")))
        Require(report.RootElement.GetArrayLength() == 1 && report.RootElement[0].GetProperty("totalHours").GetInt32() == 108,
            "Преподаватель видит одну свою строку отчёта, 108 контактных часов");
    using (var catalog = JsonDocument.Parse(await teacher.GetStringAsync("/api/v2/catalog")))
        Require(catalog.RootElement.GetProperty("teachers").GetArrayLength() == 1, "Каталог преподавателя показывает только его имя");

    using var dispatcher = NewClient(address);
    await LoginAsync(dispatcher, "dispatcher", "Dispatcher");
    using (var report = JsonDocument.Parse(await dispatcher.GetStringAsync("/api/v2/reports/teachers?semesterId=1")))
    {
        Require(report.RootElement.GetArrayLength() == 4 && report.RootElement[0].GetProperty("assignmentCount").GetInt32() == 2 &&
            report.RootElement[0].GetProperty("totalHours").GetInt32() == 108, "Сводный отчёт за первый семестр");
    }
    var sample = new MutationInput(1, 5, 1, 1, 16, 20, 24);
    var dispatcherPost = await SendAsync(dispatcher, HttpMethod.Post, "/api/v2/workloads", Guid.NewGuid(), sample);
    var teacherPost = await SendAsync(teacher, HttpMethod.Post, "/api/v2/workloads", Guid.NewGuid(), sample);
    Require(dispatcherPost.Status == HttpStatusCode.Forbidden && teacherPost.Status == HttpStatusCode.Forbidden, "Обе роли получают 403 на запись");
    Console.WriteLine("PASS | BCrypt, HTTP 401/403, четыре роли, область преподавателя и отчёт 108 часов проверены.");

    var search = Uri.EscapeDataString("БАЗЫ ДАННЫХ");
    using var filtered = JsonDocument.Parse(await methodist.GetStringAsync($"/api/v2/workloads?query={search}&semesterId=1&groupId=1&teacherId=1"));
    Require(filtered.RootElement.GetArrayLength() == 1 && filtered.RootElement[0].GetProperty("id").GetInt32() == 1,
        "Сочетание фильтров, поиск кириллицы без учёта регистра");
    Console.WriteLine("PASS | Фильтры семестра, группы, преподавателя и по тексту проверены.");
}

static async Task<(int Id, Guid DeleteKey)> CheckCrudAsync(HttpClient client)
{
    const string list = "/api/v2/workloads";
    var sample = new MutationInput(1, 5, 1, 1, 16, 20, 24);
    var invalid = await SendAsync(client, HttpMethod.Post, list, Guid.NewGuid(), sample with { GroupId = 9999 });
    Require(invalid.Status == HttpStatusCode.BadRequest, "Отклонена несуществующая ссылка из справочника");
    var invalidHours = await SendAsync(client, HttpMethod.Post, list, Guid.NewGuid(), sample with { LectureHours = -1 });
    Require(invalidHours.Status == HttpStatusCode.BadRequest, "Отклонены некорректные часы");

    var key = Guid.NewGuid();
    var responses = await Task.WhenAll(SendAsync(client, HttpMethod.Post, list, key, sample),
        SendAsync(client, HttpMethod.Post, list, key, sample));
    var created = responses[0];
    Require(responses.All(response => response.Status == HttpStatusCode.Created && response.Body == created.Body),
        "Параллельный повтор POST возвращает тот же статус и тело один раз применённой операции");
    using var first = JsonDocument.Parse(created.Body);
    var id = first.RootElement.GetProperty("id").GetInt32();
    var resource = $"{list}/{id}";
    Require(created.Location == resource && first.RootElement.GetProperty("revision").GetInt32() == 1 &&
        first.RootElement.GetProperty("totalHours").GetInt32() == 36 && first.RootElement.GetProperty("studentHours").GetInt32() == 60,
        "POST 201, Location, редакция, учебные часы");
    using (var oldClient = JsonDocument.Parse(await client.GetStringAsync("/api/v1/workloads?semesterId=1&groupId=1")))
    {
        var same = oldClient.RootElement.EnumerateArray().Single(row => row.GetProperty("id").GetInt32() == id);
        Require(same.GetProperty("hours").GetInt32() == 36, "Старый клиент по-прежнему получает 36 контактных часов");
    }
    Console.WriteLine("PASS | При 24 часах самостоятельной работы тот же ID даёт v1 hours=36, v2 totalHours=36, studentHours=60.");
    var mismatched = await SendAsync(client, HttpMethod.Post, list, key, sample with { LectureHours = 17 });
    var duplicate = await SendAsync(client, HttpMethod.Post, list, Guid.NewGuid(), sample);
    Require(mismatched.Status == HttpStatusCode.Conflict && duplicate.Status == HttpStatusCode.Conflict,
        "Повтор ключа с другим запросом и дубликат строки дают 409");

    var edits = await Task.WhenAll(
        SendAsync(client, HttpMethod.Put, resource, Guid.NewGuid(), sample with { SelfStudyHours = 26, Revision = 1 }),
        SendAsync(client, HttpMethod.Put, resource, Guid.NewGuid(), sample with { SelfStudyHours = 28, Revision = 1 }));
    Require(edits.Count(result => result.Status == HttpStatusCode.OK) == 1 &&
        edits.Count(result => result.Status == HttpStatusCode.Conflict) == 1, "При параллельном редактировании редакция блокирует одну операцию");
    using var details = JsonDocument.Parse(await client.GetStringAsync(resource));
    Require(details.RootElement.GetProperty("revision").GetInt32() == 2 &&
        details.RootElement.GetProperty("totalHours").GetInt32() == 36 &&
        details.RootElement.GetProperty("studentHours").GetInt32() is 62 or 64, "Проверка победившей редакции");

    var stale = await SendAsync(client, HttpMethod.Delete, $"{resource}?revision=1", Guid.NewGuid());
    Require(stale.Status == HttpStatusCode.Conflict, "Просроченная редакция DELETE даёт 409");
    var deleteKey = Guid.NewGuid();
    var deleted = await SendAsync(client, HttpMethod.Delete, $"{resource}?revision=2", deleteKey);
    Require(deleted.Status == HttpStatusCode.NoContent, "DELETE возвращает 204");
    Console.WriteLine("PASS | CRUD: 201/200/204, валидация 400, конфликт 409, два конкурентных POST и PUT проверены.");
    return (id, deleteKey);
}

static HttpClient NewClient(Uri address) => new() { BaseAddress = address, Timeout = TimeSpan.FromSeconds(20) };

static async Task LoginAsync(HttpClient client, string username, string role)
{
    using var response = await client.PostAsJsonAsync("/api/auth/login",
        new { username, password = "StudentLoad2026!" });
    Require(response.StatusCode == HttpStatusCode.OK, $"Вход {username} вернул {(int)response.StatusCode}");
    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    Require(body.RootElement.GetProperty("role").GetString() == role, $"Роль {role}");
    var token = body.RootElement.GetProperty("accessToken").GetString();
    Require(token is { Length: 43 } && body.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset() > DateTimeOffset.UtcNow,
        "Токен авторизации имеет срок действия");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}

static async Task<MutationResponse> SendAsync(HttpClient client, HttpMethod method, string path, Guid key, MutationInput? input = null)
{
    using var request = new HttpRequestMessage(method, path);
    request.Headers.Add("Idempotency-Key", key.ToString());
    if (input is not null)
        request.Content = JsonContent.Create(input);

    using var response = await client.SendAsync(request);
    return new MutationResponse(response.StatusCode, await response.Content.ReadAsStringAsync(), response.Headers.Location?.ToString());
}

static async Task SaveJsonAsync(string path, string json)
{
    using var parsed = JsonDocument.Parse(json);
    var format = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.BasicLatin,
            System.Text.Unicode.UnicodeRanges.Cyrillic)
    };
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(parsed.RootElement, format));
}

static long QueryLong(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    return (long)command.ExecuteScalar()!;
}

static string[] ReadRows(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    using var reader = command.ExecuteReader();
    var rows = new List<string>();
    var values = new object[reader.FieldCount];
    while (reader.Read())
    {
        reader.GetValues(values);
        rows.Add(JsonSerializer.Serialize(values));
    }
    return rows.ToArray();
}

static void Require(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException($"FAIL | {description}");
}

sealed class LegacyClientRow
{
    public required int Id { get; init; }
    public required string Group { get; init; }
    public required string Discipline { get; init; }
    public required string Teacher { get; init; }
    public required string Semester { get; init; }
    public required int Hours { get; init; }
}

sealed record CurrentClientRow(int Id, string GroupName, string DisciplineName, string TeacherName, string SemesterName,
    int LectureHours, int PracticalHours, int SelfStudyHours, int TotalHours, int StudentHours, DateTime UpdatedAtUtc, int Revision);

sealed record MutationInput(int GroupId, int DisciplineId, int TeacherId, int SemesterId, int LectureHours, int PracticalHours,
    int SelfStudyHours, int Revision = 0);
sealed record MutationResponse(HttpStatusCode Status, string Body, string? Location);
