using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlzaWatchdog.Api.Data.Migrations.MySql
{
    /// <inheritdoc />
    public partial class NullDiscontinuedZeroPricesMySql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // alza.sk publishes a product it no longer sells with price 0 and an empty
            // currency. The parser now reads that as "no price"; this clears the
            // readings stored before it did. Prices are stored as text, so the zero
            // is matched by its spellings rather than by a numeric cast.
            foreach (var column in new[] { "Price", "PlusPrice", "CouponPrice" })
                migrationBuilder.Sql($"UPDATE PriceSnapshots SET {column} = NULL WHERE {column} IN ('0', '0.0', '0.00');");

            foreach (var column in new[] { "LastPrice", "LastPlusPrice", "LastCouponPrice" })
                migrationBuilder.Sql($"UPDATE Products SET {column} = NULL WHERE {column} IN ('0', '0.0', '0.00');");

            migrationBuilder.Sql("UPDATE Products SET Currency = NULL WHERE Currency = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only: the zeros were wrong, so there is nothing worth restoring.
        }
    }
}
