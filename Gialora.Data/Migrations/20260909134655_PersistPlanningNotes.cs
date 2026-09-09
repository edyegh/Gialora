using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gialora.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistPlanningNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlanningNotes",
                table: "MealPlans",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlanningNotes",
                table: "MealPlans");
        }
    }
}
