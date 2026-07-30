using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bookkeeping.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSponsorTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnnualTargetAmount",
                table: "Sponsors");

            migrationBuilder.CreateTable(
                name: "SponsorTargets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SponsorId = table.Column<int>(type: "INTEGER", nullable: false),
                    DonationCategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SponsorTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SponsorTargets_DonationCategories_DonationCategoryId",
                        column: x => x.DonationCategoryId,
                        principalTable: "DonationCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SponsorTargets_Sponsors_SponsorId",
                        column: x => x.SponsorId,
                        principalTable: "Sponsors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SponsorTargets_DonationCategoryId",
                table: "SponsorTargets",
                column: "DonationCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_SponsorTargets_SponsorId_DonationCategoryId_Year",
                table: "SponsorTargets",
                columns: new[] { "SponsorId", "DonationCategoryId", "Year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SponsorTargets");

            migrationBuilder.AddColumn<decimal>(
                name: "AnnualTargetAmount",
                table: "Sponsors",
                type: "TEXT",
                nullable: true);
        }
    }
}
