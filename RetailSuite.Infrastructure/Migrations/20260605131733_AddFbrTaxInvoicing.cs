using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailSuite.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFbrTaxInvoicing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FbrInvoiceNumber",
                table: "Orders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvoiceIssuedAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                table: "Orders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerAddressSnapshot",
                table: "Orders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerBusinessNameSnapshot",
                table: "Orders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerNtnSnapshot",
                table: "Orders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerStrnSnapshot",
                table: "Orders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaxSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ntn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Strn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BusinessNameAsRegistered = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RegisteredAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    InvoicePrefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FiscalYearStartMonth = table.Column<int>(type: "int", nullable: false),
                    PricesIncludeTax = table.Column<bool>(type: "bit", nullable: false),
                    DefaultTaxRate = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    FbrEnabled = table.Column<bool>(type: "bit", nullable: false),
                    FbrPosId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FbrStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_InvoiceNumber",
                table: "Orders",
                columns: new[] { "TenantId", "InvoiceNumber" },
                unique: true,
                filter: "[InvoiceNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaxSettings_TenantId",
                table: "TaxSettings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaxSettings");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TenantId_InvoiceNumber",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FbrInvoiceNumber",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InvoiceIssuedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SellerAddressSnapshot",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SellerBusinessNameSnapshot",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SellerNtnSnapshot",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SellerStrnSnapshot",
                table: "Orders");
        }
    }
}
