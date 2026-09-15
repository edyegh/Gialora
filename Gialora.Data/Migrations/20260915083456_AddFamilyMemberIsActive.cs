using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gialora.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyMemberIsActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "FamilyMembers",
                type: "bit",
                nullable: false,
                // Գոյություն ունեցող անդամները պիտի ակտիվ մնան — հակառակ դեպքում migration-ը
                // բոլորի ընտանիքը կդատարկեր պլանավորիչի աչքում։
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "FamilyMembers");
        }
    }
}
