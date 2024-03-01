using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetworkMonitorData.Migrations
{
    /// <inheritdoc />
    public partial class IndexMonitorPingInfoMonitorIPIDDataSetID : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IDX_MonitorIPID_DataSetID",
                table: "MonitorPingInfos",
                columns: new[] { "MonitorIPID", "DataSetID" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IDX_MonitorIPID_DataSetID",
                table: "MonitorPingInfos");
        }
    }
}
