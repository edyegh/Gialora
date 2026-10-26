using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gialora.Data.Migrations
{
    /// <summary>
    /// Ռեցեպտների, բաղադրիչների և tag-երի տեքստերը տեղափոխում է թարգմանությունների
    /// աղյուսակներ (մեկ տող ամեն լեզվի համար)։ Եղած տեքստը դառնում է "en" թարգմանությունը։
    ///
    /// ՀԵՐԹԱԿԱՆՈՒԹՅՈՒՆԸ ԿԱՐԵՎՈՐ Է. EF-ի գեներացրածը սյուները ջնջում էր ԱՌԱՋ — հիմա
    /// նախ ստեղծում ենք աղյուսակները, պատճենում տվյալը, հետո միայն ջնջում սյուները։
    /// </summary>
    public partial class AddContentTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Նոր աղյուսակներ
            migrationBuilder.CreateTable(
                name: "IngredientTranslations",
                columns: table => new
                {
                    IngredientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientTranslations", x => new { x.IngredientId, x.Language });
                    table.ForeignKey(
                        name: "FK_IngredientTranslations_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeTranslations",
                columns: table => new
                {
                    RecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Instructions = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeTranslations", x => new { x.RecipeId, x.Language });
                    table.ForeignKey(
                        name: "FK_RecipeTranslations_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TagTranslations",
                columns: table => new
                {
                    TagId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagTranslations", x => new { x.TagId, x.Language });
                    table.ForeignKey(
                        name: "FK_TagTranslations_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // 2. Եղած տեքստը → "en" թարգմանություն։
            // Ռեցեպտներն ու tag-երը՝ ԲՈԼՈՐԸ (ջնջվածներն էլ — seeder-ը ջնջված seed ռեցեպտը
            // վերնագրով է ճանաչում, որ restart-ին հետ չբերի)։ Բաղադրիչները՝ միայն չջնջվածները.
            // անունների unique index-ը ջնջված-չջնջված չի տարբերում, իսկ նախկին filtered
            // index-ը թույլ էր տալիս ջնջվածի և նորի նույն անունը։
            migrationBuilder.Sql(@"
INSERT INTO [RecipeTranslations] ([RecipeId], [Language], [Title], [Description], [Instructions])
SELECT [Id], N'en', [Title], [Description], [Instructions] FROM [Recipes];

INSERT INTO [IngredientTranslations] ([IngredientId], [Language], [Name])
SELECT [Id], N'en', [Name] FROM [Ingredients] WHERE [IsDeleted] = 0;

INSERT INTO [TagTranslations] ([TagId], [Language], [DisplayName])
SELECT [Id], N'en', [DisplayName] FROM [Tags];
");

            // 3. Հին սյուները
            migrationBuilder.DropIndex(
                name: "IX_Ingredients_Name",
                table: "Ingredients");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "Instructions",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Ingredients");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientTranslations_Language_Name",
                table: "IngredientTranslations",
                columns: new[] { "Language", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Հին սխեման ունի ՄԵԿ սյուն ամեն տեքստի համար. վերադարձնում ենք անգլերենը
            // (չլինելու դեպքում՝ առկա առաջին լեզուն), մյուս թարգմանությունները կորչում են։
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "Tags",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Recipes",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Instructions",
                table: "Recipes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Recipes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Ingredients",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
UPDATE r SET r.[Title] = t.[Title], r.[Description] = t.[Description], r.[Instructions] = t.[Instructions]
FROM [Recipes] r
CROSS APPLY (SELECT TOP 1 x.[Title], x.[Description], x.[Instructions] FROM [RecipeTranslations] x
             WHERE x.[RecipeId] = r.[Id]
             ORDER BY CASE WHEN x.[Language] = N'en' THEN 0 ELSE 1 END, x.[Language]) t;

UPDATE i SET i.[Name] = t.[Name]
FROM [Ingredients] i
CROSS APPLY (SELECT TOP 1 x.[Name] FROM [IngredientTranslations] x
             WHERE x.[IngredientId] = i.[Id]
             ORDER BY CASE WHEN x.[Language] = N'en' THEN 0 ELSE 1 END, x.[Language]) t;

UPDATE g SET g.[DisplayName] = t.[DisplayName]
FROM [Tags] g
CROSS APPLY (SELECT TOP 1 x.[DisplayName] FROM [TagTranslations] x
             WHERE x.[TagId] = g.[Id]
             ORDER BY CASE WHEN x.[Language] = N'en' THEN 0 ELSE 1 END, x.[Language]) t;
");

            migrationBuilder.DropTable(
                name: "IngredientTranslations");

            migrationBuilder.DropTable(
                name: "RecipeTranslations");

            migrationBuilder.DropTable(
                name: "TagTranslations");

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_Name",
                table: "Ingredients",
                column: "Name",
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
