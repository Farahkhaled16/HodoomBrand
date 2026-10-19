using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HodoomBrand.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymobTransactionId",
                table: "Orders",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymobTransactionId",
                table: "Orders");
        }
    }
}
