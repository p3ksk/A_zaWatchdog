using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlzaWatchdog.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Users",
                type: "TEXT",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailConfirmToken",
                table: "Users",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "EmailConfirmedAt",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "NotifiedThroughSnapshotId",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_Users_EmailConfirmToken",
                table: "Users",
                column: "EmailConfirmToken");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_EmailConfirmToken",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailConfirmToken",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailConfirmedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotifiedThroughSnapshotId",
                table: "Users");
        }
    }
}
