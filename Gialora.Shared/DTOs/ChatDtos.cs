// Gialora.Shared/DTOs/ChatDtos.cs
using System.ComponentModel.DataAnnotations;
using Gialora.Shared.Enums;

namespace Gialora.Shared.Dtos;

/// <summary>Աջակցության chat-ի մեկ հաղորդագրություն՝ user-ի կամ admin-ի (Telegram-ից)։</summary>
public class ChatMessageDto
{
    public Guid Id { get; set; }
    public ChatSender Sender { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; }
}

public class ChatSendDto
{
    [Required, StringLength(1000, MinimumLength = 1)]
    public string Text { get; set; } = string.Empty;
}
