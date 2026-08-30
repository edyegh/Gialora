// Gialora.Data/Entities/FamilyMember.cs
namespace Gialora.Data.Entities;

public class FamilyMember : BaseEntity
{
    public Guid FamilyId { get; set; }
    public Family Family { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public int? Age { get; set; }

    // JSON-ով պահվող dietary restrictions (օր. ["vegetarian", "gluten-free"])
    public List<string> DietaryRestrictions { get; set; } = new();
    public List<string> Allergies { get; set; } = new();
}