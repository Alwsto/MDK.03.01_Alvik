using StudentLoad.Desktop.Services;

namespace StudentLoad.Desktop.Forms;

internal sealed partial class MainForm
{
    private Panel viewHost = null!;
    private TableLayoutPanel workloadPage = null!;
    private TableLayoutPanel reportPage = null!;
    private TableLayoutPanel queuePage = null!;
    private Label pageTitle = null!;
    private Label pageDescription = null!;
    private Label identity = null!;
    private Label connectionLabel = null!;
    private Label outcome = null!;
    private Label origin = null!;
    private Button workloadNav = null!;
    private Button reportNav = null!;
    private Button queueNav = null!;
    private Button signOut = null!;
    private Button refresh = null!;
    private Button add = null!;
    private Button edit = null!;
    private Button delete = null!;
    private Button export = null!;
    private Button replay = null!;
    private Button discard = null!;
    private ComboBox semesterFilter = null!;
    private ComboBox groupFilter = null!;
    private ComboBox teacherFilter = null!;
    private ComboBox reportFilter = null!;
    private TextBox search = null!;
    private DataGridView workloadGrid = null!;
    private DataGridView reportGrid = null!;
    private DataGridView queueGrid = null!;
    private Label workloadCount = null!;
    private Label reportCount = null!;
    private Label queueCount = null!;

    private void InitializeComponent()
    {
        SuspendLayout();
        Name = "StudentLoadMain";
        Font = Theme.Font();
        Text = "АИС управления студенческой нагрузкой";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1240, 741);
        MinimumSize = new Size(1060, 610);
        BackColor = Theme.Canvas;

