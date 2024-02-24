using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetworkMonitorData.Migrations
{
    /// <inheritdoc />
    public partial class PredicStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PredictStatuses",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AlertFlag = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AlertSent = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EventTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    MonitorPingInfoID = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChangeDetectionResult_IsIssueDetected = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ChangeDetectionResult_NumberOfDetections = table.Column<int>(type: "int", nullable: false),
                    ChangeDetectionResult_IsDataLimited = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ChangeDetectionResult_AverageScore = table.Column<double>(type: "double", nullable: false),
                    ChangeDetectionResult_MinPValue = table.Column<double>(type: "double", nullable: false),
                    ChangeDetectionResult_MaxMartingaleValue = table.Column<double>(type: "double", nullable: false),
                    SpikeDetectionResult_IsIssueDetected = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SpikeDetectionResult_NumberOfDetections = table.Column<int>(type: "int", nullable: false),
                    SpikeDetectionResult_IsDataLimited = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SpikeDetectionResult_AverageScore = table.Column<double>(type: "double", nullable: false),
                    SpikeDetectionResult_MinPValue = table.Column<double>(type: "double", nullable: false),
                    SpikeDetectionResult_MaxMartingaleValue = table.Column<double>(type: "double", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PredictStatuses", x => x.ID);
                    table.ForeignKey(
                        name: "FK_PredictStatuses_MonitorPingInfos_MonitorPingInfoID",
                        column: x => x.MonitorPingInfoID,
                        principalTable: "MonitorPingInfos",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PredictStatuses_MonitorPingInfoID",
                table: "PredictStatuses",
                column: "MonitorPingInfoID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PredictStatuses");
        }
    }
}
