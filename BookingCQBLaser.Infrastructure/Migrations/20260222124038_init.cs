using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingCQBLaser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Customer_FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Customer_LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Customer_Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Customer_Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ParticipantsCount = table.Column<int>(type: "int", nullable: false),
                    Package = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    GoogleCalendarEventId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bookings");
        }
    }
}
