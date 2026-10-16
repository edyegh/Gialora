// Gialora.Tests/SoftDeleteIndexTests.cs
using Gialora.Data;
using Gialora.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gialora.Tests;

/// <summary>
/// Every "does it already exist?" check goes through the !IsDeleted query filter.
/// If the matching unique index also counted soft-deleted rows, re-creating a
/// deleted recipe title / ingredient / tag / blog slug, or regenerating a deleted
/// shopping list, would pass the check and then fail the INSERT with a 500.
/// These assert on the EF model (no database needed).
/// </summary>
public class SoftDeleteIndexTests
{
    private static readonly Microsoft.EntityFrameworkCore.Metadata.IModel Model =
        new GialoraDbContext(new DbContextOptionsBuilder<GialoraDbContext>()
            .UseSqlServer("Server=unused;Database=unused")
            .Options).Model;

    [Theory]
    [InlineData(typeof(Recipe), nameof(Recipe.Slug))]
    [InlineData(typeof(Ingredient), nameof(Ingredient.Name))]
    [InlineData(typeof(Tag), nameof(Tag.Name))]
    [InlineData(typeof(BlogPost), nameof(BlogPost.Slug))]
    [InlineData(typeof(ShoppingList), nameof(ShoppingList.MealPlanId))]
    public void Unique_indexes_ignore_soft_deleted_rows(Type entity, string property)
    {
        var index = Assert.Single(
            Model.FindEntityType(entity)!.GetIndexes(),
            i => i.IsUnique && i.Properties.Count == 1 && i.Properties[0].Name == property);

        Assert.Contains("[IsDeleted] = 0", index.GetFilter());
    }
}
