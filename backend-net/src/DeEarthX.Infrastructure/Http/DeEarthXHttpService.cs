using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeEarthX.Core;

namespace DeEarthX.Infrastructure.Http;

public interface IDeEarthXHttpService
{
    Task<string> GetStringAsync(string url, CancellationToken ct = default);

    Task<T> GetJsonAsync<T>(string url, CancellationToken ct = default);

    Task<Stream> GetStreamAsync(string url, CancellationToken ct = default);

    Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct = default);

    Task<T> PostJsonAsync<T>(string url, object body, CancellationToken ct = default);

    Task<T> PostJsonWithAuthAsync<T>(string url, object body, string scheme, string token, CancellationToken ct = default);
}

public sealed class DeEarthXHttpService : IDeEarthXHttpService
{
    public const string UserAgent = "DeEarthX";
    private const int MaxRetries = 2;

    private readonly HttpClient _httpClient;

    public DeEarthXHttpService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        Configure(_httpClient);
    }

    public static void Configure(HttpClient client)
    {
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }
        client.Timeout = TimeSpan.FromMinutes(2);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        return WithRetryAsync(() => _httpClient.GetStringAsync(url, ct), ct);
    }

    public async Task<T> GetJsonAsync<T>(string url, CancellationToken ct = default)
    {
        return await WithRetryAsync(async () =>
        {
            await using var stream = await _httpClient.GetStreamAsync(url, ct).ConfigureAwait(false);
            return (await JsonSerializer.DeserializeAsync<T>(stream, DeEarthXJsonOptions.Default, ct).ConfigureAwait(false))!;
        }, ct);
    }

    public Task<Stream> GetStreamAsync(string url, CancellationToken ct = default)
    {
        return WithRetryAsync(() => _httpClient.GetStreamAsync(url, ct), ct);
    }

    public Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct = default)
    {
        return WithRetryAsync(() => _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct), ct);
    }

    public async Task<T> PostJsonAsync<T>(string url, object body, CancellationToken ct = default)
    {
        return await WithRetryAsync(async () =>
        {
            using var content = new StringContent(JsonSerializer.Serialize(body, DeEarthXJsonOptions.Default), Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return (await JsonSerializer.DeserializeAsync<T>(stream, DeEarthXJsonOptions.Default, ct).ConfigureAwait(false))!;
        }, ct);
    }

    public async Task<T> PostJsonWithAuthAsync<T>(string url, object body, string scheme, string token, CancellationToken ct = default)
    {
        return await WithRetryAsync(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue(scheme, token);
            request.Content = new StringContent(JsonSerializer.Serialize(body, DeEarthXJsonOptions.Default), Encoding.UTF8);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return (await JsonSerializer.DeserializeAsync<T>(stream, DeEarthXJsonOptions.Default, ct).ConfigureAwait(false))!;
        }, ct);
    }

    private static async Task<T> WithRetryAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (HttpRequestException) when (attempt < MaxRetries)
            {
                var delay = TimeSpan.FromMilliseconds(500 * (attempt + 1));
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (TimeoutException) when (attempt < MaxRetries)
            {
                var delay = TimeSpan.FromMilliseconds(500 * (attempt + 1));
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }
}
