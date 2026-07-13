using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailSuite.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerLocationInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryItems_TenantId_ProductVariantId",
                table: "InventoryItems");

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "InventoryTransactions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "InventoryItems",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_LocationId",
                table: "InventoryTransactions",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_LocationId",
                table: "InventoryItems",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_TenantId_ProductVariantId_LocationId",
                table: "InventoryItems",
                columns: new[] { "TenantId", "ProductVariantId", "LocationId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryItems_Locations_LocationId",
                table: "InventoryItems",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_Locations_LocationId",
                table: "InventoryTransactions",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryItems_Locations_LocationId",
                table: "InventoryItems");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_Locations_LocationId",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_LocationId",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryItems_LocationId",
                table: "InventoryItems");

            migrationBuilder.DropIndex(
                name: "IX_InventoryItems_TenantId_ProductVariantId_LocationId",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "InventoryItems");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_TenantId_ProductVariantId",
                table: "InventoryItems",
                columns: new[] { "TenantId", "ProductVariantId" },
                unique: true);
        }
    }
}
