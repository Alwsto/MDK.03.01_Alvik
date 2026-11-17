using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Polly;
using Polly.Retry;
using StudentLoad.Desktop.Data;

namespace StudentLoad.Desktop.Services;

internal sealed class StudentLoadApi : IDisposable
{
    private readonly HttpClient http;
    private readonly ResiliencePipeline retries = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            Delay = TimeSpan.FromMilliseconds(300),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = new PredicateBuilder().Handle<ApiUnavailableException>()
        })
        .Build();

    public StudentLoadApi(ClientSettings settings)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        if (settings.CertificateHash is { } pin)
        {
            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
                MatchesTrustedCertificate(certificate, errors, pin);
        }
        http = new HttpClient(handler)
        {
            BaseAddress = settings.ApiUrl,
            Timeout = TimeSpan.FromSeconds(4)
        };
    }

    internal static bool MatchesTrustedCertificate(
        X509Certificate2? certificate, SslPolicyErrors errors, byte[] pin) =>
        errors == SslPolicyErrors.None && certificate is not null &&
        CryptographicOperations.FixedTimeEquals(certificate.GetCertHash(HashAlgorithmName.SHA256), pin);

    public Task<Session> LoginAsync(string username, string password) =>
        SendJsonAsync<Session>(HttpMethod.Post, "api/auth/login", null, new { username, password });

    public Task<Catalog> CatalogAsync(Session session) =>
        SendJsonAsync<Catalog>(HttpMethod.Get, "api/v2/catalog", session);

    public Task<Workload[]> WorkloadsAsync(Session session) =>
        SendJsonAsync<Workload[]>(HttpMethod.Get, "api/v2/workloads", session);

    public Task<TeacherReport[]> ReportAsync(Session session, int semesterId) =>
        SendJsonAsync<TeacherReport[]>(HttpMethod.Get,
            $"api/v2/reports/teachers?semesterId={semesterId}", session);

    public async Task SendMutationAsync(Session session, Mutation mutation)
    {
        var path = mutation.Method switch
        {
            "POST" => "api/v2/workloads",
            "PUT" => $"api/v2/workloads/{mutation.RecordId}",
            "DELETE" => $"api/v2/workloads/{mutation.RecordId}?revision={mutation.Revision}",
            _ => throw new InvalidOperationException("Неизвестная операция.")
        };
        var body = mutation.Body is { } json
            ? JsonSerializer.Deserialize<WorkloadInput>(json, Payload.Options)
            : null;
        using var response = await SendAsync(new HttpMethod(mutation.Method), path, session, body,
            mutation.RequestId);
    }

    private async Task<T> SendJsonAsync<T>(
        HttpMethod method, string path, Session? session, object? body = null)
    {
        using var response = await SendAsync(method, path, session, body);
        return await response.Content.ReadFromJsonAsync<T>(Payload.Options)
            ?? throw new InvalidOperationException("Сервер вернул пустой ответ.");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, Session? session, object? body, string? requestId = null)
    {
        return await retries.ExecuteAsync(async cancellationToken =>
        {
            using var request = new HttpRequestMessage(method, path);
            if (session is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            if (requestId is not null)
                request.Headers.Add("Idempotency-Key", requestId);
            if (body is not null)
                request.Content = JsonContent.Create(body, options: Payload.Options);

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.HttpRequestError == HttpRequestError.SecureConnectionError)
            {
                throw new ApiTransportException(
                    "Не удалось подтвердить сертификат сервера. Проверьте HTTPS и отпечаток.", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new ApiUnavailableException("Сервер недоступен. Проверьте подключение.", ex);
            }

            if (response.IsSuccessStatusCode)
                return response;

            using (response)
            {
                if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode >= 500)
                {
                    throw new ApiUnavailableException($"Сервер временно недоступен (HTTP {(int)response.StatusCode}).");
                }

                Problem? problem = null;
                try
                {
                    problem = await response.Content.ReadFromJsonAsync<Problem>(
                        Payload.Options, cancellationToken);
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException) { }
                var fallback = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Проверьте имя, пароль или срок сеанса.",
                    HttpStatusCode.Forbidden => "Для операции недостаточно прав.",
                    HttpStatusCode.Conflict => "Запись изменилась или такое назначение уже существует.",
                    _ => $"Запрос отклонён (HTTP {(int)response.StatusCode})."
                };
                throw new ApiRejectedException(response.StatusCode, problem?.Detail ?? problem?.Title ?? fallback);
            }
        });
    }

    public void Dispose() => http.Dispose();
}

internal sealed class ApiUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

internal sealed class ApiTransportException(string message, Exception? inner = null)
    : Exception(message, inner);

internal sealed class ApiRejectedException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}
