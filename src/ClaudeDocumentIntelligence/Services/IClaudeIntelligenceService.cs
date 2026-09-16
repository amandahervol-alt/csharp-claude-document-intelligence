using ClaudeDocumentIntelligence.Models;

namespace ClaudeDocumentIntelligence.Services;

public interface IClaudeIntelligenceService
{
    Task<ExtractedClaimData> ExtractClaimStructuredDataAsync(RawDocument document, CancellationToken cancellationToken = default);
}
