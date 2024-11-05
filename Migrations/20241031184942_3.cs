﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace szakdolgozat.Migrations
{
    /// <inheritdoc />
    public partial class _3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ipAddress",
                table: "Displays");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ipAddress",
                table: "Displays",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
