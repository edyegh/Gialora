// Gialora.Tests/UnitConverterTests.cs
using Gialora.Application.Common;
using Gialora.Shared.Enums;

namespace Gialora.Tests;

/// <summary>
/// The arithmetic behind the shopping list. A mistake here is silent — the list
/// still looks plausible, it is just wrong.
/// </summary>
public class UnitConverterTests
{
    [Fact]
    public void Chicken_across_three_recipes_consolidates_to_kilograms()
    {
        // The worked example from the brief: 500 g + 750 g + 450 g.
        var total = UnitConverter.ToBase(500m, UnitOfMeasure.Gram)
                  + UnitConverter.ToBase(750m, UnitOfMeasure.Gram)
                  + UnitConverter.ToBase(450m, UnitOfMeasure.Gram);

        var (quantity, unit) = UnitConverter.FromBase(total, UnitOfMeasure.Gram);

        Assert.Equal(1.7m, quantity);
        Assert.Equal(UnitOfMeasure.Kilogram, unit);
        Assert.Equal("1.7 kg", UnitConverter.Format(quantity, unit));
    }

    [Fact]
    public void Grams_and_kilograms_sum_together()
    {
        var total = UnitConverter.ToBase(1m, UnitOfMeasure.Kilogram)
                  + UnitConverter.ToBase(500m, UnitOfMeasure.Gram);

        var (quantity, unit) = UnitConverter.FromBase(total, UnitOfMeasure.Kilogram);

        Assert.Equal(1.5m, quantity);
        Assert.Equal(UnitOfMeasure.Kilogram, unit);
    }

    [Fact]
    public void Spoon_measures_come_back_as_spoons_not_millilitres()
    {
        // Regression: the list used to read "Salt - 7.5 ml", which nobody parses
        // as "a teaspoon and a half".
        var total = UnitConverter.ToBase(1.5m, UnitOfMeasure.Teaspoon);
        var (quantity, unit) = UnitConverter.FromBase(total, UnitOfMeasure.Teaspoon);

        Assert.Equal(UnitOfMeasure.Teaspoon, unit);
        Assert.Equal("1.5 tsp", UnitConverter.Format(quantity, unit));
    }

    [Fact]
    public void Spoons_still_accumulate_into_larger_spoon_units()
    {
        // Olive oil, 2 tbsp in each of five recipes.
        var total = Enumerable.Range(0, 5)
            .Sum(_ => UnitConverter.ToBase(2m, UnitOfMeasure.Tablespoon));

        var (quantity, unit) = UnitConverter.FromBase(total, UnitOfMeasure.Tablespoon);

        Assert.Equal(10m, quantity);
        Assert.Equal(UnitOfMeasure.Tablespoon, unit);
    }

    [Fact]
    public void Millilitres_roll_up_into_litres()
    {
        var total = UnitConverter.ToBase(1200m, UnitOfMeasure.Milliliter)
                  + UnitConverter.ToBase(700m, UnitOfMeasure.Milliliter);

        var (quantity, unit) = UnitConverter.FromBase(total, UnitOfMeasure.Milliliter);

        Assert.Equal(1.9m, quantity);
        Assert.Equal(UnitOfMeasure.Liter, unit);
    }

    [Fact]
    public void Cloves_and_grams_of_garlic_never_get_added_together()
    {
        // Different families must not merge, or the total is meaningless.
        Assert.NotEqual(
            UnitConverter.ConsolidationKey(UnitOfMeasure.Clove),
            UnitConverter.ConsolidationKey(UnitOfMeasure.Gram));

        // Whereas anything in the mass family shares a bucket.
        Assert.Equal(
            UnitConverter.ConsolidationKey(UnitOfMeasure.Gram),
            UnitConverter.ConsolidationKey(UnitOfMeasure.Kilogram));
    }

    [Fact]
    public void Pieces_of_different_things_stay_in_their_own_buckets()
    {
        Assert.NotEqual(
            UnitConverter.ConsolidationKey(UnitOfMeasure.Piece),
            UnitConverter.ConsolidationKey(UnitOfMeasure.Can));
    }

    [Theory]
    [InlineData(4, 6, 750)]   // 500 g scaled to six servings
    [InlineData(4, 2, 250)]
    [InlineData(4, 4, 500)]   // unchanged
    public void Mass_scales_proportionally(int from, int to, decimal expected)
    {
        var scaled = UnitConverter.Scale(500m, UnitOfMeasure.Gram, from, to);
        Assert.Equal(expected, scaled);
    }

    [Fact]
    public void Whole_units_round_up_because_you_cannot_buy_half_an_egg()
    {
        // 1 egg for four servings, scaled to six, is 1.5 - which must become 2.
        var scaled = UnitConverter.Scale(1m, UnitOfMeasure.Piece, fromServings: 4, toServings: 6);
        Assert.Equal(2m, scaled);
    }

    [Fact]
    public void Scaling_is_a_no_op_when_serving_counts_are_missing()
    {
        Assert.Equal(500m, UnitConverter.Scale(500m, UnitOfMeasure.Gram, 0, 6));
        Assert.Equal(500m, UnitConverter.Scale(500m, UnitOfMeasure.Gram, 4, 0));
    }

    [Fact]
    public void A_single_piece_is_formatted_without_a_unit()
    {
        // "2" reads better than "2 pc" next to an ingredient name.
        Assert.Equal("2", UnitConverter.Format(2m, UnitOfMeasure.Piece));
        Assert.Equal("250 g", UnitConverter.Format(250m, UnitOfMeasure.Gram));
        Assert.Equal("1 bunch", UnitConverter.Format(1m, UnitOfMeasure.Bunch));
    }
}
