using System.Text.Json.Serialization;

namespace ClaudeDocumentIntelligence.Models;

/// <summary>
/// Represents an incoming unstructured document or claim submission for AI processing.
/// </summary>
public record RawDocument(
    string DocumentId,
    string DocumentType,
    string Content,
    DateTime ReceivedAtUtc
);

/// <summary>
/// Strongly-typed structured data extracted by Claude from the raw document.
/// </summary>
public record ExtractedClaimData
{
    [JsonPropertyName("claimant_name")]
    public string ClaimantName { get; init; } = string.Empty;

    [JsonPropertyName("case_reference_number")]
    public string CaseReferenceNumber { get; init; } = string.Empty;

    [JsonPropertyName("claim_type")]
    public string ClaimType { get; init; } = string.Empty;

    [JsonPropertyName("requested_amount")]
    public decimal? RequestedAmount { get; init; }

    [JsonPropertyName("incident_date")]
    public string? IncidentDate { get; init; }

    [JsonPropertyName("is_signed")]
    public bool IsSigned { get; init; }

    [JsonPropertyName("summary_of_loss")]
    public string SummaryOfLoss { get; init; } = string.Empty;

    [JsonPropertyName("extraction_confidence")]
    public double ExtractionConfidence { get; init; }

    [JsonPropertyName("detected_anomalies")]
    public List<string> DetectedAnomalies { get; init; } = [];
}

/// <summary>
/// Result of the Human-in-the-Loop decision engine.
/// </summary>
public enum RoutingAction
{
    AutoApprove,
    RequiresHumanReview,
    FlaggedForAudit,
    Rejected
}

/// <summary>
/// Final processed outcome with audit trail.
/// </summary>
public record ProcessedClaimOutcome(
    string DocumentId,
    ExtractedClaimData ExtractedData,
    RoutingAction Action,
    string RoutingReason,
    DateTime ProcessedAtUtc,
    string ModelUsed,
    TimeSpan ProcessingDuration
);
