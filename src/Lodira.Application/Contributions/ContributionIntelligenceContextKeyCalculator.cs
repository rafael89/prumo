using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lodira.Application.Contributions;

public static class ContributionIntelligenceContextKeyCalculator
{
    public static string Calculate(IEnumerable<GuidanceAssessment> assessments)
    {
        var canonical = assessments
            .OrderBy(x => x.AssetId)
            .ThenBy(x => x.AssessmentId)
            .ThenBy(x => x.AssessmentVersion, StringComparer.Ordinal)
            .ThenBy(x => x.Evidence, StringComparer.Ordinal)
            .ThenBy(x => x.Summary, StringComparer.Ordinal)
            .Select(x => new
            {
                x.AssetId,
                x.AssessmentId,
                x.Impact,
                x.Status,
                x.Evidence,
                x.AssessmentVersion,
                x.AttentionPriority,
                x.ActionRequirement,
                x.Summary,
                x.WhyItMatters,
                x.SuggestedBehavior,
                x.Confidence
            });
        var json = JsonSerializer.Serialize(canonical);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
}
