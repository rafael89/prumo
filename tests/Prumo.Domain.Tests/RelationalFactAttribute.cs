using System.Runtime.CompilerServices;

namespace Prumo.Domain.Tests;

/// <summary>Make an unavailable PostgreSQL suite visible in test reports.</summary>
public sealed class RelationalFactAttribute : FactAttribute
{
    public RelationalFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1) : base(sourceFilePath, sourceLineNumber)
    {
        if (!IsEnabled(Environment.GetEnvironmentVariable("PRUMO_RUN_RELATIONAL_TESTS"), ReadDotEnvLines()))
            Skip = "PostgreSQL: defina PRUMO_RUN_RELATIONAL_TESTS=1 no ambiente ou no .env da raiz para executar esta verificação.";
    }

    internal static bool IsEnabled(string? environmentValue, IEnumerable<string> dotEnvLines)
    {
        if (environmentValue is not null) return environmentValue.Trim() == "1";

        foreach (var line in dotEnvLines)
        {
            var content = line.Trim();
            if (content.StartsWith('#') || !content.StartsWith("PRUMO_RUN_RELATIONAL_TESTS", StringComparison.Ordinal)) continue;
            var separator = content.IndexOf('=');
            if (separator < 0 || content[..separator].Trim() != "PRUMO_RUN_RELATIONAL_TESTS") continue;

            var value = content[(separator + 1)..].Split('#', 2)[0].Trim().Trim('"', '\'');
            return value == "1";
        }

        return false;
    }

    private static IEnumerable<string> ReadDotEnvLines()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "Prumo.sln"))) continue;
            var path = Path.Combine(directory.FullName, ".env");
            return File.Exists(path) ? File.ReadLines(path) : Array.Empty<string>();
        }

        return Array.Empty<string>();
    }
}
