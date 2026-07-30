using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bookkeeping.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixExpenseCategoryFundId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPosted",
                table: "JournalEntries");

            migrationBuilder.AddColumn<int>(
                name: "FundId",
                table: "ExpenseCategories",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_FundId",
                table: "ExpenseCategories",
                column: "FundId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseCategories_Funds_FundId",
                table: "ExpenseCategories",
                column: "FundId",
                principalTable: "Funds",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseCategories_Funds_FundId",
                table: "ExpenseCategories");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseCategories_FundId",
                table: "ExpenseCategories");

            migrationBuilder.DropColumn(
                name: "FundId",
                table: "ExpenseCategories");

            migrationBuilder.AddColumn<bool>(
                name: "IsPosted",
                table: "JournalEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
