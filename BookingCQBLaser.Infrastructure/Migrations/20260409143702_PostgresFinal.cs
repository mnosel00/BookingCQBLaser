using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingCQBLaser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PostgresFinal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Customer_FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Customer_LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Customer_Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Customer_Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ParticipantsCount = table.Column<int>(type: "integer", nullable: false),
                    Package = table.Column<string>(type: "text", nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GoogleCalendarEventId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PaymentStatus = table.Column<int>(type: "integer", nullable: false)
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
