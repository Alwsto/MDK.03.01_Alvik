using System.Security.Cryptography;
using System.Text;

namespace StudentLoad.Desktop.Services;

internal sealed record ClientSettings(Uri ApiUrl, string DataFolder, byte[]? CertificateHash)
{
    public static ClientSettings FromEnvironment()
    {
        var apiUrl = Environment.GetEnvironmentVariable("STUDENTLOAD_API_URL")
            ?? "http://127.0.0.1:5188";
        if (!Uri.TryCreate(apiUrl.TrimEnd('/') + "/", UriKind.Absolute, out var origin)
            || origin.Scheme is not ("http" or "https")
            || (origin.Scheme == "http" && !origin.IsLoopback)
            || origin.UserInfo.Length > 0 || origin.Query.Length > 0 || origin.Fragment.Length > 0
            || origin.AbsolutePath != "/")
        {
            throw new ArgumentException(
                "Укажите базовый адрес HTTPS без пути. HTTP поддерживается только на локальном компьютере.");
        }

        var configuredPin = Environment.GetEnvironmentVariable("STUDENTLOAD_CERT_SHA256");
        byte[]? certificateHash = null;
        if (!string.IsNullOrWhiteSpace(configuredPin))
        {
            var pin = configuredPin.Replace(":", "", StringComparison.Ordinal).Trim();
            if (origin.Scheme != "https" || pin.Length != 64 || !pin.All(Uri.IsHexDigit))
                throw new ArgumentException("Отпечаток сертификата требует HTTPS и 64 шестнадцатеричных символов.");
            certificateHash = Convert.FromHexString(pin);
        }

        var folder = Environment.GetEnvironmentVariable("STUDENTLOAD_CLIENT_DATA");
        if (string.IsNullOrWhiteSpace(folder))
            folder = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "StudentLoadAIS", "Client");

        var originHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(origin.AbsoluteUri)))[..16];
        var workspace = Path.Combine(Path.GetFullPath(folder), originHash);
        Directory.CreateDirectory(workspace);
        return new ClientSettings(origin, workspace, certificateHash);
    }
}
