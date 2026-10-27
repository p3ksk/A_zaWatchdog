#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace AlzaWatchdog.Api.Data.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddProductPausedByAdminMySql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PausedByAdmin",
                table: "Products",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PausedByAdmin",
                table: "Products");
        }
    }
}