        var frame = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 225));
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        frame.Controls.Add(CreateSidebar(), 0, 0);

        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty,
            Padding = new Padding(28, 22, 28, 12)
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 99));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        workspace.Controls.Add(CreateHeader(), 0, 0);

        viewHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        workloadPage = CreateWorkloadPage();
        reportPage = CreateReportPage();
        queuePage = CreateQueuePage();
        viewHost.Controls.AddRange([workloadPage, reportPage, queuePage]);
        workspace.Controls.Add(viewHost, 0, 1);
        workspace.Controls.Add(CreateFooter(), 0, 2);
        frame.Controls.Add(workspace, 1, 0);
        Controls.Add(frame);
        ResumeLayout(false);
    }

    private Control CreateSidebar()
    {
        var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Navy, Margin = Padding.Empty };
        var badge = Theme.Label("АИС", 10, true, Color.FromArgb(124, 222, 220));
        badge.Location = new Point(23, 29);
        var name = Theme.Label("Студенческая\nнагрузка", 17, true, Color.White);
        name.Location = new Point(22, 52);
        var line = new Panel
        {
            BackColor = Theme.NavyLight, Bounds = new Rectangle(22, 126, 179, 1)
        };
        workloadNav = Navigation("Учебная нагрузка", 156, "WorkloadPage");
        reportNav = Navigation("Отчёт по нагрузке", 207, "ReportPage");
        queueNav = Navigation("Офлайн-очередь", 258, "QueuePage");

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 137, BackColor = Theme.Navy };
        var separator = new Panel
        {
            BackColor = Theme.NavyLight, Bounds = new Rectangle(22, 8, 179, 1)
        };
        identity = Theme.Label("", 9.4f, true, Color.White);
        identity.AutoSize = false;
        identity.Bounds = new Rectangle(23, 26, 183, 43);
        signOut = Theme.Button("Выйти", 180);
        signOut.Name = "SignOut";
        signOut.Location = new Point(22, 78);
        signOut.BackColor = Theme.NavyLight;
        signOut.ForeColor = Color.White;
        signOut.FlatAppearance.BorderColor = Theme.NavyLight;
        signOut.FlatAppearance.MouseOverBackColor = Theme.Accent;
        bottom.Controls.AddRange([separator, identity, signOut]);
        sidebar.Controls.AddRange([badge, name, line, workloadNav, reportNav, queueNav, bottom]);
        return sidebar;
    }

    private static Button Navigation(string text, int top, string name)
    {
        var button = Theme.Button(text, 201);
        button.Name = name;
        button.Height = 43;
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Padding = new Padding(12, 0, 4, 0);
        button.Location = new Point(12, top);
        button.BackColor = Theme.Navy;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Theme.NavyLight;
        return button;
    }

    private Control CreateHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 255));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        pageTitle = Theme.Label("", 26, true);
        pageTitle.Anchor = AnchorStyles.Left;
        pageTitle.Margin = Padding.Empty;
        pageDescription = Theme.Label("", 10, false, Theme.Muted);
        pageDescription.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        pageDescription.Margin = new Padding(2, 7, 0, 0);
        connectionLabel = Theme.Label("Подключение…", 9.5f, true, Theme.Muted);
        connectionLabel.Anchor = AnchorStyles.Right;
        connectionLabel.Margin = new Padding(0, 0, 12, 0);
        refresh = Theme.Button("Обновить", 102);
        refresh.Name = "Refresh";
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
            Padding = new Padding(0, 9, 0, 0), Margin = Padding.Empty
        };
        refresh.Margin = Padding.Empty;
        connectionLabel.Margin = new Padding(0, 8, 12, 0);
        actions.Controls.AddRange([refresh, connectionLabel]);
        header.Controls.Add(pageTitle, 0, 0);
        header.Controls.Add(actions, 1, 0);
        header.Controls.Add(pageDescription, 0, 1);
        header.SetColumnSpan(pageDescription, 2);
        return header;
    }

    private Control CreateFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outcome = Theme.Label("", 9.15f, false, Theme.Muted);
        outcome.AutoSize = false;
        outcome.Dock = DockStyle.Fill;
        outcome.AutoEllipsis = true;
        outcome.TextAlign = ContentAlignment.MiddleLeft;
        outcome.Margin = Padding.Empty;
        origin = Theme.Label("", 9, false, Theme.Muted);
        origin.Anchor = AnchorStyles.Right;
        footer.Controls.Add(outcome, 0, 0);
        footer.Controls.Add(origin, 1, 0);
        return footer;
    }

    private TableLayoutPanel CreateWorkloadPage()
    {
        var page = Page(78, 45);
        var filters = Flow(new Padding(12, 9, 6, 0));
        semesterFilter = Theme.Combo("Семестр", 195);
        groupFilter = Theme.Combo("Группа", 139);
        teacherFilter = Theme.Combo("Преподаватель", 185);
        search = new TextBox
        {
            Name = "Search", AccessibleName = "Поиск", Width = 181, Font = Theme.Font(10), MaxLength = 100,
            PlaceholderText = "Дисциплина или группа"
        };
        filters.Controls.AddRange([
            Labeled("Семестр", semesterFilter, 195), Labeled("Группа", groupFilter, 139),
            Labeled("Преподаватель", teacherFilter, 185), Labeled("Поиск", search, 181)
        ]);
        page.Controls.Add(filters, 0, 0);

        var actions = Flow(new Padding(0, 0, 0, 0));
        add = Theme.Button("Добавить", 108, primary: true);
        add.Name = "AddWorkload";
        edit = Theme.Button("Изменить", 109);
        edit.Name = "EditWorkload";
        delete = Theme.Button("Удалить", 98);
        delete.Name = "DeleteWorkload";
        workloadCount = Theme.Label("", 9.5f, false, Theme.Muted);
        workloadCount.Margin = new Padding(8, 8, 0, 0);
        actions.Controls.AddRange([add, edit, delete, workloadCount]);
        page.Controls.Add(actions, 0, 1);

        workloadGrid = Theme.CreateGrid("Записи учебной нагрузки");
        workloadGrid.Name = "WorkloadGrid";
        Theme.Column(workloadGrid, nameof(Workload.GroupName), "Группа", 1, 74);
        Theme.Column(workloadGrid, nameof(Workload.DisciplineName), "Дисциплина", 36, 132);
        Theme.Column(workloadGrid, nameof(Workload.TeacherName), "Преподаватель", 34, 135);
        Theme.Column(workloadGrid, nameof(Workload.SemesterName), "Семестр", 30, 115);
        Theme.Column(workloadGrid, nameof(Workload.LectureHours), "Лекции", 1, 55);
        Theme.Column(workloadGrid, nameof(Workload.PracticalHours), "Практика", 1, 62);
        Theme.Column(workloadGrid, nameof(Workload.SelfStudyHours), "Самост.", 1, 64);
        Theme.Column(workloadGrid, nameof(Workload.StudentHours), "Всего", 1, 52);
        foreach (var property in new[] { nameof(Workload.DisciplineName), nameof(Workload.TeacherName), nameof(Workload.SemesterName) })
            workloadGrid.Columns[property].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        FixWidth(nameof(Workload.GroupName), 84);
        FixWidth(nameof(Workload.LectureHours), 71);
        FixWidth(nameof(Workload.PracticalHours), 78);
        FixWidth(nameof(Workload.SelfStudyHours), 68);
        FixWidth(nameof(Workload.StudentHours), 55);
        page.Controls.Add(workloadGrid, 0, 2);
        return page;

        void FixWidth(string property, int width)
        {
            var column = workloadGrid.Columns[property];
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            column.Width = width;
        }
    }

    private TableLayoutPanel CreateReportPage()
    {
        var page = Page(78, 45);
        var filters = Flow(new Padding(12, 9, 6, 0));
        reportFilter = Theme.Combo("Семестр отчёта", 228);
        filters.Controls.Add(Labeled("Семестр", reportFilter, 228));
        export = Theme.Button("Экспорт CSV", 132, primary: true);
        export.Name = "ExportReport";
        export.Margin = new Padding(6, 16, 0, 0);
        filters.Controls.Add(export);
        page.Controls.Add(filters, 0, 0);

        reportCount = Theme.Label("", 11, true);
        reportCount.Anchor = AnchorStyles.Left;
        page.Controls.Add(reportCount, 0, 1);

        reportGrid = Theme.CreateGrid("Отчёт по часам преподавателей");
        reportGrid.Name = "ReportGrid";
        Theme.Column(reportGrid, nameof(TeacherReport.TeacherName), "Преподаватель", 38, 185);
        Theme.Column(reportGrid, nameof(TeacherReport.LectureHours), "Лекции, ч", 14, 86);
        Theme.Column(reportGrid, nameof(TeacherReport.PracticalHours), "Практика, ч", 15, 93);
        Theme.Column(reportGrid, nameof(TeacherReport.TotalHours), "Контактные часы", 18, 140);
        Theme.Column(reportGrid, nameof(TeacherReport.AssignmentCount), "Назначения", 15, 96);
        page.Controls.Add(reportGrid, 0, 2);
        return page;
    }

    private TableLayoutPanel CreateQueuePage()
    {
        var page = Page(56, 45);
        var actions = Flow(new Padding(0, 0, 0, 0));
        replay = Theme.Button("Синхронизировать", 169, primary: true);
        replay.Name = "ReplayQueue";
        discard = Theme.Button("Снять операцию", 149);
        discard.Name = "DiscardQueueItem";
        actions.Controls.AddRange([replay, discard]);
        page.Controls.Add(actions, 0, 0);

        queueCount = Theme.Label("", 11, true);
        queueCount.Anchor = AnchorStyles.Left;
        page.Controls.Add(queueCount, 0, 1);
        queueGrid = Theme.CreateGrid("Операции офлайн-очереди");
        queueGrid.Name = "QueueGrid";
        Theme.Column(queueGrid, "LocalTime", "Добавлено", 16, 125);
        Theme.Column(queueGrid, "Action", "Операция", 15, 103);
        Theme.Column(queueGrid, "Description", "Назначение", 34, 181);
        Theme.Column(queueGrid, "Status", "Состояние", 35, 192);
        page.Controls.Add(queueGrid, 0, 2);
        return page;
    }

    private static TableLayoutPanel Page(int top, int middle)
    {
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty
        };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, top));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, middle));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return page;
    }

    private static FlowLayoutPanel Flow(Padding padding) => new()
    {
        Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight,
        BackColor = padding.Left > 0 ? Color.White : Color.Transparent,
        Padding = padding, Margin = padding.Left > 0 ? new Padding(0, 0, 0, 10) : Padding.Empty
    };

    private static Control Labeled(string text, Control control, int width)
    {
        var panel = new Panel { Width = width, Height = 58, Margin = new Padding(0, 0, 13, 0) };
        var label = Theme.Label(text, 9, true, Theme.Muted);
        label.Location = new Point(0, 0);
        control.Location = new Point(0, 22);
        panel.Controls.AddRange([label, control]);
        return panel;
    }
}
