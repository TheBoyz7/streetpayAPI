using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Streetpay.API.Migrations
{
    /// <inheritdoc />
    public partial class AddNewBalanceFieldsToTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ReceiverNewBalance",
                table: "Transactions",
                type: "DECIMAL(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SenderNewBalance",
                table: "Transactions",
                type: "DECIMAL(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceiverNewBalance",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SenderNewBalance",
                table: "Transactions");
        }
    }
}
