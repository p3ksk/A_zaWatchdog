#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace AlzaWatchdog.Api.Data.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddSeenThroughSnapshotMySql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SeenThroughSnapshotId",
                table: "Users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Existing accounts start with nothing new. Without this, the bell would
            // open on every change recorded since each account was created.
            migrationBuilder.Sql(
                "UPDATE Users SET SeenThroughSnapshotId = COALESCE((SELECT MAX(Id) FROM PriceSnapshots), 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SeenThroughSnapshotId",
                table: "Users");
        }
    }
}
