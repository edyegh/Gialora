using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gialora.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EmailVerificationAttempts",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EmailVerificationCodeHash",
                table: "Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerificationExpiresUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerificationSentUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            // Մինչ այս migration-ը EmailConfirmed-ը ոչինչ չէր ստուգում և բոլորը false էին։
            // Հիմա login-ը պահանջում է հաստատում — առանց սրա արդեն գրանցված ԲՈԼՈՐ
            // user-ները կկողպվեին։ Գոյություն ունեցողներին համարում ենք հաստատված։
            migrationBuilder.Sql("UPDATE [Users] SET [EmailConfirmed] = 1 WHERE [EmailConfirmed] = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailVerificationAttempts",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailVerificationCodeHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailVerificationExpiresUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailVerificationSentUtc",
                table: "Users");
        }
    }
}
