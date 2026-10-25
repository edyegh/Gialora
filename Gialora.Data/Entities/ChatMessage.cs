// Gialora.Data/Entities/ChatMessage.cs
using Gialora.Shared.Enums;

namespace Gialora.Data.Entities;

public class ChatMessage : BaseEntity
{
    public Guid ConversationId { get; set; }
    public ChatConversation Conversation { get; set; } = null!;

    public ChatSender Sender { get; set; }

    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Telegram-ի message_id-ն admin-ի chat-ում։ User-ի հաղորդագրության համար՝ bot-ի
    /// ուղարկած պատճենի id-ն (admin-ը "Reply" է անում ՀԵՆՑ դրան, և այդպես ենք գտնում
    /// խոսակցությունը)։ Admin-ի պատասխանի համար՝ նրա Telegram հաղորդագրության id-ն,
    /// որ նույն update-ը երկու անգամ չմշակենք (restart-ից հետո Telegram-ը կրկնում է)։
    /// </summary>
    public long? TelegramMessageId { get; set; }
}
