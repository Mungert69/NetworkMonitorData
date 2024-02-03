using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetworkMonitorData.Migrations
{
    /// <inheritdoc />
    public partial class TestUsersEmailKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_TestUsers",
                table: "TestUsers");

            migrationBuilder.AlterColumn<string>(
                name: "UserID",
                table: "TestUsers",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldMaxLength: 50)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TestUsers",
                table: "TestUsers",
                column: "Email");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_TestUsers",
                table: "TestUsers");

            migrationBuilder.UpdateData(
                table: "TestUsers",
                keyColumn: "UserID",
                keyValue: null,
                column: "UserID",
                value: "");

            migrationBuilder.AlterColumn<string>(
                name: "UserID",
                table: "TestUsers",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldMaxLength: 50,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TestUsers",
                table: "TestUsers",
                column: "UserID");
        }
    }
}
