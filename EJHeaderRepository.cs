using LegalHoldAdmin.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LegalHoldAdmin.Data;

public sealed class EJHeaderRepository : IEJHeaderRepository
{
    private readonly IDbContextFactory<LegalHoldDbContext> _dbContextFactory;
    private readonly ILogger<EJHeaderRepository> _logger;

    public EJHeaderRepository(
        IDbContextFactory<LegalHoldDbContext> dbContextFactory,
        ILogger<EJHeaderRepository> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<PagedResult<EJHeaderRecord>> LoadPageAsync(
        string accessNumber,
        int skip,
        int pageSize,
        string? orderBy = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessNumber))
        {
            throw new ArgumentException("Access number is required.", nameof(accessNumber));
        }

        if (accessNumber.Trim().Length > 14)
        {
            throw new ArgumentException("Access number cannot exceed 14 characters.", nameof(accessNumber));
        }

        var normalizedAccessNumber = accessNumber.Trim().PadLeft(14, '0');
        var accessNumberVariants = BuildAccessNumberVariants(normalizedAccessNumber);
        var safeSkip = Math.Max(skip, 0);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var query = dbContext.EJHeaders
                .AsNoTracking()
                .Where(row => row.AccessNo != null && accessNumberVariants.Contains(row.AccessNo));

            var totalCount = await query.CountAsync(cancellationToken);
            var rows = await ApplySorting(query, orderBy)
                .Skip(safeSkip)
                .Take(safePageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<EJHeaderRecord>(rows, totalCount);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to load dbo.EJHeader");
            throw new InvalidOperationException($"Database error while loading dbo.EJHeader: {ex.Message}", ex);
        }
    }

    private static string[] BuildAccessNumberVariants(string normalizedAccessNumber)
    {
        var unpaddedAccessNumber = normalizedAccessNumber.TrimStart('0');
        if (unpaddedAccessNumber.Length == 0)
        {
            unpaddedAccessNumber = "0";
        }

        return Enumerable.Range(unpaddedAccessNumber.Length, 15 - unpaddedAccessNumber.Length)
            .Select(length => unpaddedAccessNumber.PadLeft(length, '0'))
            .ToArray();
    }

    private static IOrderedQueryable<EJHeaderRecord> ApplySorting(
        IQueryable<EJHeaderRecord> query,
        string? orderBy)
    {
        var (property, descending) = ParseFirstSort(orderBy);

        return (property, descending) switch
        {
            (nameof(EJHeaderRecord.TranName), false) =>
                query.OrderBy(row => row.TranName).ThenByDescending(row => row.RecordID),
            (nameof(EJHeaderRecord.TranName), true) =>
                query.OrderByDescending(row => row.TranName).ThenByDescending(row => row.RecordID),
            (nameof(EJHeaderRecord.EJTime), false) =>
                query.OrderBy(row => row.EJTime).ThenByDescending(row => row.RecordID),
            (nameof(EJHeaderRecord.EJTime), true) =>
                query.OrderByDescending(row => row.EJTime).ThenByDescending(row => row.RecordID),
            (nameof(EJHeaderRecord.CalendarDate), false) =>
                query.OrderBy(row => row.CalendarDate).ThenByDescending(row => row.RecordID),
            (nameof(EJHeaderRecord.CalendarDate), true) =>
                query.OrderByDescending(row => row.CalendarDate).ThenByDescending(row => row.RecordID),
            (nameof(EJHeaderRecord.AccountNumber), false) =>
                query.OrderBy(row => row.AccountNumber).ThenByDescending(row => row.RecordID),
            (nameof(EJHeaderRecord.AccountNumber), true) =>
                query.OrderByDescending(row => row.AccountNumber).ThenByDescending(row => row.RecordID),
            (nameof(EJHeaderRecord.TranAmount), false) =>
                query.OrderBy(row => row.TranAmount).ThenByDescending(row => row.RecordID),
            (nameof(EJHeaderRecord.TranAmount), true) =>
                query.OrderByDescending(row => row.TranAmount).ThenByDescending(row => row.RecordID),
            (nameof(EJHeaderRecord.BeginTime), false) =>
                query.OrderBy(row => row.BeginTime).ThenByDescending(row => row.RecordID),
            (nameof(EJHeaderRecord.BeginTime), true) =>
                query.OrderByDescending(row => row.BeginTime).ThenByDescending(row => row.RecordID),
            (nameof(EJHeaderRecord.AccessNo), false) =>
                query.OrderBy(row => row.AccessNo).ThenByDescending(row => row.RecordID),
            (nameof(EJHeaderRecord.AccessNo), true) =>
                query.OrderByDescending(row => row.AccessNo).ThenByDescending(row => row.RecordID),
            _ => query.OrderByDescending(row => row.RecordID)
        };
    }

    private static (string? Property, bool Descending) ParseFirstSort(string? orderBy)
    {
        if (string.IsNullOrWhiteSpace(orderBy))
        {
            return (null, true);
        }

        var firstSort = orderBy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        var parts = firstSort.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var descending = parts.Length > 1 && parts[1].Equals("DESC", StringComparison.OrdinalIgnoreCase);

        return (parts[0], descending);
    }
}
