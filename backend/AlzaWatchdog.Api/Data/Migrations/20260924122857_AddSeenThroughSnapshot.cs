using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlzaWatchdog.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeenThroughSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SeenThroughSnapshotId",
                table: "Users",
                type: "INTEGER",
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
