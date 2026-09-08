// Gialora.Shared/DTOs/ShoppingListDtos.cs
using System.ComponentModel.DataAnnotations;
using Gialora.Shared.Enums;

namespace Gialora.Shared.Dtos;

public class ShoppingListDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ShoppingListStatus Status { get; set; }
    public Guid? MealPlanId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Խմբավորված ըստ խանութի բաժնի — այդպես ենք գնումներ անում, ոչ թե ըստ ռեցեպտի։</summary>
    public List<ShoppingListGroupDto> Groups { get; set; } = new();

    public int TotalItems => Groups.Sum(g => g.Items.Count);
    public int CheckedItems => Groups.Sum(g => g.Items.Count(i => i.IsChecked));
}

public class ShoppingListGroupDto
{
    public IngredientCategory Category { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public List<ShoppingListItemDto> Items { get; set; } = new();
}

public class ShoppingListItemDto
{
    public Guid Id { get; set; }
    public Guid? IngredientId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public UnitOfMeasure Unit { get; set; }

    /// <summary>Արդեն ձևաչափված՝ "1.7 kg", "2 pieces" — client-ը կլորացում չի անում։</summary>
    public string QuantityDisplay { get; set; } = string.Empty;

    public IngredientCategory Category { get; set; }
    public bool IsChecked { get; set; }
    public bool IsOptional { get; set; }

    /// <summary>
    /// Աղ, ձեթ, պղպեղ — ամենայն հավանականությամբ արդեն տանն են։ UI-ն սրանք
    /// առանձնացնում է "check your cupboard"-ի տակ, որ ցանկը իրական գնումների
    /// ցանկ մնա, ոչ թե բոլոր բաղադրիչների ցուցակ։
    /// </summary>
    public bool IsPantryStaple { get; set; }

    /// <summary>Որ ռեցեպտներից է եկել այս տողը (consolidation-ի հետքը)։</summary>
    public List<string> SourceRecipes { get; set; } = new();
}

public class ShoppingListItemCreateDto
{
    [Required, StringLength(150, MinimumLength = 1)]
    public string DisplayName { get; set; } = string.Empty;

    [Range(0.001, 100000)]
    public decimal Quantity { get; set; } = 1;

    public UnitOfMeasure Unit { get; set; } = UnitOfMeasure.Piece;
    public IngredientCategory Category { get; set; } = IngredientCategory.Other;
}

public class ShoppingListItemCheckDto
{
    public bool IsChecked { get; set; }
}
