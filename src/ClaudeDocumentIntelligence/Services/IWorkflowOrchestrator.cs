using ClaudeDocumentIntelligence.Models;

namespace ClaudeDocumentIntelligence.Services;

public interface IWorkflowOrchestrator
{
    Task<ProcessedClaimOutcome> ProcessDocumentWorkflowAsync(RawDocument document, CancellationToken cancellationToken = default);
}
