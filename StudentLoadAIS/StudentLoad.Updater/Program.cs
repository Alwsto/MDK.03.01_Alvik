using System.Text;
using System.Text.Json;
using StudentLoad.Updater;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length is < 2 or > 3 ||
    !Uri.TryCreate(args[0], UriKind.Absolute, out var manifestUri) ||
    (args.Length == 3 && args[2] != "--allow-loopback-http"))
{
    Console.Error.WriteLine("Использование: StudentLoad.Updater <адрес-манифеста> <каталог-установки> [--allow-loopback-http]");
    return 2;
}

using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
using var handler = new HttpClientHandler { AllowAutoRedirect = false };
using var http = new HttpClient(handler);
var installer = new UpdateInstaller(http, args.Length == 3);
try
{
    Console.WriteLine(await installer.InstallAsync(manifestUri, Path.GetFullPath(args[1]), timeout.Token));
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
    or HttpRequestException or FormatException or JsonException or OperationCanceledException or ArgumentException)
{
    Console.Error.WriteLine($"Обновление отклонено. {error.Message}");
    return 1;
}

