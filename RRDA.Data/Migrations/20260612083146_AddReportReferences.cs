using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RRDA.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReportReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportReferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceReportFileId = table.Column<int>(type: "int", nullable: false),
                    SourceReportEntityId = table.Column<int>(type: "int", nullable: true),
                    TargetReportFileId = table.Column<int>(type: "int", nullable: true),
                    TargetReportTypeId = table.Column<int>(type: "int", nullable: true),
                    TargetKeyField = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TargetKeyValue = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Origin = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportReferences", x => x.Id);
                    table.CheckConstraint("CK_ReportReferences_ImportedShape", "[Origin] <> 0 OR ([SourceReportEntityId] IS NOT NULL AND [TargetReportTypeId] IS NOT NULL AND [TargetReportFileId] IS NULL AND [TargetKeyField] IS NOT NULL)");
                    table.CheckConstraint("CK_ReportReferences_ManualShape", "[Origin] <> 1 OR ([SourceReportEntityId] IS NULL AND [TargetReportFileId] IS NOT NULL AND [TargetReportTypeId] IS NULL AND [TargetKeyField] IS NULL AND [TargetKeyValue] IS NULL)");
                    table.ForeignKey(
                        name: "FK_ReportReferences_ReportEntities_SourceReportEntityId",
                        column: x => x.SourceReportEntityId,
                        principalTable: "ReportEntities",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReportReferences_ReportFiles_SourceReportFileId",
                        column: x => x.SourceReportFileId,
                        principalTable: "ReportFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReportReferences_ReportFiles_TargetReportFileId",
                        column: x => x.TargetReportFileId,
                        principalTable: "ReportFiles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReportReferences_ReportTypes_TargetReportTypeId",
                        column: x => x.TargetReportTypeId,
                        principalTable: "ReportTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportReferences_SourceReportEntityId",
                table: "ReportReferences",
                column: "SourceReportEntityId",
                unique: true,
                filter: "[SourceReportEntityId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReportReferences_SourceReportFileId",
                table: "ReportReferences",
                column: "SourceReportFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportReferences_SourceReportFileId_TargetReportFileId",
                table: "ReportReferences",
                columns: new[] { "SourceReportFileId", "TargetReportFileId" },
                unique: true,
                filter: "[TargetReportFileId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReportReferences_TargetReportFileId",
                table: "ReportReferences",
                column: "TargetReportFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportReferences_TargetReportTypeId_TargetKeyValue",
                table: "ReportReferences",
                columns: new[] { "TargetReportTypeId", "TargetKeyValue" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportReferences");
        }
    }
}
