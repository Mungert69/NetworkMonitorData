using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetworkMonitorData.Migrations
{
    /// <inheritdoc />
    public partial class ChangeToLongPingInfoID : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PingInfos",
                table: "PingInfos");

            migrationBuilder.AddColumn<ulong>(
                name: "ID",
                table: "PingInfos",
                type: "bigint unsigned",
                nullable: false,
                defaultValue: 0ul)
                .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

            migrationBuilder.AddPrimaryKey(
                name: "PK_PingInfos",
                table: "PingInfos",
                column: "ID");

            migrationBuilder.CreateIndex(
                name: "IX_PingInfos_MonitorPingInfoID",
                table: "PingInfos",
                column: "MonitorPingInfoID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PingInfos",
                table: "PingInfos");

            migrationBuilder.DropIndex(
                name: "IX_PingInfos_MonitorPingInfoID",
                table: "PingInfos");

            migrationBuilder.DropColumn(
                name: "ID",
                table: "PingInfos");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PingInfos",
                table: "PingInfos",
                columns: new[] { "MonitorPingInfoID", "DateSentInt" });
        }
    }
}
