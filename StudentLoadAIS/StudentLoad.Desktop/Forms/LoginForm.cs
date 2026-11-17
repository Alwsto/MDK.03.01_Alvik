using StudentLoad.Desktop.Services;

namespace StudentLoad.Desktop.Forms;

internal sealed partial class LoginForm : Form
{
    private readonly StudentLoadApi api;
    private readonly SessionVault vault;
    internal Session? Session { get; private set; }

    public LoginForm(StudentLoadApi api, SessionVault vault)
    {
        this.api = api;
        this.vault = vault;
        InitializeComponent();
        var existing = vault.Load();
        saved.Visible = existing is not null;
        saved.Click += (_, _) =>
        {
            if (vault.Load() is { } session)
                Continue(session);
            else
            {
                feedback.Text = "Срок сохранённого сеанса истёк. Войдите снова.";
                saved.Visible = false;
            }
        };
        if (existing is not null)
            username.Text = existing.Username;
        signIn.Click += async (_, _) => await SignInAsync();
    }

    private async Task SignInAsync()
    {
        if (string.IsNullOrWhiteSpace(username.Text) || password.Text.Length == 0)
        {
            feedback.Text = "Заполните имя пользователя и пароль.";
            return;
        }

        signIn.Enabled = false;
        saved.Enabled = false;
        feedback.Text = "";
        UseWaitCursor = true;
        try
        {
            var session = await api.LoginAsync(username.Text.Trim(), password.Text);
            if (remember.Checked)
                vault.Save(session);
            else
                vault.Clear();
            Continue(session);
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                feedback.Text = ex.Message;
                password.SelectAll();
                password.Focus();
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                signIn.Enabled = true;
                saved.Enabled = true;
                UseWaitCursor = false;
            }
        }
    }

    private void Continue(Session session)
    {
        Session = session;
        password.Clear();
        DialogResult = DialogResult.OK;
        Close();
    }
}
