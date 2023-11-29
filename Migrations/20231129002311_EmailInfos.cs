using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetworkMonitorData.Migrations
{
    /// <inheritdoc />
    public partial class EmailInfos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailInfos",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Email = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DateSent = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DateOpened = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IsOpen = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailInfos", x => x.ID);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailInfos");
        }
    }
}
