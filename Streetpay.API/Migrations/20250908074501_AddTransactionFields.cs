using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Streetpay.API.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Transactions",
                type: "TEXT",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ReceiverId",
                table: "Transactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SenderId",
                table: "Transactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Signature",
                table: "Transactions",
                type: "TEXT",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Transactions",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Used",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ReceiverId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SenderId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Signature",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Used",
                table: "Transactions");
        }
    }
}
