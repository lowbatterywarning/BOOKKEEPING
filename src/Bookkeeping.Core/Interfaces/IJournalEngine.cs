using Bookkeeping.Core.Models;

namespace Bookkeeping.Core.Interfaces;

/// <summary>
/// Core double-entry bookkeeping engine.
/// Every financial transaction must flow through this engine to ensure the ledger always balances.
/// </summary>
public interface IJournalEngine
{
    /// <summary>
    /// Record a donation: Debit Cash/Bank, Credit the appropriate income account.
    /// Returns the created JournalEntry.
    /// </summary>
    Task<JournalEntry> RecordDonationAsync(Donation donation);

    /// <summary>
    /// Record an expense: Debit the expense account, Credit Cash/Bank.
    /// Returns the created JournalEntry.
    /// </summary>
    Task<JournalEntry> RecordExpenseAsync(Expense expense);

    /// <summary>
    /// Record a cash-to-bank or bank-to-cash transfer.
    /// Returns the created JournalEntry.
    /// </summary>
    Task<JournalEntry> RecordTransferAsync(CashBankTransfer transfer);

    /// <summary>
    /// Get the current balance of any account.
    /// </summary>
    Task<decimal> GetAccountBalanceAsync(int accountId);

    /// <summary>
    /// Get the balance of a specific fund (restricted or unrestricted).
    /// Computed as: Fund Income - Fund Expenses.
    /// </summary>
    Task<decimal> GetFundBalanceAsync(int fundId);

    /// <summary>
    /// Record a beginning/opening balance for an asset account (Cash or Bank).
    /// Debits the asset, credits the equity account.
    /// Returns the created JournalEntry.
    /// </summary>
    Task<JournalEntry> RecordBeginningBalanceAsync(string accountCode, decimal amount, DateTime date, int createdByUserId);

    /// <summary>
    /// Validate that a journal entry is balanced (total debits = total credits).
    /// </summary>
    bool IsBalanced(JournalEntry entry);
}
