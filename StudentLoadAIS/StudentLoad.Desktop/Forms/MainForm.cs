using System.Net;
using System.Text;
using System.Text.Json;
using StudentLoad.Desktop.Data;
using StudentLoad.Desktop.Services;

namespace StudentLoad.Desktop.Forms;

internal sealed partial class MainForm : Form
{
    private enum View { Workload, Report, Queue }

    private readonly StudentLoadApi api;
    private readonly OfflineStore store;
    private readonly QueueSync queueSync;
    private readonly Session session;
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 20_000 };
    private Catalog catalog = Catalog.Empty;
    private Workload[] workloads = [];
    private TeacherReport[] report = [];
    private Mutation[] queued = [];
    private View current;
    private bool changingFilters;
    private bool busy;
    private bool online;
    private bool refreshReportPending;
    private bool expired;
    private int completedSinceLaunch;
    public bool SignedOut { get; private set; }

    public MainForm(StudentLoadApi api, OfflineStore store, Session session, ClientSettings settings)
    {
        this.api = api;
        this.store = store;
        this.session = session;
        queueSync = new QueueSync(api, store);
        InitializeComponent();

        identity.Text = session.DisplayName + "\n" + session.RoleName;
        origin.Text = $"REST API v2  ·  {settings.ApiUrl.Host}:{settings.ApiUrl.Port}";
        add.Visible = session.CanEdit;
        edit.Visible = session.CanEdit;
        delete.Visible = session.CanEdit;
        discard.Visible = session.CanEdit;
        replay.Visible = session.CanEdit;
        SelectView(View.Workload);

        workloadNav.Click += async (_, _) => await SwitchViewAsync(View.Workload);
        reportNav.Click += async (_, _) => await SwitchViewAsync(View.Report);
        queueNav.Click += async (_, _) => await SwitchViewAsync(View.Queue);
        signOut.Click += (_, _) => { SignedOut = true; Close(); };
        refresh.Click += async (_, _) => await RunAsync(() => RefreshAllAsync());
        replay.Click += async (_, _) => await RunAsync(() => RefreshAllAsync());
        add.Click += async (_, _) => await EditAsync(null);
        edit.Click += async (_, _) => await EditSelectedAsync();
        delete.Click += async (_, _) => await DeleteSelectedAsync();
        discard.Click += async (_, _) => await DiscardSelectedAsync();
        export.Click += async (_, _) => await RunAsync(ExportAsync);

        semesterFilter.SelectedIndexChanged += (_, _) => ApplyWorkloadFilters();
        groupFilter.SelectedIndexChanged += (_, _) => ApplyWorkloadFilters();
        teacherFilter.SelectedIndexChanged += (_, _) => ApplyWorkloadFilters();
        search.TextChanged += (_, _) => ApplyWorkloadFilters();
        reportFilter.SelectedIndexChanged += async (_, _) =>
        {
            if (!changingFilters)
                await RefreshReportAsync();
        };
        workloadGrid.SelectionChanged += (_, _) => UpdateButtons();
        queueGrid.SelectionChanged += (_, _) => UpdateButtons();
        workloadGrid.CellDoubleClick += async (_, args) =>
        {
            if (args.RowIndex >= 0 && edit.Enabled)
                await EditSelectedAsync();
        };
        refreshTimer.Tick += async (_, _) =>
        {
            if (!online || queued.Length > 0)
                await RunAsync(() => RefreshAllAsync(background: true));
        };
        Shown += async (_, _) =>
        {
            await RunAsync(async () =>
            {
                await LoadCachedAsync();
                await RefreshAllAsync();
            });
            if (!IsDisposed)
                refreshTimer.Start();
        };
        FormClosing += (_, args) =>
        {
            if (busy)
            {
                args.Cancel = true;
                SignedOut = false;
            }
        };
        FormClosed += (_, _) => refreshTimer.Dispose();
        UpdateButtons();
    }

    private async Task LoadCachedAsync()
    {
        catalog = await store.LoadSnapshotAsync<Catalog>(session.Username, "catalog") ?? Catalog.Empty;
        workloads = await store.LoadSnapshotAsync<Workload[]>(session.Username, "workloads") ?? [];
        BindFilters();
        ApplyWorkloadFilters();
        await RefreshQueueAsync();
    }

    private async Task RefreshAllAsync(bool background = false)
    {
        if (background && online && queued.FirstOrDefault()?.Error is not null)
            return;

        var result = new SyncResult(0, false, null);
        try
        {
            if (session.CanEdit)
                result = await queueSync.ReplayAsync(session);
            completedSinceLaunch += result.Sent;
            if (result.ConnectionLost)
            {
                SetConnection(false);
                if (!background)
                    Status("Сервер недоступен. Операции сохранены в офлайн-очереди.", warning: true);
                return;
            }

            var freshCatalog = await api.CatalogAsync(session);
            var freshWorkloads = await api.WorkloadsAsync(session);
            await store.SaveSnapshotAsync(session.Username, "catalog", freshCatalog);
            await store.SaveSnapshotAsync(session.Username, "workloads", freshWorkloads);
            catalog = freshCatalog;
            workloads = freshWorkloads;
            SetConnection(true);
            BindFilters();
            ApplyWorkloadFilters();

            if (result.Blocked is not null)
                Status("Синхронизация приостановлена. Откройте офлайн-очередь. " + result.Blocked, warning: true);
            else if (result.Sent > 0)
                Status($"Синхронизация завершена. Передано операций: {result.Sent}.");
            else if (!background)
                Status("Данные получены с сервера.");
        }
        catch (ApiUnavailableException)
        {
            SetConnection(false);
            if (!background)
                Status("Автономный режим. Показаны последние сохранённые данные.", warning: true);
        }
        finally
        {
            await RefreshQueueAsync();
            if (current == View.Report)
                await LoadReportAsync();
        }
    }

    private async Task SwitchViewAsync(View view)
    {
        SelectView(view);
        if (view == View.Report)
            await RefreshReportAsync();
    }

    private void SelectView(View view)
    {
        current = view;
        workloadNav.BackColor = view == View.Workload ? Theme.Accent : Theme.Navy;
        reportNav.BackColor = view == View.Report ? Theme.Accent : Theme.Navy;
        queueNav.BackColor = view == View.Queue ? Theme.Accent : Theme.Navy;
        workloadPage.Visible = view == View.Workload;
        reportPage.Visible = view == View.Report;
        queuePage.Visible = view == View.Queue;
        (pageTitle.Text, pageDescription.Text) = view switch
        {
            View.Workload => ("Учебная нагрузка",
                "Назначения по группам и дисциплинам. Все показатели в академических часах."),
            View.Report => ("Отчёт по нагрузке",
                "Контактные часы преподавателей по выбранному семестру."),
            _ => ("Офлайн-очередь",
                "Сохранённые операции отправляются по порядку при восстановлении связи.")
        };
    }

    private async Task RefreshReportAsync()
    {
        if (busy)
        {
            refreshReportPending = true;
            return;
        }
        await RunAsync(LoadReportAsync);
    }

    private async Task LoadReportAsync()
    {
        var semester = Selection(reportFilter);
        if (semester == 0)
        {
            ShowReport([]);
            return;
        }
        var key = $"report-{semester}";
        if (online)
        {
            try
            {
                var rows = await api.ReportAsync(session, semester);
                await store.SaveSnapshotAsync(session.Username, key, rows);
                if (Selection(reportFilter) == semester)
                    ShowReport(rows);
                return;
            }
            catch (ApiUnavailableException)
            {
                SetConnection(false);
                Status("Отчёт открыт из локальных данных.", warning: true);
            }
        }

        var cached = await store.LoadSnapshotAsync<TeacherReport[]>(session.Username, key);
        if (Selection(reportFilter) == semester)
        {
            ShowReport(cached ?? workloads.Where(row => row.SemesterId == semester)
                .GroupBy(row => (row.TeacherId, row.TeacherName))
                .Select(group => new TeacherReport(group.Key.TeacherId, group.Key.TeacherName,
                    group.Sum(row => row.LectureHours), group.Sum(row => row.PracticalHours),
                    group.Sum(row => row.TotalHours), group.Count()))
                .OrderBy(row => row.TeacherName).ToArray());
        }
    }

    private void ShowReport(TeacherReport[] rows)
    {
        report = rows;
        reportGrid.DataSource = report;
        reportCount.Text = $"Преподавателей: {report.Length}     ·     Контактных часов: {report.Sum(row => row.TotalHours)}";
        UpdateButtons();
    }

    private async Task ExportAsync()
    {
        if (report.Length == 0)
            return;
        using var dialog = new SaveFileDialog
        {
            AddExtension = true, DefaultExt = "csv", Filter = "Таблица CSV (*.csv)|*.csv",
            FileName = $"Нагрузка_семестр_{Selection(reportFilter)}.csv",
            Title = "Сохранить отчёт"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        await WriteReportCsvAsync(dialog.FileName, report);
        Status("Отчёт сохранён в формате CSV.");
    }

    internal static async Task WriteReportCsvAsync(string path, TeacherReport[] rows)
    {
        await using var output = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await output.WriteLineAsync("Преподаватель;Лекции;Практика;Контактные часы;Назначения");
        foreach (var row in rows)
        {
            var name = row.TeacherName.Trim();
            if (name.Length > 0 && "=+-@".Contains(name[0]))
                name = "'" + name;
            name = "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            await output.WriteLineAsync($"{name};{row.LectureHours};{row.PracticalHours};{row.TotalHours};{row.AssignmentCount}");
        }
    }

    private void BindFilters()
    {
        changingFilters = true;
        try
        {
            Rebind(semesterFilter, catalog.Semesters, "Все семестры");
            Rebind(groupFilter, catalog.Groups, "Все группы");
            Rebind(teacherFilter, catalog.Teachers, "Все преподаватели");
            Rebind(reportFilter, catalog.Semesters, all: null);
        }
        finally
        {
            changingFilters = false;
        }
    }

    private static void Rebind(ComboBox filter, CatalogItem[] values, string? all)
    {
        var currentValue = Selection(filter);
        CatalogItem[] list = all is null ? values : [new CatalogItem(0, all), .. values];
        filter.DataSource = list;
        if (list.Any(item => item.Id == currentValue))
            filter.SelectedValue = currentValue;
    }

    private static int Selection(ComboBox box) => box.SelectedValue is int id ? id : 0;

    private void ApplyWorkloadFilters()
    {
        if (changingFilters)
            return;
        var semester = Selection(semesterFilter);
        var group = Selection(groupFilter);
        var teacher = Selection(teacherFilter);
        var query = search.Text.Trim();
        var filtered = workloads.Where(row =>
            (semester == 0 || row.SemesterId == semester)
            && (group == 0 || row.GroupId == group)
            && (teacher == 0 || row.TeacherId == teacher)
            && (query.Length == 0 || row.DisciplineName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || row.GroupName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || row.TeacherName.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            .ToArray();
        workloadGrid.DataSource = filtered;
        workloadCount.Text = $"Назначений: {filtered.Length}     ·     Студенческих часов: {filtered.Sum(row => row.StudentHours)}";
        UpdateButtons();
    }

    private async Task EditSelectedAsync()
    {
        if (workloadGrid.CurrentRow?.DataBoundItem is Workload row)
            await EditAsync(row);
    }

    private async Task EditAsync(Workload? value)
    {
        if (busy || !session.CanEdit)
            return;
        using var editor = new WorkloadForm(catalog, value);
        if (editor.ShowDialog(this) != DialogResult.OK || editor.Value is not { } input)
            return;
        await RunAsync(() => SaveMutationAsync(value is null ? "POST" : "PUT", value, input));
    }

    private async Task DeleteSelectedAsync()
    {
        if (busy || !session.CanEdit || workloadGrid.CurrentRow?.DataBoundItem is not Workload row)
            return;
        var result = MessageBox.Show(this, $"Удалить назначение «{row.DisciplineName}» для группы {row.GroupName}?",
            "Удаление назначения", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (result == DialogResult.Yes)
            await RunAsync(() => SaveMutationAsync("DELETE", row, null));
    }

    private async Task SaveMutationAsync(string method, Workload? row, WorkloadInput? input)
    {
        if (row is not null && queued.Any(item => item.RecordId == row.Id))
        {
            Status("Для этого назначения уже есть операция в офлайн-очереди.", warning: true);
            return;
        }
        var discipline = row?.DisciplineName ?? NameOf(catalog.Disciplines, input!.DisciplineId);
        var group = row?.GroupName ?? NameOf(catalog.Groups, input!.GroupId);
        await store.EnqueueAsync(new Mutation
        {
            Username = session.Username,
            Method = method,
            RecordId = row?.Id,
            Revision = input?.Revision ?? row?.Revision ?? 0,
            Description = $"{group} · {discipline}",
            Body = input is null ? null : JsonSerializer.Serialize(input, Payload.Options)
        });
        await RefreshQueueAsync();
        if (online)
            await RefreshAllAsync();
        else
        {
            SelectView(View.Queue);
            Status("Операция сохранена. Она будет отправлена после подключения.", warning: true);
        }
    }

    private static string NameOf(CatalogItem[] items, int id) =>
        items.First(item => item.Id == id).Name;

    private async Task RefreshQueueAsync()
    {
        queued = await store.ListAsync(session.Username);
        queueGrid.DataSource = queued.Select(item => new QueueRow(
            item.Sequence, item.CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
            item.ActionName, item.Description, item.Error ?? "Ожидает отправки")).ToArray();
        queueCount.Text = $"В очереди: {queued.Length}     ·     Передано за сеанс: {completedSinceLaunch}";
        queueNav.Text = queued.Length > 0 ? $"Офлайн-очередь ({queued.Length})" : "Офлайн-очередь";
        UpdateButtons();
    }

    private async Task DiscardSelectedAsync()
    {
        if (!session.CanEdit || queueGrid.CurrentRow?.DataBoundItem is not QueueRow row)
            return;
        var result = MessageBox.Show(this, "Снять выбранную операцию с отправки?",
            "Офлайн-очередь", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (result == DialogResult.Yes)
            await RunAsync(async () =>
            {
                await store.RemoveAsync(session.Username, row.Sequence);
                await RefreshQueueAsync();
                Status("Операция снята с отправки.");
            });
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy || IsDisposed)
            return;
        if (expired || !session.IsValid)
        {
            SetExpired();
            return;
        }
        busy = true;
        UpdateButtons();
        try
        {
            await action();
        }
        catch (ApiRejectedException ex) when (ex.Status == HttpStatusCode.Unauthorized)
        {
            SetExpired();
        }
        catch (ApiTransportException ex)
        {
            SetConnection(false);
            Status(ex.Message, warning: true);
        }
        catch (Exception ex)
        {
            Status(ex.Message, warning: true);
        }
        finally
        {
            busy = false;
            UpdateButtons();
        }
        if (refreshReportPending)
        {
            refreshReportPending = false;
            if (current == View.Report && !expired)
                await RefreshReportAsync();
        }
    }

    private void SetExpired()
    {
        expired = true;
        online = false;
        connectionLabel.Text = "Сеанс истёк";
        connectionLabel.ForeColor = Theme.Error;
        Status("Выйдите и войдите снова, чтобы продолжить работу.", warning: true);
        UpdateButtons();
    }

    private void SetConnection(bool connected)
    {
        online = connected;
        connectionLabel.Text = online ? "На связи" : "Автономно";
        connectionLabel.ForeColor = online ? Theme.Accent : Theme.Caution;
    }

    private void Status(string text, bool warning = false)
    {
        outcome.Text = text;
        outcome.ForeColor = warning ? Theme.Error : Theme.Muted;
    }

    private void UpdateButtons()
    {
        var ready = !busy && !expired;
        var editing = ready && session.CanEdit;
        refresh.Enabled = ready;
        signOut.Enabled = !busy;
        add.Enabled = editing && catalog.Disciplines.Length != 0 && catalog.Groups.Length != 0
            && catalog.Teachers.Length != 0 && catalog.Semesters.Length != 0;
        edit.Enabled = editing && workloadGrid.CurrentRow?.DataBoundItem is Workload;
        delete.Enabled = edit.Enabled;
        discard.Enabled = editing && queueGrid.CurrentRow?.DataBoundItem is QueueRow;
        replay.Enabled = editing;
        export.Enabled = ready && report.Length > 0;
    }

    private sealed record QueueRow(int Sequence, string LocalTime, string Action, string Description, string Status);
}
