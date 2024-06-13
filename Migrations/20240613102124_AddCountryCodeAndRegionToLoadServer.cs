using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetworkMonitorData.Migrations
{
    /// <inheritdoc />
    public partial class AddCountryCodeAndRegionToLoadServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "LoadServers",
                type: "varchar(2)",
                maxLength: 2,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "LoadServers",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "LoadServers");

            migrationBuilder.DropColumn(
                name: "Region",
                table: "LoadServers");
        }
    }
}
