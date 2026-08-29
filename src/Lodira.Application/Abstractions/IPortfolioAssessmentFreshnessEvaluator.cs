using Lodira.Domain.Entities;
using Lodira.Application.Portfolio;
namespace Lodira.Application.Abstractions;
public interface IPortfolioAssessmentFreshnessEvaluator { FreshnessResult Evaluate(PortfolioAssessment assessment, string? currentPortfolioSnapshotKey, string? currentTargetAllocationKey, InvestmentEvent? currentEvent, string currentAssessmentPolicyVersion); }
