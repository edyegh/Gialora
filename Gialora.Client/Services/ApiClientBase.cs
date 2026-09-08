// Gialora.Client/Services/ApiClientBase.cs
using System.Net;
using System.Net.Http.Json;
using Gialora.Shared.Dtos;

namespace Gialora.Client.Services;

/// <summary>
/// API-ի սխալը՝ արդեն մարդու համար ընթեռնելի տեսքով։ Ամեն էջ նույն ձևով է
/// սա բռնում, ուստի "Something went wrong"-ի փոխարեն user-ը տեսնում է
/// սերվերի իրական բացատրությունը ("No other recipe matches your preferences...")։
/// </summary>
public class ApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public Dictionary<string, string[]>? ValidationErrors { get; }

    public ApiException(HttpStatusCode statusCode, string message, Dictionary<string, string[]>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        ValidationErrors = errors;
    }

    public bool IsUnauthorized => StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}

public abstract class ApiClientBase
{
    protected readonly HttpClient Http;

    protected ApiClientBase(HttpClient http)
    {
        Http = http;
    }

    protected async Task<T> GetAsync<T>(string url)
    {
        var response = await Http.GetAsync(url);
        await EnsureSuccessAsync(response);
        return await ReadAsync<T>(response);
    }

    /// <summary>404-ը այստեղ սխալ չէ — "դեռ չկա" նորմալ վիճակ է (օր. ընթացիկ պլան)։</summary>
    protected async Task<T?> GetOrDefaultAsync<T>(string url)
    {
        var response = await Http.GetAsync(url);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
            return default;

        await EnsureSuccessAsync(response);
        return await ReadAsync<T>(response);
    }

    protected async Task<TResult> PostAsync<TResult>(string url, object? body = null)
    {
        var response = await SendJsonAsync(HttpMethod.Post, url, body);
        await EnsureSuccessAsync(response);
        return await ReadAsync<TResult>(response);
    }

    protected async Task PostAsync(string url, object? body = null)
    {
        var response = await SendJsonAsync(HttpMethod.Post, url, body);
        await EnsureSuccessAsync(response);
    }

    protected async Task<TResult> PutAsync<TResult>(string url, object? body = null)
    {
        var response = await SendJsonAsync(HttpMethod.Put, url, body);
        await EnsureSuccessAsync(response);
        return await ReadAsync<TResult>(response);
    }

    protected async Task PutAsync(string url, object? body = null)
    {
        var response = await SendJsonAsync(HttpMethod.Put, url, body);
        await EnsureSuccessAsync(response);
    }

    protected async Task DeleteAsync(string url)
    {
        var response = await Http.DeleteAsync(url);
        await EnsureSuccessAsync(response);
    }

    private async Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string url, object? body)
    {
        var request = new HttpRequestMessage(method, url);

        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: GialoraJson.Options);

        return await Http.SendAsync(request);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        // 204 No Content — մարմին չկա, բայց caller-ը տիպ է սպասում
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            return default!;

        return await response.Content.ReadFromJsonAsync<T>(GialoraJson.Options) ?? default!;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Please sign in to continue.",
            HttpStatusCode.Forbidden => "You do not have access to this.",
            HttpStatusCode.TooManyRequests => "Too many attempts. Please wait a moment and try again.",
            _ => "Something went wrong. Please try again."
        };

        Dictionary<string, string[]>? errors = null;

        // API-ն ամեն սխալ վերադարձնում է ApiErrorDto-ով, բայց 500-ի կամ proxy-ի
        // դեպքում մարմինը կարող է HTML լինել — այդ պատճառով try/catch։
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<ApiErrorDto>(GialoraJson.Options);

            if (!string.IsNullOrWhiteSpace(payload?.Message))
                message = payload.Message;

            errors = payload?.Errors;
        }
        catch
        {
            // Մնում ենք status-ի հիման վրա կառուցված message-ի վրա
        }

        throw new ApiException(response.StatusCode, message, errors);
    }
}
