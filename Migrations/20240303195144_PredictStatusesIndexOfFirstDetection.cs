using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetworkMonitorData.Migrations
{
    /// <inheritdoc />
    public partial class PredictStatusesIndexOfFirstDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChangeDetectionResult_IndexOfFirstDetection",
                table: "PredictStatuses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SpikeDetectionResult_IndexOfFirstDetection",
                table: "PredictStatuses",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChangeDetectionResult_IndexOfFirstDetection",
                table: "PredictStatuses");

            migrationBuilder.DropColumn(
                name: "SpikeDetectionResult_IndexOfFirstDetection",
                table: "PredictStatuses");
        }
    }
}
