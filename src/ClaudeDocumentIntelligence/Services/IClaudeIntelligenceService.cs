using ClaudeDocumentIntelligence.Models;

namespace ClaudeDocumentIntelligence.Services;

public interface IClaudeIntelligenceService
{
    /// <summary>The Claude model ID used for extraction (recorded in the audit outcome).</summary>
    string ModelName { get; }

    Task<ExtractedClaimData> ExtractClaimStructuredDataAsync(RawDocument document, CancellationToken cancellationToken = default);
}
