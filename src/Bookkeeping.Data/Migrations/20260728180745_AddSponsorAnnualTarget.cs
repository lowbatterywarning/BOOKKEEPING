using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bookkeeping.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSponsorAnnualTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AnnualTargetAmount",
                table: "Sponsors",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnnualTargetAmount",
                table: "Sponsors");
        }
    }
}
