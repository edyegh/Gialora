// Gialora.Application/Common/UnitConverter.cs
using Gialora.Shared.Enums;

namespace Gialora.Application.Common;

/// <summary>
/// Shopping list-ի consolidation-ի սիրտը (app structure §3)։
///
/// 500 g + 750 g + 450 g պիտի դառնա "1.7 kg", ոչ թե երեք առանձին տող։ Դրա համար
/// ամեն միավոր բերում ենք ընդհանուր base unit-ի (զանգված→գրամ, ծավալ→մլ, հատ→հատ),
/// գումարում ենք, հետո ցուցադրելիս նորից բարձրացնում ենք հարմար միավորի։
/// </summary>
public static class UnitConverter
{
    /// <summary>
    /// Չափման "ընտանիքը"։ Տարբեր ընտանիքների միավորները ՉԵՆ գումարվում իրար
    /// (2 clove garlic + 5 g garlic → երկու առանձին տող, ոչ թե սխալ գումար)։
    /// </summary>
    public enum UnitFamily
    {
        Mass,
        Volume,
        Count,
        Loose
    }

    public static UnitFamily FamilyOf(UnitOfMeasure unit) => unit switch
    {
        UnitOfMeasure.Gram or UnitOfMeasure.Kilogram => UnitFamily.Mass,
        UnitOfMeasure.Milliliter or UnitOfMeasure.Liter
            or UnitOfMeasure.Teaspoon or UnitOfMeasure.Tablespoon
            or UnitOfMeasure.Cup => UnitFamily.Volume,
        UnitOfMeasure.Piece or UnitOfMeasure.Clove or UnitOfMeasure.Slice
            or UnitOfMeasure.Can or UnitOfMeasure.Pack or UnitOfMeasure.Bunch => UnitFamily.Count,
        _ => UnitFamily.Loose // Pinch — "ըստ ճաշակի", չի գումարվում
    };

    /// <summary>Քանի՞ base միավոր է մեկ այս միավորը։</summary>
    public static decimal ToBaseFactor(UnitOfMeasure unit) => unit switch
    {
        UnitOfMeasure.Gram => 1m,
        UnitOfMeasure.Kilogram => 1000m,

        UnitOfMeasure.Milliliter => 1m,
        UnitOfMeasure.Liter => 1000m,
        UnitOfMeasure.Teaspoon => 5m,
        UnitOfMeasure.Tablespoon => 15m,
        UnitOfMeasure.Cup => 240m,

        _ => 1m // Count/Loose ընտանիքները իրենք են իրենց base-ը
    };

    /// <summary>
    /// Count/Loose ընտանիքում "clove"-ը և "piece"-ը նույն base-ը չունեն, ուստի
    /// consolidation-ի բանալին ընտանիք + (count/loose-ի դեպքում) կոնկրետ միավորն է։
    /// </summary>
    public static string ConsolidationKey(UnitOfMeasure unit)
    {
        var family = FamilyOf(unit);
        return family is UnitFamily.Mass or UnitFamily.Volume
            ? family.ToString()
            : $"{family}:{unit}";
    }

    public static decimal ToBase(decimal quantity, UnitOfMeasure unit) => quantity * ToBaseFactor(unit);

