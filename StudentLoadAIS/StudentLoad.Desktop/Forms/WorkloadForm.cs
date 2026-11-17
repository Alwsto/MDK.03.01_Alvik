using StudentLoad.Desktop.Services;

namespace StudentLoad.Desktop.Forms;

internal sealed partial class WorkloadForm : Form
{
    internal WorkloadInput? Value { get; private set; }

    public WorkloadForm(Catalog catalog, Workload? source)
    {
        Text = source is null ? "Новое назначение" : "Изменение назначения";
        InitializeComponent();
        Bind(group, catalog.Groups, source?.GroupId);
        Bind(discipline, catalog.Disciplines, source?.DisciplineId);
        Bind(teacher, catalog.Teachers, source?.TeacherId);
        Bind(semester, catalog.Semesters, source?.SemesterId);
        lecture.Value = source?.LectureHours ?? 0;
        practical.Value = source?.PracticalHours ?? 0;
        independent.Value = source?.SelfStudyHours ?? 0;
        lecture.ValueChanged += (_, _) => UpdateTotal();
        practical.ValueChanged += (_, _) => UpdateTotal();
        independent.ValueChanged += (_, _) => UpdateTotal();
        save.Click += (_, _) => Confirm(source?.Revision ?? 0);
        UpdateTotal();
    }

    private static void Bind(ComboBox combo, CatalogItem[] data, int? selected)
    {
        combo.DataSource = data;
        combo.SelectedIndex = -1;
        if (selected.HasValue)
            combo.SelectedValue = selected.Value;
    }

    private void UpdateTotal()
    {
        var contact = lecture.Value + practical.Value;
        total.Text = $"Контактные часы: {contact}     ·     Всего у студента: {contact + independent.Value}";
    }

    private void Confirm(int revision)
    {
        if (group.SelectedValue is not int groupId || discipline.SelectedValue is not int disciplineId
            || teacher.SelectedValue is not int teacherId || semester.SelectedValue is not int semesterId)
        {
            warning.Text = "Выберите семестр, группу, дисциплину и преподавателя.";
            return;
        }
        if (lecture.Value + practical.Value == 0)
        {
            warning.Text = "Укажите хотя бы один час лекций или практики.";
            return;
        }

        Value = new WorkloadInput(groupId, disciplineId, teacherId, semesterId,
            (int)lecture.Value, (int)practical.Value, (int)independent.Value, revision);
        DialogResult = DialogResult.OK;
        Close();
    }
}
