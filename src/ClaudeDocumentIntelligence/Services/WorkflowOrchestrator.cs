using System.Diagnostics;
using ClaudeDocumentIntelligence.Models;
using Microsoft.Extensions.Logging;

namespace ClaudeDocumentIntelligence.Services;

public class WorkflowOrchestrator : IWorkflowOrchestrator
{
    private readonly IClaudeIntelligenceService _aiService;
    private readonly ILogger<WorkflowOrchestrator> _logger;

    public const double AutoApproveConfidenceThreshold = 0.90;
    public const double HumanReviewConfidenceThreshold = 0.70;

    public WorkflowOrchestrator(
        IClaudeIntelligenceService aiService, 
        ILogger<WorkflowOrchestrator> logger)
    {
        _aiService = aiService;
        _logger = logger;
    }

    public async Task<ProcessedClaimOutcome> ProcessDocumentWorkflowAsync(
        RawDocument document, 
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Starting HITL Workflow for Document {DocId}", document.DocumentId);

        // 1. AI Extraction Phase
        var extractedData = await _aiService.ExtractClaimStructuredDataAsync(document, cancellationToken);

        // 2. Deterministic Rule & HITL Decision Engine
        var (action, reason) = EvaluateRoutingDecision(extractedData);

        stopwatch.Stop();

        var outcome = new ProcessedClaimOutcome(
            DocumentId: document.DocumentId,
            ExtractedData: extractedData,
            Action: action,
            RoutingReason: reason,
            ProcessedAtUtc: DateTime.UtcNow,
            ModelUsed: _aiService.ModelName ?? "unknown",
            ProcessingDuration: stopwatch.Elapsed
        );

        _logger.LogInformation("Completed Workflow for {DocId} -> Decision: {Action} ({Reason}) in {ElapsedMs}ms",
            document.DocumentId, action, reason, stopwatch.ElapsedMilliseconds);

        return outcome;
    }

    public static (RoutingAction Action, string Reason) EvaluateRoutingDecision(ExtractedClaimData data)
    {
        // Rule 1: Missing signature always triggers human review
        if (!data.IsSigned)
        {
            return (RoutingAction.RequiresHumanReview, "Document lacks verified signature. Sent to paralegal queue.");
        }

        // Rule 2: Detected anomalies escalate to Audit
        if (data.DetectedAnomalies.Count > 0)
        {
            return (RoutingAction.FlaggedForAudit, $"Anomalies flagged: {string.Join("; ", data.DetectedAnomalies)}");
        }

        // Rule 3: High amount (> $5,000) requires human sign-off regardless of confidence
        if (data.RequestedAmount.HasValue && data.RequestedAmount.Value > 5000.00m)
        {
            return (RoutingAction.RequiresHumanReview, $"Claim amount (${data.RequestedAmount.Value:N2}) exceeds autonomous approval threshold ($5,000.00).");
        }

        // Rule 4: High Confidence auto-approves
        if (data.ExtractionConfidence >= AutoApproveConfidenceThreshold)
        {
            return (RoutingAction.AutoApprove, $"Confidence ({data.ExtractionConfidence:P0}) meets auto-approval criteria.");
        }

        // Rule 5: Medium Confidence routes to Human Review
        if (data.ExtractionConfidence >= HumanReviewConfidenceThreshold)
        {
            return (RoutingAction.RequiresHumanReview, $"Confidence ({data.ExtractionConfidence:P0}) requires human verification.");
        }

        // Rule 6: Low confidence rejected / escalated
        return (RoutingAction.Rejected, $"Confidence ({data.ExtractionConfidence:P0}) is below minimum acceptable threshold (70%).");
    }
}
