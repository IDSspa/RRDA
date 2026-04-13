using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RRDA.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReportBatchAndRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReportBatchId",
                table: "ReportFiles",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsMaintenance = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportBatches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportFiles_ReportBatchId",
                table: "ReportFiles",
                column: "ReportBatchId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReportFiles_ReportBatches_ReportBatchId",
                table: "ReportFiles",
                column: "ReportBatchId",
                principalTable: "ReportBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReportFiles_ReportBatches_ReportBatchId",
                table: "ReportFiles");

            migrationBuilder.DropTable(
                name: "ReportBatches");

            migrationBuilder.DropIndex(
                name: "IX_ReportFiles_ReportBatchId",
                table: "ReportFiles");

            migrationBuilder.DropColumn(
                name: "ReportBatchId",
                table: "ReportFiles");
        }
    }
}
