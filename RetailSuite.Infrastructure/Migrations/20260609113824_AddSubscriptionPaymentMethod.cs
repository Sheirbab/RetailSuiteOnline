using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailSuite.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPaymentMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CardBrand",
                table: "TenantSubscriptions",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CardExpMonth",
                table: "TenantSubscriptions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CardExpYear",
                table: "TenantSubscriptions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardHolderName",
                table: "TenantSubscriptions",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardLast4",
                table: "TenantSubscriptions",
                type: "nvarchar(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GatewayCustomerId",
                table: "TenantSubscriptions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethodType",
                table: "TenantSubscriptions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardBrand",
                table: "TenantSubscriptions");

            migrationBuilder.DropColumn(
                name: "CardExpMonth",
                table: "TenantSubscriptions");

            migrationBuilder.DropColumn(
                name: "CardExpYear",
                table: "TenantSubscriptions");

            migrationBuilder.DropColumn(
                name: "CardHolderName",
                table: "TenantSubscriptions");

            migrationBuilder.DropColumn(
                name: "CardLast4",
                table: "TenantSubscriptions");

            migrationBuilder.DropColumn(
                name: "GatewayCustomerId",
                table: "TenantSubscriptions");

            migrationBuilder.DropColumn(
                name: "PaymentMethodType",
                table: "TenantSubscriptions");
        }
    }
}
