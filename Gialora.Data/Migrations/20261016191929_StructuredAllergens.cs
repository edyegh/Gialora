using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gialora.Data.Migrations
{
    /// <summary>
    /// Ազատ տեքստի ալերգիաներից և 7 bool flag-ից → ԵՄ-ի 14 ալերգենների ցանկ։
    ///
    /// ՁԵՌՔՈՎ ԳՐՎԱԾ — EF-ի scaffold-ը ContainsSoy-ը վերանվանում էր AllergensReviewed
    /// (սոյա պարունակողը կդառնար "ստուգված") և flag-երը ջնջում էր առանց պատճենելու։
    /// Հերթականությունը՝ նոր սյունակներ → տվյալների փոխանցում → հին սյունակների ջնջում։
    ///
    /// Բոլոր եղած բաղադրիչները մնում են AllergensReviewed = 0. հին flag-երը 14-ից միայն
    /// 7-ն էին ծածկում (celery, mustard, sesame, sulphites, lupin չէին ստուգվել), և
    /// "nuts"-ը չէր տարբերում peanuts / tree nuts — admin-ը պիտի մեկ անգամ վերանայի։
    /// Հին "nuts"-ը դառնում է Peanuts + TreeNuts, "shellfish"-ը՝ Crustaceans + Molluscs
    /// (կասկածի դեպքում՝ ավելի խիստը)։
    /// </summary>
    public partial class StructuredAllergens : Migration
    {
        // Անդամների ազատ տեքստի միանգամյա փոխանցումը։ Բառը (փոքրատառ, "-"/"_" → բացատ,
        // " allergy"/" free"/" intolerance" կտրված) համեմատվում է ամբողջությամբ։
        // Չճանաչվածը մնում է OtherAllergies-ում (անունով ստուգում) — ոչինչ չի կորչում։
        private const string LegacyAllergyMap = """
            (VALUES
                ('gluten','Gluten'),('wheat','Gluten'),('celiac','Gluten'),('coeliac','Gluten'),('barley','Gluten'),('rye','Gluten'),('spelt','Gluten'),
                ('dairy','Milk'),('milk','Milk'),('lactose','Milk'),('casein','Milk'),('whey','Milk'),
                ('egg','Eggs'),('eggs','Eggs'),
                ('fish','Fish'),
                ('seafood','Fish'),('seafood','Crustaceans'),('seafood','Molluscs'),
                ('shellfish','Crustaceans'),('shellfish','Molluscs'),
                ('crustacean','Crustaceans'),('crustaceans','Crustaceans'),('shrimp','Crustaceans'),('shrimps','Crustaceans'),
                ('prawn','Crustaceans'),('prawns','Crustaceans'),('crab','Crustaceans'),('lobster','Crustaceans'),
                ('mollusc','Molluscs'),('molluscs','Molluscs'),('mollusk','Molluscs'),('mollusks','Molluscs'),
                ('mussel','Molluscs'),('mussels','Molluscs'),('clam','Molluscs'),('clams','Molluscs'),
                ('oyster','Molluscs'),('oysters','Molluscs'),('squid','Molluscs'),('octopus','Molluscs'),
                ('nut','Peanuts'),('nut','TreeNuts'),('nuts','Peanuts'),('nuts','TreeNuts'),
                ('peanut','Peanuts'),('peanuts','Peanuts'),
                ('tree nut','TreeNuts'),('tree nuts','TreeNuts'),('almond','TreeNuts'),('almonds','TreeNuts'),
                ('walnut','TreeNuts'),('walnuts','TreeNuts'),('hazelnut','TreeNuts'),('hazelnuts','TreeNuts'),
                ('cashew','TreeNuts'),('cashews','TreeNuts'),('pistachio','TreeNuts'),('pistachios','TreeNuts'),
                ('pecan','TreeNuts'),('pecans','TreeNuts'),
                ('soy','Soy'),('soya','Soy'),('soybean','Soy'),('soybeans','Soy'),
                ('celery','Celery'),('mustard','Mustard'),('sesame','Sesame'),
                ('sulphite','Sulphites'),('sulphites','Sulphites'),('sulfite','Sulphites'),('sulfites','Sulphites'),
                ('lupin','Lupin'),('lupine','Lupin')
            ) AS Map(Phrase, Allergen)
            """;

        private const string NormalizedToken =
            "LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(s.value), '-', ' '), '_', ' '), " +
            "' allergies', ''), ' allergy', ''), ' intolerance', ''), ' free', '')))";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Նոր սյունակներ
            migrationBuilder.AddColumn<string>(
                name: "Allergens",
                table: "Ingredients",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "AllergensReviewed",
                table: "Ingredients",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Allergens",
                table: "FamilyMembers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            // 2. Բաղադրիչներ. flag-եր → անուններ
            migrationBuilder.Sql("""
                UPDATE Ingredients SET Allergens = ISNULL(STUFF(
                      CASE WHEN ContainsGluten    = 1 THEN '|Gluten' ELSE '' END
                    + CASE WHEN ContainsDairy     = 1 THEN '|Milk' ELSE '' END
                    + CASE WHEN ContainsNuts      = 1 THEN '|Peanuts|TreeNuts' ELSE '' END
                    + CASE WHEN ContainsEgg       = 1 THEN '|Eggs' ELSE '' END
                    + CASE WHEN ContainsFish      = 1 THEN '|Fish' ELSE '' END
                    + CASE WHEN ContainsShellfish = 1 THEN '|Crustaceans|Molluscs' ELSE '' END
                    + CASE WHEN ContainsSoy       = 1 THEN '|Soy' ELSE '' END,
                    1, 1, ''), '');
                """);

            // 3. Անդամներ. ազատ տեքստ → ճանաչվածը Allergens, մնացածը՝ OtherAllergies
            migrationBuilder.RenameColumn(
                name: "Allergies",
                table: "FamilyMembers",
                newName: "OtherAllergies");

            migrationBuilder.Sql($"""
                UPDATE fm SET
                    Allergens = ISNULL((
                        SELECT STRING_AGG(x.Allergen, '|') FROM (
                            SELECT DISTINCT Map.Allergen
                            FROM STRING_SPLIT(fm.OtherAllergies, '|') s
                            CROSS APPLY (SELECT {NormalizedToken} AS Token) t
                            JOIN {LegacyAllergyMap} ON Map.Phrase = t.Token
                        ) x), ''),
                    OtherAllergies = ISNULL((
                        SELECT STRING_AGG(s.value, '|')
                        FROM STRING_SPLIT(fm.OtherAllergies, '|') s
                        CROSS APPLY (SELECT {NormalizedToken} AS Token) t
                        WHERE s.value <> ''
                          AND NOT EXISTS (SELECT 1 FROM {LegacyAllergyMap} WHERE Map.Phrase = t.Token)
                        ), '')
                FROM FamilyMembers fm
                WHERE fm.OtherAllergies <> '';
                """);

            // 4. Հին flag-երը՝ միայն տվյալները փոխանցելուց ՀԵՏՈ
            foreach (var column in new[]
                     { "ContainsGluten", "ContainsDairy", "ContainsNuts", "ContainsEgg",
                       "ContainsFish", "ContainsShellfish", "ContainsSoy" })
            {
                migrationBuilder.DropColumn(name: column, table: "Ingredients");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Հակառակ ուղղությունը կորցնում է այն, ինչ հին մոդելը չուներ (celery, mustard,
            // sesame, sulphites, lupin, AllergensReviewed)։ Անդամների ալերգենները վերադառնում
            // են որպես ազատ տեքստ։
            foreach (var column in new[]
                     { "ContainsGluten", "ContainsDairy", "ContainsNuts", "ContainsEgg",
                       "ContainsFish", "ContainsShellfish", "ContainsSoy" })
            {
                migrationBuilder.AddColumn<bool>(
                    name: column, table: "Ingredients", type: "bit", nullable: false, defaultValue: false);
            }

            migrationBuilder.Sql("""
                UPDATE Ingredients SET
                    ContainsGluten    = CASE WHEN '|' + Allergens + '|' LIKE '%|Gluten|%' THEN 1 ELSE 0 END,
                    ContainsDairy     = CASE WHEN '|' + Allergens + '|' LIKE '%|Milk|%' THEN 1 ELSE 0 END,
                    ContainsNuts      = CASE WHEN '|' + Allergens + '|' LIKE '%|Peanuts|%'
                                               OR '|' + Allergens + '|' LIKE '%|TreeNuts|%' THEN 1 ELSE 0 END,
                    ContainsEgg       = CASE WHEN '|' + Allergens + '|' LIKE '%|Eggs|%' THEN 1 ELSE 0 END,
                    ContainsFish      = CASE WHEN '|' + Allergens + '|' LIKE '%|Fish|%' THEN 1 ELSE 0 END,
                    ContainsShellfish = CASE WHEN '|' + Allergens + '|' LIKE '%|Crustaceans|%'
                                               OR '|' + Allergens + '|' LIKE '%|Molluscs|%' THEN 1 ELSE 0 END,
                    ContainsSoy       = CASE WHEN '|' + Allergens + '|' LIKE '%|Soy|%' THEN 1 ELSE 0 END;
                """);

            migrationBuilder.Sql("""
                UPDATE FamilyMembers SET OtherAllergies =
                    CASE WHEN OtherAllergies = '' THEN LOWER(Allergens)
                         WHEN Allergens = '' THEN OtherAllergies
                         ELSE OtherAllergies + '|' + LOWER(Allergens) END
                WHERE Allergens <> '';
                """);

            migrationBuilder.RenameColumn(
                name: "OtherAllergies",
                table: "FamilyMembers",
                newName: "Allergies");

            migrationBuilder.DropColumn(name: "Allergens", table: "FamilyMembers");
            migrationBuilder.DropColumn(name: "AllergensReviewed", table: "Ingredients");
            migrationBuilder.DropColumn(name: "Allergens", table: "Ingredients");
        }
    }
}
