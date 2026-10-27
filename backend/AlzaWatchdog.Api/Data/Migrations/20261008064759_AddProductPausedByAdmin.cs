using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlzaWatchdog.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductPausedByAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PausedByAdmin",
                table: "Products",
                type: "INTEGER",
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
