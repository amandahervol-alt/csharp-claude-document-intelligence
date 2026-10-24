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

    /// <summary>Default model. Claude Sonnet 3.5 was retired on October 28, 2025.</summary>
    public const string DefaultModel = "claude-sonnet-5-5";

    public string ModelName => _model;

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
        _model = string.IsNullOrWhiteSpace(configuration["Anthropic:Model"]) ? DefaultModel : configuration["Anthropic:Model"]!;
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
                // Headroom for adaptive thinking, which is on by default for current models.
                max_tokens = 4096,
                // Current Claude models reject temperature/top_p/top_k with a 400 error.
                // Low effort keeps simple extraction fast and minimizes thinking.
                output_config = new { effort = "low" },
                system = SystemPrompt,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = $$"""
                            Extract the structured claim data from the following document text:
                            ---
                            Document ID: {{document.DocumentId}}
                            Type: {{document.DocumentType}}
                            Content:
                            {{document.Content}}
                            ---
                            Return JSON adhering to schema:
                            {
                              "claimant_name": "string",
                              "case_reference_number": "string",
                              "claim_type": "string",
                              "requested_amount": number or null,
                              "incident_date": "YYYY-MM-DD" or null,
                              "is_signed": boolean,
                              "summary_of_loss": "string",
                              "extraction_confidence": float between 0.0 and 1.0,
                              "detected_anomalies": ["string"]
                            }
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
            // With adaptive thinking, "thinking" blocks can precede the answer,
            // so read the first "text" block rather than assuming content[0].
            var contentText = ExtractFirstTextBlock(doc.RootElement);

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

    public static string ExtractFirstTextBlock(JsonElement root)
    {
        foreach (var block in root.GetProperty("content").EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type) && type.GetString() == "text")
            {
                return StripCodeFence(block.GetProperty("text").GetString() ?? "{}");
            }
        }
        throw new InvalidOperationException("Claude response contained no text block.");
    }

    private static string StripCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```")) return trimmed;
        var firstNewline = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```");
        return firstNewline >= 0 && lastFence > firstNewline
            ? trimmed[(firstNewline + 1)..lastFence].Trim()
            : trimmed;
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
