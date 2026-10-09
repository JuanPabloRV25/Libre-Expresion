using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommercialQuotationReviewAndDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentAssigneeUserId",
                table: "CommercialProductionOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DesignApplicability",
                table: "CommercialProductionOrders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<string>(
                name: "PurchaseOrderApplicability",
                table: "CommercialProductionOrders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<string>(
                name: "QuotationNumber",
                table: "CommercialProductionOrders",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewOwnerUserId",
                table: "CommercialProductionOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewReturnedAt",
                table: "CommercialProductionOrders",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewSubmittedAt",
                table: "CommercialProductionOrders",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommercialNotificationOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialNotificationOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommercialNotificationOutbox_AspNetUsers_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialNotificationOutbox_CommercialProductionOrders_Pro~",
                        column: x => x.ProductionOrderId,
                        principalTable: "CommercialProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommercialProductionOrderDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Applicability = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    SupersededAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialProductionOrderDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommercialProductionOrderDocuments_AspNetUsers_UploadedByUs~",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialProductionOrderDocuments_CommercialProductionOrde~",
                        column: x => x.ProductionOrderId,
                        principalTable: "CommercialProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommercialProductionOrderImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ImporterVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ExtractedDataJson = table.Column<string>(type: "jsonb", nullable: false),
                    WarningsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ImportedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialProductionOrderImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommercialProductionOrderImports_AspNetUsers_ImportedByUser~",
                        column: x => x.ImportedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialProductionOrderImports_CommercialProductionOrderD~",
                        column: x => x.SourceDocumentId,
                        principalTable: "CommercialProductionOrderDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialProductionOrderImports_CommercialProductionOrders~",
                        column: x => x.ProductionOrderId,
                        principalTable: "CommercialProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrders_CurrentAssigneeUserId",
                table: "CommercialProductionOrders",
                column: "CurrentAssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrders_ReviewOwnerUserId",
                table: "CommercialProductionOrders",
                column: "ReviewOwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialNotificationOutbox_ProductionOrderId",
                table: "CommercialNotificationOutbox",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialNotificationOutbox_RecipientUserId",
                table: "CommercialNotificationOutbox",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialNotificationOutbox_Status_NextAttemptAt",
                table: "CommercialNotificationOutbox",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrderDocuments_ProductionOrderId_Type_I~",
                table: "CommercialProductionOrderDocuments",
                columns: new[] { "ProductionOrderId", "Type", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrderDocuments_StorageKey",
                table: "CommercialProductionOrderDocuments",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrderDocuments_UploadedByUserId",
                table: "CommercialProductionOrderDocuments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrderImports_ImportedByUserId",
                table: "CommercialProductionOrderImports",
                column: "ImportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrderImports_ProductionOrderId",
                table: "CommercialProductionOrderImports",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialProductionOrderImports_SourceDocumentId",
                table: "CommercialProductionOrderImports",
                column: "SourceDocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_CommercialProductionOrders_AspNetUsers_CurrentAssigneeUserId",
                table: "CommercialProductionOrders",
                column: "CurrentAssigneeUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_CommercialProductionOrders_AspNetUsers_ReviewOwnerUserId",
                table: "CommercialProductionOrders",
                column: "ReviewOwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CommercialProductionOrders_AspNetUsers_CurrentAssigneeUserId",
                table: "CommercialProductionOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_CommercialProductionOrders_AspNetUsers_ReviewOwnerUserId",
                table: "CommercialProductionOrders");

            migrationBuilder.DropTable(
                name: "CommercialNotificationOutbox");

            migrationBuilder.DropTable(
                name: "CommercialProductionOrderImports");

            migrationBuilder.DropTable(
                name: "CommercialProductionOrderDocuments");

            migrationBuilder.DropIndex(
                name: "IX_CommercialProductionOrders_CurrentAssigneeUserId",
                table: "CommercialProductionOrders");

            migrationBuilder.DropIndex(
                name: "IX_CommercialProductionOrders_ReviewOwnerUserId",
                table: "CommercialProductionOrders");

            migrationBuilder.DropColumn(
                name: "CurrentAssigneeUserId",
                table: "CommercialProductionOrders");

            migrationBuilder.DropColumn(
                name: "DesignApplicability",
                table: "CommercialProductionOrders");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderApplicability",
                table: "CommercialProductionOrders");

            migrationBuilder.DropColumn(
                name: "QuotationNumber",
                table: "CommercialProductionOrders");

            migrationBuilder.DropColumn(
                name: "ReviewOwnerUserId",
                table: "CommercialProductionOrders");

            migrationBuilder.DropColumn(
                name: "ReviewReturnedAt",
                table: "CommercialProductionOrders");

            migrationBuilder.DropColumn(
                name: "ReviewSubmittedAt",
                table: "CommercialProductionOrders");
        }
    }
}
