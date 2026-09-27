#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace AlzaWatchdog.Api.Data.Migrations.MySql
{
    /// <inheritdoc />
    public partial class AddEmailNotificationsMySql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MariaDB DDL is not transactional: if a run dies midway, the columns it
            // already added stay while the history row is never written, and the
            // startup retry loop then fails forever on "Duplicate column name".
            // IF NOT EXISTS lets a re-run finish the job from any partial state.
            migrationBuilder.Sql("ALTER TABLE `Users` ADD COLUMN IF NOT EXISTS `Email` varchar(320) CHARACTER SET utf8mb4 NULL;");
            migrationBuilder.Sql("ALTER TABLE `Users` ADD COLUMN IF NOT EXISTS `EmailConfirmToken` varchar(64) CHARACTER SET utf8mb4 NULL;");
            migrationBuilder.Sql("ALTER TABLE `Users` ADD COLUMN IF NOT EXISTS `EmailConfirmedAt` bigint NULL;");
            migrationBuilder.Sql("ALTER TABLE `Users` ADD COLUMN IF NOT EXISTS `NotifiedThroughSnapshotId` bigint NOT NULL DEFAULT 0;");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_Users_EmailConfirmToken` ON `Users` (`EmailConfirmToken`);");
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
