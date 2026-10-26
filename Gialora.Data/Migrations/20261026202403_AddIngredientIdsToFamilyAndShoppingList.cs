using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gialora.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIngredientIdsToFamilyAndShoppingList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceRecipeIds",
                table: "ShoppingListItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DislikedIngredientIds",
                table: "Families",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceRecipeIds",
                table: "ShoppingListItems");

            migrationBuilder.DropColumn(
                name: "DislikedIngredientIds",
                table: "Families");
        }
    }
}
