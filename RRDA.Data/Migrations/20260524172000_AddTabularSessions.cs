using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RRDA.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTabularSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SubjectKind",
                table: "ReportTypes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "TabularSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportTypeId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FilterHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceWatermark = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TabularSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TabularSessions_ReportTypes_ReportTypeId",
                        column: x => x.ReportTypeId,
                        principalTable: "ReportTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TabularSessionRows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TabularSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowIndex = table.Column<int>(type: "int", nullable: false),
                    EntityKey = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    JsonData = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TabularSessionRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TabularSessionRows_TabularSessions_TabularSessionId",
                        column: x => x.TabularSessionId,
                        principalTable: "TabularSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TabularSessionRows_TabularSessionId_RowIndex",
                table: "TabularSessionRows",
                columns: new[] { "TabularSessionId", "RowIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_TabularSessions_ExpiresAt",
                table: "TabularSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_TabularSessions_ReportTypeId_UserId_FilterHash",
                table: "TabularSessions",
                columns: new[] { "ReportTypeId", "UserId", "FilterHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TabularSessionRows");

            migrationBuilder.DropTable(
                name: "TabularSessions");

            migrationBuilder.DropColumn(
                name: "SubjectKind",
                table: "ReportTypes");
        }
    }
}
