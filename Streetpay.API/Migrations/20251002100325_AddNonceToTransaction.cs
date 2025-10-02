using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Streetpay.API.Migrations
{
    /// <inheritdoc />
    public partial class AddNonceToTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Nonce",
                table: "Transactions",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Nonce",
                table: "Transactions");
        }
    }
}
