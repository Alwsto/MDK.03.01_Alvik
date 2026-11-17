using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

internal sealed class ApiProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _stdout;
    private readonly Task<string> _stderr;
    public Uri Address { get; }

    private ApiProcess(Process process, Uri address)
    {
        _process = process;
        _stdout = process.StandardOutput.ReadToEndAsync();
        _stderr = process.StandardError.ReadToEndAsync();
        Address = address;
    }

    public static async Task MigrateAsync(string assembly, string database, string? target = null)
    {
        var start = StartInfo(assembly, database);
        start.ArgumentList.Add("--migrate-only");
        if (target is not null)
            start.ArgumentList.Add(target);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить миграцию.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException($"Миграция превысила 30 секунд. {await stderr}");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Миграция завершилась с кодом {process.ExitCode}. {await stdout}\n{await stderr}");
    }

    public static async Task<ApiProcess> StartAsync(string assembly, string database)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var address = new Uri($"http://127.0.0.1:{port}");
        var start = StartInfo(assembly, database);
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add(address.AbsoluteUri.TrimEnd('/'));
        var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить тестовый API.");
        var host = new ApiProcess(process, address);
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromMilliseconds(500) };

        for (var attempt = 0; attempt < 75 && !process.HasExited; attempt++)
        {
            try
            {
                using var response = await client.GetAsync("/health");
                if (response.IsSuccessStatusCode)
                    return host;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }

            await Task.Delay(200);
        }

        await host.StopAsync();
        var output = $"{await host._stdout}\n{await host._stderr}";
        process.Dispose();
        throw new InvalidOperationException($"Тестовый API не подтвердил готовность. {output}");
    }

    private static ProcessStartInfo StartInfo(string assembly, string database)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(assembly)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(assembly);
        start.Environment["STUDENTLOAD_DATABASE"] = database;
        start.Environment["STUDENTLOAD_DEMO_ACCOUNTS"] = "true";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        return start;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _process.Dispose();
    }

    private async Task StopAsync()
    {
        if (!_process.HasExited)
            _process.Kill(entireProcessTree: true);

        await _process.WaitForExitAsync();
        await Task.WhenAll(_stdout, _stderr);
    }
}
