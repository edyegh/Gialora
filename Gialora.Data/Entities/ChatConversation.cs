// Gialora.Data/Entities/ChatConversation.cs
namespace Gialora.Data.Entities;

/// <summary>
/// Մեկ այցելուի աջակցության chat-ը։ Մուտք գործած user-ը ճանաչվում է UserId-ով,
/// հյուրը՝ իր browser-ում պահված գաղտնի բանալու hash-ով (բանալին ինքը երբեք
/// չի պահվում — DB-ի արտահոսքը չպիտի թույլ տա ուրիշի խոսակցությունը կարդալ)։
/// Ճիշտ մեկն է լրացված։
/// </summary>
public class ChatConversation : BaseEntity
{
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>SHA-256(hex) հյուրի բանալուց։</summary>
    public string? GuestKeyHash { get; set; }

    public DateTime LastMessageAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
