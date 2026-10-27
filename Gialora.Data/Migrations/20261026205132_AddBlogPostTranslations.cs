using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gialora.Data.Migrations
{
    /// <summary>
    /// Blog-ի գրառումների Title/Excerpt/Content-ը տեղափոխում է BlogPostTranslations
    /// (մեկ տող ամեն լեզվի համար)։ Եղած տեքստը դառնում է "en" թարգմանությունը։
    ///
    /// Ինչպես AddContentTranslations-ում, EF-ի գեներացրածը սյուները ջնջում էր ԱՌԱՋ —
    /// հիմա նախ աղյուսակ, հետո պատճեն, հետո միայն սյուների ջնջում։
    /// </summary>
    public partial class AddBlogPostTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlogPostTranslations",
                columns: table => new
                {
                    BlogPostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Excerpt = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlogPostTranslations", x => new { x.BlogPostId, x.Language });
                    table.ForeignKey(
                        name: "FK_BlogPostTranslations_BlogPosts_BlogPostId",
                        column: x => x.BlogPostId,
                        principalTable: "BlogPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Բոլոր գրառումները, ջնջվածներն էլ — թարգմանության տողը ծնողի հետ է ապրում
            migrationBuilder.Sql(@"
INSERT INTO [BlogPostTranslations] ([BlogPostId], [Language], [Title], [Excerpt], [Content])
SELECT [Id], N'en', [Title], [Excerpt], [Content] FROM [BlogPosts];
");

            migrationBuilder.DropColumn(
                name: "Content",
                table: "BlogPosts");

            migrationBuilder.DropColumn(
                name: "Excerpt",
                table: "BlogPosts");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "BlogPosts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Հին սխեման մեկ լեզու ունի. վերադարձնում ենք անգլերենը (չլինելու դեպքում՝
            // առկա առաջին լեզուն), մյուս թարգմանությունները կորչում են։
            migrationBuilder.AddColumn<string>(
                name: "Content",
                table: "BlogPosts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Excerpt",
                table: "BlogPosts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "BlogPosts",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
UPDATE p SET p.[Title] = t.[Title], p.[Excerpt] = t.[Excerpt], p.[Content] = t.[Content]
FROM [BlogPosts] p
CROSS APPLY (SELECT TOP 1 x.[Title], x.[Excerpt], x.[Content] FROM [BlogPostTranslations] x
             WHERE x.[BlogPostId] = p.[Id]
             ORDER BY CASE WHEN x.[Language] = N'en' THEN 0 ELSE 1 END, x.[Language]) t;
");

            migrationBuilder.DropTable(
                name: "BlogPostTranslations");
        }
    }
}
