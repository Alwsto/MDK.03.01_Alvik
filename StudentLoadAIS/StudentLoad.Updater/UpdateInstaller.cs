using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace StudentLoad.Updater;

internal sealed record UpdateManifest(string Version, string Package, string Sha256);

internal sealed class UpdateInstaller(HttpClient http, bool allowLoopbackHttp)
{
    private const long PackageLimit = 150 * 1024 * 1024;
    private const long ExpandedLimit = 500 * 1024 * 1024;
    private const int EntryLimit = 10_000;

    public async Task<string> InstallAsync(Uri manifestUri, string root, CancellationToken token)
    {
        ValidateUri(manifestUri);
        var manifest = await http.GetFromJsonAsync<UpdateManifest>(manifestUri, token)
            ?? throw new InvalidDataException("Манифест обновления пуст.");
        var target = SemVersion.Parse(manifest.Version ?? "");
        if (!Uri.TryCreate(manifestUri, manifest.Package, out var packageUri)
            || packageUri.GetLeftPart(UriPartial.Authority) != manifestUri.GetLeftPart(UriPartial.Authority))
            throw new InvalidDataException("Пакет должен размещаться на сервере манифеста.");
        ValidateUri(packageUri);
        var expected = Convert.FromHexString(manifest.Sha256 ?? "");
        if (expected.Length != 32)
            throw new InvalidDataException("Ожидается SHA-256 в виде 64 шестнадцатеричных знаков.");

        var versions = Directory.CreateDirectory(Path.Combine(root, "versions")).FullName;
        using var updateLock = new FileStream(Path.Combine(root, ".update.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var pointerPath = Path.Combine(root, "active-version.txt");
        if (File.Exists(pointerPath))
        {
            var installed = SemVersion.Parse((await File.ReadAllTextAsync(pointerPath, token)).Trim());
            if (target.CompareTo(installed) <= 0)
                return $"Текущая версия {installed.Value}. Обновление не требуется.";
        }

        var staging = Directory.CreateDirectory(Path.Combine(versions, ".stage-" + Guid.NewGuid())).FullName;
        var packagePath = Path.Combine(staging, "package.zip");
        var extracted = Path.Combine(staging, "files");
        try
        {
            await DownloadAsync(packageUri, packagePath, token);
            await using (var package = File.OpenRead(packagePath))
            {
                var actual = await SHA256.HashDataAsync(package, token);
                if (!CryptographicOperations.FixedTimeEquals(actual, expected))
                    throw new InvalidDataException("SHA-256 пакета не совпадает с манифестом.");
            }

            ExtractPackage(packagePath, extracted);
            var destination = Path.Combine(versions, target.Value);
            if (Directory.Exists(destination))
            {
                // Публикация каталога могла завершиться до смены указателя.
                var filesMarker = Path.Combine(destination, ".release-sha256");
                if (!File.Exists(filesMarker) ||
                    !string.Equals(File.ReadAllText(filesMarker), Convert.ToHexString(expected), StringComparison.Ordinal))
                    throw new IOException($"Каталог версии {target.Value} занят другим пакетом.");
                if (!File.Exists(Path.Combine(destination, "StudentLoad.Desktop.exe")))
                    throw new IOException($"Каталог версии {target.Value} повреждён.");
            }
            else
            {
                File.WriteAllText(Path.Combine(extracted, ".release-sha256"), Convert.ToHexString(expected));
                Directory.Move(extracted, destination);
            }

            var pointerTemp = Path.Combine(staging, "active-version.txt");
            await File.WriteAllTextAsync(pointerTemp, target.Value + Environment.NewLine, Encoding.UTF8, token);
            File.Move(pointerTemp, pointerPath, overwrite: true);
            return $"Установлена версия {target.Value}. SHA-256 подтверждён.";
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    private void ValidateUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.UserInfo.Length != 0 ||
            !(uri.Scheme == Uri.UriSchemeHttps ||
              (allowLoopbackHttp && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            throw new InvalidDataException("Источник обновлений должен использовать HTTPS. HTTP доступен только для локальной проверки.");
    }

    private async Task DownloadAsync(Uri uri, string path, CancellationToken token)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > PackageLimit)
            throw new InvalidDataException("Пакет превышает допустимый размер.");

        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var file = File.Create(path);
        var buffer = new byte[64 * 1024];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, token)) != 0)
        {
            total += count;
            if (total > PackageLimit)
                throw new InvalidDataException("Пакет превышает допустимый размер.");
            await file.WriteAsync(buffer.AsMemory(0, count), token);
        }
    }

    private static void ExtractPackage(string path, string target)
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count > EntryLimit ||
            archive.Entries.Sum(entry => entry.Length) > ExpandedLimit)
            throw new InvalidDataException("Содержимое архива превышает допустимый размер.");
        archive.ExtractToDirectory(target);
        if (!File.Exists(Path.Combine(target, "StudentLoad.Desktop.exe")))
            throw new InvalidDataException("Отсутствует StudentLoad.Desktop.exe в корне пакета.");
    }
}

