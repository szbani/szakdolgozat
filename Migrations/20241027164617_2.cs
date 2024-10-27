using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace szakdolgozat.Migrations
{
    /// <inheritdoc />
    public partial class _2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Displays",
                table: "Displays");

            migrationBuilder.DropColumn(
                name: "DisplayID",
                table: "Displays");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "Displays",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0)
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Displays",
                table: "Displays",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Displays",
                table: "Displays");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "Displays");

            migrationBuilder.AddColumn<string>(
                name: "DisplayID",
                table: "Displays",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Displays",
                table: "Displays",
                column: "DisplayID");
        }
    }
}