    /// <summary>
    /// Base քանակը վերադարձնում է մարդու համար ընթեռնելի միավորի.
    /// 1700 գ → (1.7, Kilogram), 250 գ → (250, Gram)։
    /// </summary>
    public static (decimal Quantity, UnitOfMeasure Unit) FromBase(decimal baseQuantity, UnitOfMeasure originalUnit)
    {
        switch (FamilyOf(originalUnit))
        {
            case UnitFamily.Mass:
                return baseQuantity >= 1000m
                    ? (Round(baseQuantity / 1000m), UnitOfMeasure.Kilogram)
                    : (Round(baseQuantity), UnitOfMeasure.Gram);

            case UnitFamily.Volume:
                // Գդալով չափվածը գդալով էլ վերադարձնում ենք։ Ամեն ինչ մլ-ի բերելը
                // ցանկում տալիս էր "Salt — 7.5 ml", ինչը ոչ ոք չի կարդում որպես
                // "մեկուկես թեյի գդալ"։ Գումարումը դեռ ընդհանուր base-ով է, ուստի
                // 5 ռեցեպտի ձիթապտղի ձեթը դեռ միանում է մեկ տողի մեջ։
                if (IsSpoonUnit(originalUnit))
                {
                    if (baseQuantity >= 240m) return (Round(baseQuantity / 240m), UnitOfMeasure.Cup);
                    if (baseQuantity >= 15m) return (Round(baseQuantity / 15m), UnitOfMeasure.Tablespoon);
                    return (Round(baseQuantity / 5m), UnitOfMeasure.Teaspoon);
                }

                return baseQuantity >= 1000m
                    ? (Round(baseQuantity / 1000m), UnitOfMeasure.Liter)
                    : (Round(baseQuantity), UnitOfMeasure.Milliliter);

            default:
                return (Round(baseQuantity), originalUnit);
        }
    }

    /// <summary>
    /// Servings-ի scaling-ը (app structure §3՝ 4 → 6 չափաբաժին)։
    /// Ամբողջական միավորները (ձու, հատ, բանկա) կլորացվում են ՎԵՐև — կես ձու չես գնում։
    /// </summary>
    public static decimal Scale(decimal quantity, UnitOfMeasure unit, int fromServings, int toServings)
    {
        if (fromServings <= 0 || toServings <= 0 || fromServings == toServings)
            return quantity;

        var scaled = quantity * toServings / fromServings;

        return IsWholeUnit(unit) ? Math.Ceiling(scaled) : Round(scaled);
    }

    public static bool IsSpoonUnit(UnitOfMeasure unit) => unit is
        UnitOfMeasure.Teaspoon or UnitOfMeasure.Tablespoon or UnitOfMeasure.Cup;

    /// <summary>Միավորներ, որոնք չեն կարող կոտորակային լինել գնումների ցանկում։</summary>
    public static bool IsWholeUnit(UnitOfMeasure unit) => unit is
        UnitOfMeasure.Piece or UnitOfMeasure.Can or UnitOfMeasure.Pack
        or UnitOfMeasure.Slice or UnitOfMeasure.Bunch or UnitOfMeasure.Clove;

    public static string Abbreviation(UnitOfMeasure unit) => unit switch
    {
        UnitOfMeasure.Gram => "g",
        UnitOfMeasure.Kilogram => "kg",
        UnitOfMeasure.Milliliter => "ml",
        UnitOfMeasure.Liter => "l",
        UnitOfMeasure.Piece => "pc",
        UnitOfMeasure.Teaspoon => "tsp",
        UnitOfMeasure.Tablespoon => "tbsp",
        UnitOfMeasure.Cup => "cup",
        UnitOfMeasure.Pinch => "pinch",
        UnitOfMeasure.Bunch => "bunch",
        UnitOfMeasure.Clove => "clove",
        UnitOfMeasure.Slice => "slice",
        UnitOfMeasure.Can => "can",
        UnitOfMeasure.Pack => "pack",
        _ => string.Empty
    };

    /// <summary>"1.7 kg", "2 pc", "1/2 tsp" — մեկ տեղում, որ UI-ն ամեն տեղ նույնը ցույց տա։</summary>
    public static string Format(decimal quantity, UnitOfMeasure unit)
    {
        var rounded = Round(quantity);
        var number = rounded == Math.Truncate(rounded)
            ? ((long)rounded).ToString()
            : rounded.ToString("0.##");

        var abbreviation = Abbreviation(unit);

        // "pc"-ը իմաստ չունի ցույց տալ երբ քանակը 1 է. "1 Egg" > "1 pc Egg"
        if (unit == UnitOfMeasure.Piece)
            return number;

        return string.IsNullOrEmpty(abbreviation) ? number : $"{number} {abbreviation}";
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
