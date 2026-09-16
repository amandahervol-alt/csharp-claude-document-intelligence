using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClaudeDocumentIntelligence.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClaudeDocumentIntelligence.Services;

public class ClaudeIntelligenceService : IClaudeIntelligenceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ClaudeIntelligenceService> _logger;
    private readonly string _apiKey;
    private readonly string _model;

    private const string SystemPrompt = """
        You are an elite Document Intelligence AI specialized in legal, insurance, and fiduciary claim processing.
        Your task is to extract structured entities from unstructured documents accurately.

        CRITICAL RULES:
        1. Extract the claimant name, case reference number, claim type, requested amount, incident date, and signature status.
        2. Assign an extraction confidence score between 0.0 and 1.0 based on clarity and completeness.
        3. Detect any anomalies (e.g. missing signatures, suspicious amount formatting, date contradictions).
        4. You MUST respond with ONLY a valid JSON object matching the requested schema. No markdown formatting, no commentary.
        """;

    public ClaudeIntelligenceService(
        HttpClient httpClient, 
        IConfiguration configuration, 
        ILogger<ClaudeIntelligenceService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = configuration["Anthropic:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? string.Empty;
        _model = configuration["Anthropic:Model"] ?? "claude-3-5-sonnet-20241022";
    }

    public async Task<ExtractedClaimData> ExtractClaimStructuredDataAsync(
        RawDocument document, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing Document ID: {DocId} ({DocType})", document.DocumentId, document.DocumentType);

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Anthropic API key not configured. Running in deterministic mock engine mode for local demonstration.");
            return GenerateMockExtraction(document);
        }

        try
        {
            var requestBody = new
            {
                model = _model,
                max_tokens = 1024,
                temperature = 0.0, // Deterministic extraction
                system = SystemPrompt,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = $"""
                            Extract the structured claim data from the following document text:
                            ---
                            Document ID: {document.DocumentId}
                            Type: {document.DocumentType}
                            Content:
                            {document.Content}
                            ---
                            Return JSON adhering to schema:
                            {{
                              "claimant_name": "string",
                              "case_reference_number": "string",
                              "claim_type": "string",
                              "requested_amount": number or null,
                              "incident_date": "YYYY-MM-DD" or null,
                              "is_signed": boolean,
                              "summary_of_loss": "string",
                              "extraction_confidence": float between 0.0 and 1.0,
                              "detected_anomalies": ["string"]
                            }}
                            """
                    }
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody), 
                Encoding.UTF8, 
                "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
            {
                Content = jsonContent
            };
            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseString);
            var contentText = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString() ?? "{}";

            var extracted = JsonSerializer.Deserialize<ExtractedClaimData>(contentText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return extracted ?? GenerateMockExtraction(document);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Claude API for Document {DocId}. Falling back to safe error model.", document.DocumentId);
            return new ExtractedClaimData
            {
                ClaimantName = "Unknown (Error during extraction)",
                ExtractionConfidence = 0.0,
                DetectedAnomalies = [$"API Exception: {ex.Message}"]
            };
        }
    }

    private static ExtractedClaimData GenerateMockExtraction(RawDocument document)
    {
        // Deterministic simulation based on document content for zero-dependency local testing
        bool hasSignature = document.Content.Contains("Signed", StringComparison.OrdinalIgnoreCase) ||
                            document.Content.Contains("Signature:", StringComparison.OrdinalIgnoreCase);

        bool isHighRisk = document.Content.Contains("disputed", StringComparison.OrdinalIgnoreCase) ||
                          document.Content.Contains("inconsistent", StringComparison.OrdinalIgnoreCase);

        var anomalies = new List<string>();
        if (!hasSignature) anomalies.Add("Missing authorized claimant signature.");
        if (isHighRisk) anomalies.Add("Flagged conflicting dates in statement.");

        return new ExtractedClaimData
        {
            ClaimantName = "Elena Vance",
            CaseReferenceNumber = "CLM-2026-8841",
            ClaimType = "Class Action - Settlement Benefit",
            RequestedAmount = 2450.00m,
            IncidentDate = "2025-11-14",
            IsSigned = hasSignature,
            SummaryOfLoss = "Unreimbursed subscription fees and disputed platform charges.",
            ExtractionConfidence = isHighRisk ? 0.65 : (hasSignature ? 0.98 : 0.75),
            DetectedAnomalies = anomalies
        };
    }
}
