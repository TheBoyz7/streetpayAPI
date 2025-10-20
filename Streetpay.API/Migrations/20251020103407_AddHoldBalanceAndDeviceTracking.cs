using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Streetpay.API.Migrations
{
    /// <inheritdoc />
    public partial class AddHoldBalanceAndDeviceTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "HeldBalance",
                table: "Users",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "HoldExpiresAt",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastKnownDeviceId",
                table: "Users",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeldBalance",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "HoldExpiresAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastKnownDeviceId",
                table: "Users");
        }
    }
}
