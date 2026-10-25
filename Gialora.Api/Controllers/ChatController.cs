// Gialora.Api/Controllers/ChatController.cs
using Gialora.Api.Extensions;
using Gialora.Application.Services;
using Gialora.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Gialora.Api.Controllers;

/// <summary>
/// Աջակցության chat-ը։ Բաց է հյուրերի համար էլ. մուտք գործածը ճանաչվում է JWT-ով,
/// հյուրը՝ X-Chat-Key header-ով (browser-ում պահված գաղտնի բանալի)։
/// Admin-ը պատասխանում է Telegram-ից (TelegramPollingService)։
/// </summary>
[ApiController]
[Route("api/chat")]
[AllowAnonymous]
public class ChatController : ControllerBase
{
    public const string GuestKeyHeader = "X-Chat-Key";

    private readonly IChatService _chat;

    public ChatController(IChatService chat) => _chat = chat;

    [HttpGet("messages")]
    public async Task<ActionResult<List<ChatMessageDto>>> GetMessages() =>
        Ok(await _chat.GetMessagesAsync(Caller()));

    [HttpPost("messages")]
    [EnableRateLimiting("chat")]
    public async Task<ActionResult<ChatMessageDto>> Send([FromBody] ChatSendDto dto, CancellationToken cancellationToken) =>
        Ok(await _chat.SendAsync(Caller(), dto, cancellationToken));

    private ChatCaller Caller() =>
        new(User.GetUserId(), Request.Headers[GuestKeyHeader].FirstOrDefault());
}
