using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailSuite.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModelWarningsFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ProductId1 was a shadow FK created by EF for the misconfigured
            // Product.Categories navigation. It exists on some local dev DBs
            // (whichever ran a pre-fix scaffold) but was never applied to the
            // Azure production DB. Guard the drop with IF EXISTS so the same
            // migration runs cleanly on both. Data loss risk was verified
            // separately (column is either absent or an all-null / mirror of ProductId).
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM sys.foreign_keys
                    WHERE name = 'FK_ProductCategories_Products_ProductId1'
                      AND parent_object_id = OBJECT_ID('ProductCategories'))
                BEGIN
                    ALTER TABLE ProductCategories DROP CONSTRAINT FK_ProductCategories_Products_ProductId1;
                END;

                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = 'IX_ProductCategories_ProductId1'
                      AND object_id = OBJECT_ID('ProductCategories'))
                BEGIN
                    DROP INDEX IX_ProductCategories_ProductId1 ON ProductCategories;
                END;

                IF EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE Name = N'ProductId1'
                      AND Object_ID = OBJECT_ID(N'ProductCategories'))
                BEGIN
                    ALTER TABLE ProductCategories DROP COLUMN ProductId1;
                END;
            ");

            migrationBuilder.AlterColumn<decimal>(
                name: "AverageCost",
                table: "ProductVariants",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "PaidAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "AverageCost",
                table: "ProductVariants",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "PaidAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldDefaultValue: 0m);

            // Re-add ProductId1 only if it does not already exist. Mirrors the
            // guarded drop in Up() so a down-migration is safe on databases
            // that never had this shadow column in the first place.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE Name = N'ProductId1'
                      AND Object_ID = OBJECT_ID(N'ProductCategories'))
                BEGIN
                    ALTER TABLE ProductCategories ADD ProductId1 uniqueidentifier NULL;
                END;

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = 'IX_ProductCategories_ProductId1'
                      AND object_id = OBJECT_ID('ProductCategories'))
                BEGIN
                    CREATE INDEX IX_ProductCategories_ProductId1 ON ProductCategories(ProductId1);
                END;

                IF NOT EXISTS (
                    SELECT 1 FROM sys.foreign_keys
                    WHERE name = 'FK_ProductCategories_Products_ProductId1'
                      AND parent_object_id = OBJECT_ID('ProductCategories'))
                BEGIN
                    ALTER TABLE ProductCategories
                        ADD CONSTRAINT FK_ProductCategories_Products_ProductId1
                        FOREIGN KEY (ProductId1) REFERENCES Products(Id) ON DELETE NO ACTION;
                END;
            ");
        }
    }
}
