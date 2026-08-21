using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingCQBLaser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingIndexesAndOverlapConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Bookings_PaymentStatus_CreatedAt",
                table: "Bookings",
                columns: new[] { "PaymentStatus", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_StartTime_PaymentStatus",
                table: "Bookings",
                columns: new[] { "StartTime", "PaymentStatus" });

            // Final line of defense against two concurrent requests both passing the
            // application-level availability check for overlapping times. PaymentStatus 2 =
            // Failed: a failed/expired booking no longer occupies real arena time. Postgres range
            // types have built-in GiST operator support, so no extension is required for a single
            // range column exclusion like this.
            migrationBuilder.Sql(@"
                ALTER TABLE ""Bookings""
                ADD CONSTRAINT ""EX_Bookings_NoOverlap""
                EXCLUDE USING gist (
                    tstzrange(""StartTime"", ""EndTime"", '[)') WITH &&
                ) WHERE (""PaymentStatus"" <> 2);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""Bookings"" DROP CONSTRAINT ""EX_Bookings_NoOverlap"";");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_PaymentStatus_CreatedAt",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_StartTime_PaymentStatus",
                table: "Bookings");
        }
    }
}
