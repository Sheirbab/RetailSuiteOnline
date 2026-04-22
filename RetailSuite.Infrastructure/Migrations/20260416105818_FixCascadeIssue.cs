using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailSuite.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixCascadeIssue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_InventoryItems_InventoryItemId",
                table: "InventoryTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_JournalEntries_JournalEntryId",
                table: "JournalEntryLines");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_Orders_OrderId",
                table: "OrderItems");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Orders_OrderId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductAttributeValues_ProductAttributes_AttributeId",
                table: "ProductAttributeValues");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductCategories_Categories_CategoryId",
                table: "ProductCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductCategories_Products_ProductId",
                table: "ProductCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductCategories_Products_ProductId1",
                table: "ProductCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariants_Products_ProductId",
                table: "ProductVariants");

            migrationBuilder.DropForeignKey(
                name: "FK_VariantAttributeValues_ProductAttributeValues_ProductAttributeValueId",
                table: "VariantAttributeValues");

            migrationBuilder.DropForeignKey(
                name: "FK_VariantAttributeValues_ProductVariants_ProductVariantId",
                table: "VariantAttributeValues");

            migrationBuilder.AddColumn<decimal>(
                name: "AverageCost",
                table: "ProductVariants",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "StockQuantity",
                table: "ProductVariants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_InventoryItems_InventoryItemId",
                table: "InventoryTransactions",
                column: "InventoryItemId",
                principalTable: "InventoryItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_JournalEntries_JournalEntryId",
                table: "JournalEntryLines",
                column: "JournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_Orders_OrderId",
                table: "OrderItems",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Orders_OrderId",
                table: "Payments",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductAttributeValues_ProductAttributes_AttributeId",
                table: "ProductAttributeValues",
                column: "AttributeId",
                principalTable: "ProductAttributes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductCategories_Categories_CategoryId",
                table: "ProductCategories",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductCategories_Products_ProductId",
                table: "ProductCategories",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductCategories_Products_ProductId1",
                table: "ProductCategories",
                column: "ProductId1",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariants_Products_ProductId",
                table: "ProductVariants",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VariantAttributeValues_ProductAttributeValues_ProductAttributeValueId",
                table: "VariantAttributeValues",
                column: "ProductAttributeValueId",
                principalTable: "ProductAttributeValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VariantAttributeValues_ProductVariants_ProductVariantId",
                table: "VariantAttributeValues",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_InventoryItems_InventoryItemId",
                table: "InventoryTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_JournalEntries_JournalEntryId",
                table: "JournalEntryLines");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_Orders_OrderId",
                table: "OrderItems");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Orders_OrderId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductAttributeValues_ProductAttributes_AttributeId",
                table: "ProductAttributeValues");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductCategories_Categories_CategoryId",
                table: "ProductCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductCategories_Products_ProductId",
                table: "ProductCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductCategories_Products_ProductId1",
                table: "ProductCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariants_Products_ProductId",
                table: "ProductVariants");

            migrationBuilder.DropForeignKey(
                name: "FK_VariantAttributeValues_ProductAttributeValues_ProductAttributeValueId",
                table: "VariantAttributeValues");

            migrationBuilder.DropForeignKey(
                name: "FK_VariantAttributeValues_ProductVariants_ProductVariantId",
                table: "VariantAttributeValues");

            migrationBuilder.DropColumn(
                name: "AverageCost",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "StockQuantity",
                table: "ProductVariants");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_InventoryItems_InventoryItemId",
                table: "InventoryTransactions",
                column: "InventoryItemId",
                principalTable: "InventoryItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_JournalEntries_JournalEntryId",
                table: "JournalEntryLines",
                column: "JournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_Orders_OrderId",
                table: "OrderItems",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Orders_OrderId",
                table: "Payments",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductAttributeValues_ProductAttributes_AttributeId",
                table: "ProductAttributeValues",
                column: "AttributeId",
                principalTable: "ProductAttributes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductCategories_Categories_CategoryId",
                table: "ProductCategories",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductCategories_Products_ProductId",
                table: "ProductCategories",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductCategories_Products_ProductId1",
                table: "ProductCategories",
                column: "ProductId1",
                principalTable: "Products",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariants_Products_ProductId",
                table: "ProductVariants",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VariantAttributeValues_ProductAttributeValues_ProductAttributeValueId",
                table: "VariantAttributeValues",
                column: "ProductAttributeValueId",
                principalTable: "ProductAttributeValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VariantAttributeValues_ProductVariants_ProductVariantId",
                table: "VariantAttributeValues",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
