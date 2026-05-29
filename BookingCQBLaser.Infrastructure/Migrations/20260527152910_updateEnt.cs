using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingCQBLaser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class updateEnt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgeRange",
                table: "Bookings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAdultGroup",
                table: "Bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgeRange",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "IsAdultGroup",
                table: "Bookings");
        }
    }
}
