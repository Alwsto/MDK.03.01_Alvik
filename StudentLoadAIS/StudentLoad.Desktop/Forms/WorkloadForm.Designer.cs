namespace StudentLoad.Desktop.Forms;

internal sealed partial class WorkloadForm
{
    private ComboBox group = null!;
    private ComboBox discipline = null!;
    private ComboBox teacher = null!;
    private ComboBox semester = null!;
    private NumericUpDown lecture = null!;
    private NumericUpDown practical = null!;
    private NumericUpDown independent = null!;
    private Label total = null!;
    private Label warning = null!;
    private Button save = null!;

    private void InitializeComponent()
    {
        SuspendLayout();
        Font = Theme.Font();
        Name = "WorkloadEditor";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(648, 496);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.White;

        var title = Theme.Label(Text, 21, true);
        title.Location = new Point(25, 22);
        var prompt = Theme.Label("Выберите назначение и укажите академические часы.", 10, false, Theme.Muted);
        prompt.Location = new Point(27, 66);

        semester = Theme.Combo("Семестр", 283);
        group = Theme.Combo("Группа", 283);
        discipline = Theme.Combo("Дисциплина", 283);
        teacher = Theme.Combo("Преподаватель", 283);
        AddField("Семестр", semester, 27, 114);
        AddField("Группа", group, 335, 114);
        AddField("Дисциплина", discipline, 27, 184);
        AddField("Преподаватель", teacher, 335, 184);

        var divider = new Panel { BackColor = Theme.Border, Bounds = new Rectangle(27, 253, 592, 1) };
        lecture = Number("Лекции", 173);
        practical = Number("Практика", 173);
        independent = Number("Самост. работа", 173);
        AddField("Лекции", lecture, 27, 271);
        AddField("Практика", practical, 232, 271);
        AddField("Самостоятельная работа", independent, 437, 271);

        total = Theme.Label("", 10.5f, true, Theme.Accent);
        total.Location = new Point(27, 342);
        warning = Theme.Label("", 9.5f, false, Theme.Error);
        warning.Location = new Point(28, 380);
        warning.MaximumSize = new Size(590, 45);
        save = Theme.Button("Сохранить", 133, primary: true);
        save.Name = "SaveWorkload";
        save.Location = new Point(486, 434);
        var cancel = Theme.Button("Отмена", 105);
        cancel.Location = new Point(366, 434);
        cancel.DialogResult = DialogResult.Cancel;
        AcceptButton = save;
        CancelButton = cancel;
        Controls.AddRange([title, prompt, divider, total, warning, save, cancel]);
        ResumeLayout(false);
        PerformLayout();
    }

    private void AddField(string caption, Control field, int left, int top)
    {
        var label = Theme.Label(caption, 9.5f, true, Theme.Muted);
        label.Location = new Point(left, top);
        field.Location = new Point(left, top + 25);
        Controls.AddRange([label, field]);
    }

    private static NumericUpDown Number(string label, int width) => new()
    {
        Name = label, AccessibleName = label, Width = width, Maximum = 1000,
        ThousandsSeparator = true, Font = Theme.Font(11)
    };
}
