// Gialora.Client/Services/ChatApi.cs
using System.Net.Http.Json;
using System.Security.Cryptography;
using Gialora.Client.Localization;
using Gialora.Shared.Dtos;
using Microsoft.JSInterop;

namespace Gialora.Client.Services;

/// <summary>
/// Աջակցության chat-ը։ Ամեն request-ի հետ ուղարկում ենք X-Chat-Key-ը — հյուրի
/// խոսակցությունը կապված է այս browser-ում պահված գաղտնի բանալուն։ Մուտք գործած
/// user-ին server-ը ճանաչում է JWT-ով (AuthHeaderHandler), բանալին այդ դեպքում անտեսվում է։
/// </summary>
public class ChatApi : ApiClientBase
{
    private const string KeyStorageKey = "gialoraChatKey";
    private const string KeyHeader = "X-Chat-Key";

    private readonly IJSRuntime _js;
    private string? _key;

    public ChatApi(HttpClient http, Localizer localizer, IJSRuntime js) : base(http, localizer)
    {
        _js = js;
    }

    public async Task<List<ChatMessageDto>> GetMessagesAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/chat/messages");
        request.Headers.Add(KeyHeader, await GetKeyAsync());

        using var response = await Http.SendAsync(request);
        await EnsureSuccessAsync(response);
        return await ReadAsync<List<ChatMessageDto>>(response) ?? new();
    }

    public async Task<ChatMessageDto> SendAsync(string text)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/chat/messages")
        {
            Content = JsonContent.Create(new ChatSendDto { Text = text }, options: GialoraJson.Options)
        };
        request.Headers.Add(KeyHeader, await GetKeyAsync());

        using var response = await Http.SendAsync(request);
        await EnsureSuccessAsync(response);
        return await ReadAsync<ChatMessageDto>(response);
    }

    /// <summary>
    /// 32 պատահական բայթ (base64url, 43 նիշ)։ Private mode-ում localStorage-ը կարող է
    /// չաշխատել — այդ դեպքում բանալին ապրում է միայն այս tab-ի համար։
    /// </summary>
    private async Task<string> GetKeyAsync()
    {
        if (_key is not null)
            return _key;

        try
        {
            _key = await _js.InvokeAsync<string?>("localStorage.getItem", KeyStorageKey);
        }
        catch (JSException)
        {
            // storage-ը արգելափակված է
        }

        if (string.IsNullOrEmpty(_key))
        {
            _key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

            try
            {
                await _js.InvokeVoidAsync("localStorage.setItem", KeyStorageKey, _key);
            }
            catch (JSException)
            {
            }
        }

        return _key;
    }
}
