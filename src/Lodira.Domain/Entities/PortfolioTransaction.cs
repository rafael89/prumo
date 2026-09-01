using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lodira.Domain.Enums;

namespace Lodira.Domain.Entities;

public sealed class PortfolioTransaction
{
    private PortfolioTransaction() { }
    public PortfolioTransaction(Guid portfolioId, PortfolioTransactionType type, DateTimeOffset occurredAt, Guid? contributionGuidanceId, decimal totalAmount, string? importFingerprint = null)
    {
        if (portfolioId == Guid.Empty) throw new ArgumentException("A carteira é obrigatória.", nameof(portfolioId));
        if (totalAmount <= 0) throw new ArgumentOutOfRangeException(nameof(totalAmount));
        Id = Guid.NewGuid(); PortfolioId = portfolioId; Type = type; OccurredAt = occurredAt.ToUniversalTime();
        ContributionGuidanceId = contributionGuidanceId; TotalAmount = decimal.Round(totalAmount, 2); ImportFingerprint = string.IsNullOrWhiteSpace(importFingerprint) ? null : importFingerprint.Trim().ToLowerInvariant(); CreatedAt = DateTimeOffset.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid PortfolioId { get; private set; }
    public PortfolioTransactionType Type { get; private set; }
    public Guid? ContributionGuidanceId { get; private set; }
    public decimal TotalAmount { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? ImportFingerprint { get; private set; }
    public List<PortfolioTransactionItem> Items { get; private set; } = new();

    public static string CreateImportFingerprint(Guid portfolioId, Guid assetId, DateTimeOffset occurredAt, decimal quantity, decimal unitPrice, string? institution)
    {
        var canonical = string.Join("|", portfolioId.ToString("D"), assetId.ToString("D"), occurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), quantity.ToString("G29", CultureInfo.InvariantCulture), unitPrice.ToString("G29", CultureInfo.InvariantCulture), institution?.Trim().ToUpperInvariant() ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
