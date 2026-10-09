using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommercialOperationGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OperationGroupId",
                table: "CommercialProductionOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "CommercialProductionOrders"
                SET "OperationGroupId" = "Id"
                WHERE "OperationGroupId" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "OperationGroupId",
                table: "CommercialProductionOrders",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrders_OperationGroupId",
                table: "CommercialProductionOrders",
                column: "OperationGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CommercialProductionOrders_OperationGroupId",
                table: "CommercialProductionOrders");

            migrationBuilder.DropColumn(
                name: "OperationGroupId",
                table: "CommercialProductionOrders");
        }
    }
}
