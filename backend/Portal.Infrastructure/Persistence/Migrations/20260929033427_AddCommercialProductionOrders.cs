using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommercialProductionOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommercialProductionOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Consecutive = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CommercialOwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastUpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionOwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerOrderNumber = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    DeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ClientName = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: true),
                    ProductName = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: true),
                    ReferenceNumber = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    ClientPurchaseOrder = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    UnitValue = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    CityCountry = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: true),
                    Address = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    WorkType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    PrintColorProof = table.Column<bool>(type: "boolean", nullable: false),
                    DieType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    OpenSize = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    ClosedSize = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    Observations = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    AdditionalSpecifications = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    ReceptionContact = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: true),
                    DeliveryAddress = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    ReceptionSchedule = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: true),
                    PartialDelivery = table.Column<bool>(type: "boolean", nullable: false),
                    PartialDeliveryQuantity = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    LegalContractRequirements = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    DispatchDay = table.Column<DateOnly>(type: "date", nullable: true),
                    QualityCertificateMode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    TechnicalSheetMode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    PlanningDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PlanningManager = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: true),
                    MaterialCutDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CuttingManager = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: true),
                    PrintStartShift1 = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    PrintingManagerShift1 = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: true),
                    PrintStartShift2 = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    PrintingManagerShift2 = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: true),
                    FinishingStart = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    FinishingManager = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: true),
                    DieCutStart = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    DieCutManager = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: true),
                    DieMachine = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    DieNumber = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    DieTotalProcessed = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    DieConforming = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    DieNonConforming = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    GluingStart = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    GluingManager = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: true),
                    GlueType = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    GlueTotalProcessed = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    GlueConforming = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    GlueNonConforming = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    QualityReviewDate = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    QualityReviewer = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: true),
                    QualityApproved = table.Column<bool>(type: "boolean", nullable: true),
                    QualityNotes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ProductionReceivedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialProductionOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommercialProductionOrders_AspNetUsers_CommercialOwnerUserId",
                        column: x => x.CommercialOwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialProductionOrders_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialProductionOrders_AspNetUsers_LastUpdatedByUserId",
                        column: x => x.LastUpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialProductionOrders_AspNetUsers_ProductionOwnerUserId",
                        column: x => x.ProductionOwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CommercialProductionOrders_CommercialProductionOrders_Sourc~",
                        column: x => x.SourceOrderId,
                        principalTable: "CommercialProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CommercialProductionOrderFinishes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Specification = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    Front = table.Column<bool>(type: "boolean", nullable: false),
                    Back = table.Column<bool>(type: "boolean", nullable: false),
                    Reserve = table.Column<bool>(type: "boolean", nullable: false),
                    TotalProcessed = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    ConformingQuantity = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    NonConformingQuantity = table.Column<decimal>(type: "numeric(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialProductionOrderFinishes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommercialProductionOrderFinishes_CommercialProductionOrder~",
                        column: x => x.ProductionOrderId,
                        principalTable: "CommercialProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommercialProductionOrderMaterials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Material = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: true),
                    Weight = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                    Caliber = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                    OptionalSpecifications = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    SheetSize = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    SheetQuantity = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    CutSize = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    FractionPerSheet = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    FitPerFraction = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    TotalCutQuantity = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    ConformingQuantity = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    NonConformingQuantity = table.Column<decimal>(type: "numeric(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialProductionOrderMaterials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommercialProductionOrderMaterials_CommercialProductionOrde~",
                        column: x => x.ProductionOrderId,
                        principalTable: "CommercialProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommercialProductionOrderPrintLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Product = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: true),
                    Inks = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    Process = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    Specials = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    Machine = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: true),
                    Mounting = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: true),
                    ShotsToProcess = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    ConformingQuantity = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    NonConformingQuantity = table.Column<decimal>(type: "numeric(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialProductionOrderPrintLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommercialProductionOrderPrintLines_CommercialProductionOrd~",
                        column: x => x.ProductionOrderId,
                        principalTable: "CommercialProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommercialProductionOrderStatusHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialProductionOrderStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommercialProductionOrderStatusHistory_AspNetUsers_ActorUse~",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialProductionOrderStatusHistory_CommercialProduction~",
                        column: x => x.ProductionOrderId,
                        principalTable: "CommercialProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrderFinishes_ProductionOrderId_Position",
                table: "CommercialProductionOrderFinishes",
                columns: new[] { "ProductionOrderId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrderMaterials_ProductionOrderId_Positi~",
                table: "CommercialProductionOrderMaterials",
                columns: new[] { "ProductionOrderId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrderPrintLines_ProductionOrderId_Posit~",
                table: "CommercialProductionOrderPrintLines",
                columns: new[] { "ProductionOrderId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrders_ClientName",
                table: "CommercialProductionOrders",
                column: "ClientName");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrders_CommercialOwnerUserId",
                table: "CommercialProductionOrders",
                column: "CommercialOwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrders_Consecutive",
                table: "CommercialProductionOrders",
                column: "Consecutive",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrders_CreatedAt",
                table: "CommercialProductionOrders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrders_CreatedByUserId",
                table: "CommercialProductionOrders",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrders_CustomerOrderNumber",
                table: "CommercialProductionOrders",
                column: "CustomerOrderNumber");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrders_LastUpdatedByUserId",
                table: "CommercialProductionOrders",
                column: "LastUpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrders_ProductionOwnerUserId",
                table: "CommercialProductionOrders",
                column: "ProductionOwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrders_SourceOrderId",
                table: "CommercialProductionOrders",
                column: "SourceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrders_Status",
                table: "CommercialProductionOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrderStatusHistory_ActorUserId",
                table: "CommercialProductionOrderStatusHistory",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrderStatusHistory_ProductionOrderId_Oc~",
                table: "CommercialProductionOrderStatusHistory",
                columns: new[] { "ProductionOrderId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommercialProductionOrderFinishes");

            migrationBuilder.DropTable(
                name: "CommercialProductionOrderMaterials");

            migrationBuilder.DropTable(
                name: "CommercialProductionOrderPrintLines");

            migrationBuilder.DropTable(
                name: "CommercialProductionOrderStatusHistory");

            migrationBuilder.DropTable(
                name: "CommercialProductionOrders");
        }
    }
}
