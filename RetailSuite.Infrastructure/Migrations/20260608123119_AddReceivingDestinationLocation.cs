using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailSuite.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivingDestinationLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DestinationLocationId",
                table: "ReceivingOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingOrders_DestinationLocationId",
                table: "ReceivingOrders",
                column: "DestinationLocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReceivingOrders_Locations_DestinationLocationId",
                table: "ReceivingOrders",
                column: "DestinationLocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReceivingOrders_Locations_DestinationLocationId",
                table: "ReceivingOrders");

            migrationBuilder.DropIndex(
                name: "IX_ReceivingOrders_DestinationLocationId",
                table: "ReceivingOrders");

            migrationBuilder.DropColumn(
                name: "DestinationLocationId",
                table: "ReceivingOrders");
        }
    }
}
