namespace Lodira.Domain.Entities;

public sealed class Portfolio
{
    private Portfolio()
    {
    }

    public Portfolio(string name)
    {
        Id = Guid.NewGuid();
        Name = ValidateName(name);
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid? UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public List<PortfolioPosition> Positions { get; private set; } = new();
    public List<TargetAllocation> TargetAllocations { get; private set; } = new();

    public void Rename(string name)
    {
        Name = ValidateName(name);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("O nome da carteira é obrigatório.", nameof(name));
        }

        return name.Trim();
    }
}
