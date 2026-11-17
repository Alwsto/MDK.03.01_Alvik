namespace StudentLoad.Desktop.Forms;

internal sealed partial class LoginForm
{
    private TextBox username = null!;
    private TextBox password = null!;
    private Label feedback = null!;
    private CheckBox remember = null!;
    private Button signIn = null!;
    private Button saved = null!;

    private void InitializeComponent()
    {
        SuspendLayout();
        Font = Theme.Font();
        Text = "Вход | АИС управления студенческой нагрузкой";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(860, 492);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Color.White;

        var identity = new Panel
        {
            Width = 343, Dock = DockStyle.Left, BackColor = Theme.Navy
        };
        var product = Theme.Label("АИС", 12, true, Color.FromArgb(124, 222, 220));
        product.Location = new Point(32, 39);
        var title = Theme.Label("Управление\nстуденческой\nнагрузкой", 28, true, Color.White);
        title.Location = new Point(30, 108);
        var description = Theme.Label("Учебные планы, распределение\nчасов и отчётность по семестрам.", 11, false,
            Color.FromArgb(201, 219, 231));
        description.Location = new Point(34, 275);
        var footer = Theme.Label("МДК 03.01  /  ВАРИАНТ 5", 9.5f, true, Color.FromArgb(124, 222, 220));
        footer.Location = new Point(34, 429);
        identity.Controls.AddRange([product, title, description, footer]);

        var access = Theme.Label("Вход в систему", 24, true);
        access.Location = new Point(386, 44);
        var prompt = Theme.Label("Укажите данные вашей учётной записи.", 10.5f, false, Theme.Muted);
        prompt.Location = new Point(388, 105);

        var userLabel = Theme.Label("Имя пользователя", 10, true);
        userLabel.Location = new Point(388, 155);
        username = new TextBox
        {
            Name = "Username", Location = new Point(388, 182), Width = 420, Font = Theme.Font(12),
            AccessibleName = "Имя пользователя", MaxLength = 100, TabIndex = 0
        };
        var passwordLabel = Theme.Label("Пароль", 10, true);
        passwordLabel.Location = new Point(388, 234);
        password = new TextBox
        {
            Name = "Password", Location = new Point(388, 261), Width = 420, Font = Theme.Font(12),
            UseSystemPasswordChar = true, AccessibleName = "Пароль", MaxLength = 256, TabIndex = 1
        };
        remember = new CheckBox
        {
            Name = "RememberSession", Text = "Сохранить сеанс на этом компьютере",
            Font = Theme.Font(10), AutoSize = true, ForeColor = Theme.Muted,
            Location = new Point(388, 309), TabIndex = 2
        };
        signIn = Theme.Button("Войти", 137, primary: true);
        signIn.Name = "SignIn";
        signIn.Location = new Point(388, 355);
        signIn.TabIndex = 3;
        saved = Theme.Button("Продолжить сеанс", 178);
        saved.Name = "ResumeSession";
        saved.Location = new Point(539, 355);
        saved.TabIndex = 4;
        feedback = Theme.Label("", 10, false, Theme.Error);
        feedback.AutoSize = false;
        feedback.Bounds = new Rectangle(388, 405, 421, 65);

        AcceptButton = signIn;
        Controls.AddRange([identity, access, prompt, userLabel, username, passwordLabel, password,
            remember, signIn, saved, feedback]);
        ResumeLayout(false);
        PerformLayout();
    }
}
