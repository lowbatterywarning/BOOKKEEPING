using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bookkeeping.Core.Models;
using Bookkeeping.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;

namespace Bookkeeping.Wpf.Services;

public class ReportService
{
    private readonly AppDbContext _db;

    public ReportService(AppDbContext db) { _db = db; }

    public async Task<List<DonationReportRow>> GetDonationReportAsync(DateTime? from, DateTime? to, int? sponsorId, int? programId, string? paymentMethod)
    {
        var query = _db.Donations.Include(d => d.Sponsor).Include(d => d.Program).AsQueryable();
        if (from.HasValue) query = query.Where(d => d.Date >= from.Value);
        if (to.HasValue) query = query.Where(d => d.Date <= to.Value);
        if (sponsorId.HasValue) query = query.Where(d => d.SponsorId == sponsorId.Value);
        if (programId.HasValue) query = query.Where(d => d.ProgramId == programId.Value);
        if (!string.IsNullOrEmpty(paymentMethod))
            query = query.Where(d => d.PaymentMethod.ToString() == paymentMethod);
        return await query.OrderByDescending(d => d.Date).Select(d => new DonationReportRow
        {
            Date = d.Date,
            Sponsor = d.Sponsor.Name,
            Program = d.Program.Name,
            PaymentMethod = d.PaymentMethod.ToString(),
            Amount = d.Amount,
            ReceiptNumber = d.ReceiptNumber,
            Notes = d.Notes
        }).ToListAsync();
    }

    public async Task<List<ExpenseReportRow>> GetExpenseReportAsync(DateTime? from, DateTime? to, int? programId, string? paymentMethod)
    {
        var query = _db.Expenses.Include(e => e.Program).AsQueryable();
        if (from.HasValue) query = query.Where(e => e.Date >= from.Value);
        if (to.HasValue) query = query.Where(e => e.Date <= to.Value);
        if (programId.HasValue) query = query.Where(e => e.ProgramId == programId.Value);
        if (!string.IsNullOrEmpty(paymentMethod))
            query = query.Where(e => e.PaymentMethod.ToString() == paymentMethod);
        return await query.OrderByDescending(e => e.Date).Select(e => new ExpenseReportRow
        {
            Date = e.Date,
            Vendor = e.VendorName,
            Program = e.Program.Name,
            PaymentMethod = e.PaymentMethod.ToString(),
            Amount = e.Amount,
            Notes = e.Notes
        }).ToListAsync();
    }
}

public class DonationReportRow
{
    public DateTime Date { get; set; }
    public string Sponsor { get; set; } = "";
    public string Program { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public decimal Amount { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? Notes { get; set; }
}

public class ExpenseReportRow
{
    public DateTime Date { get; set; }
    public string Vendor { get; set; } = "";
    public string Program { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}
