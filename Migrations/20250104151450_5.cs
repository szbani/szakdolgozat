using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace szakdolgozat.Migrations
{
    /// <inheritdoc />
    public partial class _5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Playlists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlaylistName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PlaylistDescription = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PlaylistType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PlaylistItems = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Playlists", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Playlists");
        }
    }
}
