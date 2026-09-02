// Gialora.Shared/Dtos/FamilyDtos.cs
using System.ComponentModel.DataAnnotations;

namespace Gialora.Shared.Dtos;

public class FamilyMemberDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? Age { get; set; }
    public List<string> DietaryRestrictions { get; set; } = new();
    public List<string> Allergies { get; set; } = new();
}

public class FamilyDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<FamilyMemberDto> Members { get; set; } = new();
}

public class FamilyMemberCreateDto
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 120)]
    public int? Age { get; set; }

    public List<string> DietaryRestrictions { get; set; } = new();
    public List<string> Allergies { get; set; } = new();
}

public class FamilyMemberUpdateDto : FamilyMemberCreateDto
{
    // Հիմա նույն դաշտերն են, ինչ Create-ի — ապագայում կարող են տարբերվել
}