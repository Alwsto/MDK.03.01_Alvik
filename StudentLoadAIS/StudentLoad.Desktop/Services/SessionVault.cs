using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StudentLoad.Desktop.Services;

internal sealed class SessionVault(ClientSettings settings)
{
    private readonly string path = Path.Combine(settings.DataFolder, "session.dpapi");
    private readonly byte[] entropy = Encoding.UTF8.GetBytes(
        "StudentLoadAIS/session/v1/" + settings.ApiUrl.AbsoluteUri);

    public Session? Load()
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var data = ProtectedData.Unprotect(
                File.ReadAllBytes(path), entropy, DataProtectionScope.CurrentUser);
            var session = JsonSerializer.Deserialize<Session>(data, Payload.Options);
            return session is { IsValid: true } ? session : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or CryptographicException or JsonException)
        {
            return null;
        }
    }

    public void Save(Session session)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(session, Payload.Options);
        var encrypted = ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public void Clear() => File.Delete(path);
}
