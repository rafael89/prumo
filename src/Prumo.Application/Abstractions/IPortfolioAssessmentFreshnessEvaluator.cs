using Prumo.Domain.Entities;
using Prumo.Application.Portfolio;
namespace Prumo.Application.Abstractions;
public interface IPortfolioAssessmentFreshnessEvaluator { FreshnessResult Evaluate(PortfolioAssessment assessment, string? currentPortfolioSnapshotKey, string? currentTargetAllocationKey, InvestmentEvent? currentEvent, string currentAssessmentPolicyVersion); }
