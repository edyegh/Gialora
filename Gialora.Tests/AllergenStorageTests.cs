// Gialora.Tests/AllergenStorageTests.cs
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Gialora.Tests;

/// <summary>
/// Allergens are stored as pipe-delimited enum NAMES ("Milk|TreeNuts"), like the
/// project's other enum columns, so reordering the enum can never silently turn a
/// stored "Sesame" into something else. Read through the EF model's converter.
/// </summary>
public class AllergenStorageTests
{
    private static readonly Microsoft.EntityFrameworkCore.Metadata.IModel Model =
        new GialoraDbContext(new DbContextOptionsBuilder<GialoraDbContext>()
            .UseSqlServer("Server=unused;Database=unused")
            .Options).Model;

    private static Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter Converter<T>(string property) =>
        Model.FindEntityType(typeof(T))!.FindProperty(property)!.GetValueConverter()!;

    [Fact]
    public void Ingredient_allergens_are_stored_by_name()
    {
        var stored = Converter<Ingredient>(nameof(Ingredient.Allergens))
            .ConvertToProvider(new List<Allergen> { Allergen.TreeNuts, Allergen.Milk, Allergen.Milk });

        Assert.Equal("Milk|TreeNuts", stored);
    }

    [Fact]
    public void Unknown_or_numeric_values_are_dropped_on_read()
    {
        var read = (List<Allergen>)Converter<FamilyMember>(nameof(FamilyMember.Allergens))
            .ConvertFromProvider("Sesame|NotAnAllergen|99| Peanuts ")!;

        Assert.Equal(new[] { Allergen.Sesame, Allergen.Peanuts }, read);
    }
}
