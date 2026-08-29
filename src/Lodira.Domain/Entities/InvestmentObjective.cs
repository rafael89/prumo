using Lodira.Domain.Enums;

namespace Lodira.Domain.Entities;

public sealed class InvestmentObjective
{
    private InvestmentObjective() { }

    public InvestmentObjective(
        Guid portfolioId,
        InvestmentGoal primaryGoal,
        TargetMetric targetMetric,
        decimal? targetAmount,
        TimeHorizon timeHorizon,
        int version)
    {
        if (portfolioId == Guid.Empty) throw new ArgumentException("A carteira é obrigatória.", nameof(portfolioId));
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        ValidateTarget(targetMetric, targetAmount);

        Id = Guid.NewGuid();
        PortfolioId = portfolioId;
        PrimaryGoal = primaryGoal;
        TargetMetric = targetMetric;
        TargetAmount = targetAmount;
        TimeHorizon = timeHorizon;
        Version = version;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid PortfolioId { get; private set; }
    public InvestmentGoal PrimaryGoal { get; private set; }
    public TargetMetric TargetMetric { get; private set; }
    public decimal? TargetAmount { get; private set; }
    public TimeHorizon TimeHorizon { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }

    public void Archive(DateTimeOffset? archivedAt = null) => ArchivedAt = archivedAt ?? DateTimeOffset.UtcNow;

    public static void ValidateTarget(TargetMetric metric, decimal? amount)
    {
        if (metric == TargetMetric.None && amount.HasValue) throw new ArgumentException("TargetAmount exige um TargetMetric.", nameof(amount));
        if (metric != TargetMetric.None && (!amount.HasValue || amount <= 0)) throw new ArgumentException("TargetAmount deve ser positivo quando TargetMetric está definido.", nameof(amount));
    }
}
