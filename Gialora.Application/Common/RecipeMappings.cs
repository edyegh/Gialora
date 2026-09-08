// Gialora.Application/Common/RecipeMappings.cs
using System.Linq.Expressions;
using Gialora.Data.Entities;
using Gialora.Shared.Dtos;

namespace Gialora.Application.Common;

/// <summary>
/// Recipe → DTO projection-ները մեկ տեղում։ Երեք service (Recipe, MealPlan, Favorite)
/// նույն ձևաչափն է վերադարձնում, ուստի պատճենելու փոխարեն կիսում ենք նույն expression-ը —
/// այդպես նոր դաշտ ավելացնելիս ոչ մի endpoint հետ չի մնում։
/// </summary>
public static class RecipeMappings
{
    public static Expression<Func<Recipe, RecipeSummaryDto>> SummaryProjection(Guid? currentUserId) =>
        r => new RecipeSummaryDto
        {
            Id = r.Id,
            Title = r.Title,
            Slug = r.Slug,
            Description = r.Description,
            ImageUrl = r.ImageUrl,
            MealType = r.MealType,
            Cuisine = r.Cuisine,
            Difficulty = r.Difficulty,
            DietType = r.DietType,
            PrimaryProtein = r.PrimaryProtein,
            PrepTimeMinutes = r.PrepTimeMinutes,
            CookTimeMinutes = r.CookTimeMinutes,
            Servings = r.Servings,
            MinAgeMonths = r.MinAgeMonths,
            IsFreezerFriendly = r.IsFreezerFriendly,
            IsLunchboxFriendly = r.IsLunchboxFriendly,
            IsKidFriendly = r.IsKidFriendly,
            IsHighProtein = r.IsHighProtein,
            IsIronRich = r.IsIronRich,
            IsBatchFriendly = r.IsBatchFriendly,
            IsPublished = r.IsPublished,
            AverageRating = r.Feedbacks.Any(f => f.Rating > 0)
                ? r.Feedbacks.Where(f => f.Rating > 0).Average(f => (double)f.Rating)
                : 0d,
            RatingCount = r.Feedbacks.Count(f => f.Rating > 0),
            IsFavorite = currentUserId != null && r.Favorites.Any(fav => fav.UserId == currentUserId),
            Tags = r.RecipeTags.Select(rt => rt.Tag.Name).ToList()
        };

    public static Expression<Func<Recipe, RecipeDetailDto>> DetailProjection(Guid? currentUserId) =>
        r => new RecipeDetailDto
        {
            Id = r.Id,
            Title = r.Title,
            Slug = r.Slug,
            Description = r.Description,
            Instructions = r.Instructions,
            ImageUrl = r.ImageUrl,
            MealType = r.MealType,
            Cuisine = r.Cuisine,
            Difficulty = r.Difficulty,
            DietType = r.DietType,
            PrimaryProtein = r.PrimaryProtein,
            PrepTimeMinutes = r.PrepTimeMinutes,
            CookTimeMinutes = r.CookTimeMinutes,
            Servings = r.Servings,
            MinAgeMonths = r.MinAgeMonths,
            IsFreezerFriendly = r.IsFreezerFriendly,
            IsLunchboxFriendly = r.IsLunchboxFriendly,
            IsKidFriendly = r.IsKidFriendly,
            IsHighProtein = r.IsHighProtein,
            IsIronRich = r.IsIronRich,
            IsBatchFriendly = r.IsBatchFriendly,
            IsPublished = r.IsPublished,
            EstimatedCostPerServing = r.EstimatedCostPerServing,
            AverageRating = r.Feedbacks.Any(f => f.Rating > 0)
                ? r.Feedbacks.Where(f => f.Rating > 0).Average(f => (double)f.Rating)
                : 0d,
            RatingCount = r.Feedbacks.Count(f => f.Rating > 0),
            IsFavorite = currentUserId != null && r.Favorites.Any(fav => fav.UserId == currentUserId),
            Tags = r.RecipeTags.Select(rt => rt.Tag.Name).ToList(),
            Ingredients = r.RecipeIngredients
                .OrderBy(ri => ri.SortOrder)
                .Select(ri => new RecipeIngredientDto
                {
                    IngredientId = ri.IngredientId,
                    IngredientName = ri.Ingredient.Name,
                    Quantity = ri.Quantity,
                    Unit = ri.Unit,
                    Category = ri.Ingredient.Category,
                    Note = ri.Note,
                    IsOptional = ri.IsOptional,
                    SortOrder = ri.SortOrder
                })
                .ToList()
        };

    /// <summary>
    /// Instructions-ը մեկ text է. Steps-ի բաժանումը SQL-ում հնարավոր չէ, ուստի անում ենք
    /// materialize-ից հետո։ Աջակցում ենք և՛ "1. ..." համարակալված, և՛ պարզ տողերի ձևաչափը։
    /// </summary>
    public static RecipeDetailDto WithSteps(this RecipeDetailDto dto)
    {
        dto.Steps = SplitSteps(dto.Instructions);
        return dto;
    }

    public static List<string> SplitSteps(string? instructions)
    {
        if (string.IsNullOrWhiteSpace(instructions))
            return new List<string>();

        return instructions
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(StripLeadingNumber)
            .Where(s => s.Length > 0)
            .ToList();
    }

    private static string StripLeadingNumber(string line)
    {
        var index = 0;
        while (index < line.Length && char.IsDigit(line[index]))
            index++;

        // "1. Preheat" / "2) Chop" → "Preheat" / "Chop". "2 eggs" չենք կտրում։
        if (index > 0 && index < line.Length && (line[index] == '.' || line[index] == ')'))
            return line[(index + 1)..].Trim();

        return line.Trim();
    }
}
