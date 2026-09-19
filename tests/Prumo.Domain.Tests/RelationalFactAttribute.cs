namespace Prumo.Domain.Tests;

/// <summary>Make an unavailable PostgreSQL suite visible in test reports.</summary>
public sealed class RelationalFactAttribute : FactAttribute
{
    public RelationalFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PRUMO_RUN_RELATIONAL_TESTS") != "1")
            Skip = "PostgreSQL: defina PRUMO_RUN_RELATIONAL_TESTS=1 para executar esta verificação.";
    }
}
