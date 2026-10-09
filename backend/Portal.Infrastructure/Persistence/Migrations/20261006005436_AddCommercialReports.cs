using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommercialReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommercialReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    DataJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastExportedVersion = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommercialReportSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalBytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialReportSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommercialReportSources_CommercialReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "CommercialReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommercialOpReportRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderVersion = table.Column<int>(type: "integer", nullable: true),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRow = table.Column<int>(type: "integer", nullable: true),
                    Number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Client = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Product = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DataJson = table.Column<string>(type: "text", nullable: false),
                    CapturedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialOpReportRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommercialOpReportRecords_CommercialReportSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "CommercialReportSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpReportRecords_Number",
                table: "CommercialOpReportRecords",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpReportRecords_ProductionOrderId_OrderVersion",
                table: "CommercialOpReportRecords",
                columns: new[] { "ProductionOrderId", "OrderVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialOpReportRecords_SourceId_SourceRow",
                table: "CommercialOpReportRecords",
                columns: new[] { "SourceId", "SourceRow" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialReports_OwnerUserId_UpdatedAt",
                table: "CommercialReports",
                columns: new[] { "OwnerUserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialReportSources_Kind_Sha256",
                table: "CommercialReportSources",
                columns: new[] { "Kind", "Sha256" },
                unique: true,
                filter: "\"Kind\" = 'ops'");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialReportSources_OwnerUserId_Kind_Sha256",
                table: "CommercialReportSources",
                columns: new[] { "OwnerUserId", "Kind", "Sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialReportSources_ReportId_Sha256",
                table: "CommercialReportSources",
                columns: new[] { "ReportId", "Sha256" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommercialOpReportRecords");

            migrationBuilder.DropTable(
                name: "CommercialReportSources");

            migrationBuilder.DropTable(
                name: "CommercialReports");
        }
    }
}
