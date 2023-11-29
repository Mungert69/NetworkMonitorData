using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetworkMonitorData.Migrations
{
    /// <inheritdoc />
    public partial class EmailInfoEmailType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailType",
                table: "EmailInfos",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailType",
                table: "EmailInfos");
        }
    }
}
