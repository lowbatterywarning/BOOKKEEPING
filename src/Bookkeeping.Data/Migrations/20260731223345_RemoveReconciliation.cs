using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bookkeeping.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReconciledItems");

            migrationBuilder.DropTable(
                name: "BankReconciliations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankReconciliations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Difference = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    LedgerBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    StatementBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    StatementDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReconciliations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankReconciliations_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReconciledItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BankReconciliationId = table.Column<int>(type: "INTEGER", nullable: false),
                    JournalEntryLineId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsCleared = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciledItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReconciledItems_BankReconciliations_BankReconciliationId",
                        column: x => x.BankReconciliationId,
                        principalTable: "BankReconciliations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReconciledItems_JournalEntryLines_JournalEntryLineId",
                        column: x => x.JournalEntryLineId,
                        principalTable: "JournalEntryLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliations_CreatedByUserId",
                table: "BankReconciliations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliations_StatementDate",
                table: "BankReconciliations",
                column: "StatementDate");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciledItems_BankReconciliationId",
                table: "ReconciledItems",
                column: "BankReconciliationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciledItems_JournalEntryLineId",
                table: "ReconciledItems",
                column: "JournalEntryLineId");
        }
    }
}
