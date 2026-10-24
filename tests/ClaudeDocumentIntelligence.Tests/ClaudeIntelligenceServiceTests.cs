using System.Net;
using System.Text;
using System.Text.Json;
using ClaudeDocumentIntelligence.Models;
using ClaudeDocumentIntelligence.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClaudeDocumentIntelligence.Tests;

public class ClaudeIntelligenceServiceTests
{
    private sealed class CapturingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public HttpRequestMessage? Request { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static ClaudeIntelligenceService CreateService(HttpMessageHandler handler, string apiKey = "test-key")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Anthropic:ApiKey"] = apiKey,
                ["Anthropic:Model"] = "test-model"
            })
            .Build();
        return new ClaudeIntelligenceService(new HttpClient(handler), config, NullLogger<ClaudeIntelligenceService>.Instance);
    }

    private static string ClaudeResponse(object extracted) =>
        JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(extracted) } } });

    private static readonly RawDocument Doc = new("DOC-XYZ-42", "ProofOfClaim", "Claimant: Ada Lovelace. Amount: $99.00.", DateTime.UtcNow);

    [Fact]
    public async Task Prompt_InterpolatesDocumentValues_AndRendersSingleBraceSchema()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, ClaudeResponse(new { claimant_name = "Ada Lovelace" }));
        await CreateService(handler).ExtractClaimStructuredDataAsync(Doc);

        using var req = JsonDocument.Parse(handler.RequestBody!);
        var prompt = req.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;

        Assert.Contains("Document ID: DOC-XYZ-42", prompt);
        Assert.Contains("Type: ProofOfClaim", prompt);
        Assert.Contains("Claimant: Ada Lovelace. Amount: $99.00.", prompt);
        Assert.DoesNotContain("{{", prompt);
        Assert.DoesNotContain("{document.", prompt);
        Assert.Contains("\"claimant_name\": \"string\"", prompt);
        Assert.Equal("test-model", req.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Request_SendsAnthropicHeaders()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, ClaudeResponse(new { claimant_name = "x" }));
        await CreateService(handler).ExtractClaimStructuredDataAsync(Doc);

        Assert.Equal("https://api.anthropic.com/v1/messages", handler.Request!.RequestUri!.ToString());
        Assert.Equal("test-key", handler.Request.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", handler.Request.Headers.GetValues("anthropic-version").Single());
    }

    [Fact]
    public async Task SuccessfulResponse_IsDeserializedIntoClaimData()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, ClaudeResponse(new
        {
            claimant_name = "Ada Lovelace",
            case_reference_number = "CLM-1",
            requested_amount = 99.0,
            is_signed = true,
            extraction_confidence = 0.91,
            detected_anomalies = new[] { "none" }
        }));

        var result = await CreateService(handler).ExtractClaimStructuredDataAsync(Doc);

        Assert.Equal("Ada Lovelace", result.ClaimantName);
        Assert.Equal("CLM-1", result.CaseReferenceNumber);
        Assert.Equal(99.0m, result.RequestedAmount);
        Assert.True(result.IsSigned);
        Assert.Equal(0.91, result.ExtractionConfidence);
    }

    [Fact]
    public async Task ApiError_ReturnsZeroConfidenceErrorModel()
    {
        var handler = new CapturingHandler(HttpStatusCode.InternalServerError, "{}");
        var result = await CreateService(handler).ExtractClaimStructuredDataAsync(Doc);

        Assert.Equal(0.0, result.ExtractionConfidence);
        Assert.StartsWith("Unknown", result.ClaimantName);
        Assert.Contains(result.DetectedAnomalies, a => a.StartsWith("API Exception"));
    }

    [Fact]
    public async Task MissingApiKey_UsesMockEngine_WithoutCallingApi()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "{}");
        var unsigned = Doc with { Content = "No signature here" };
        var result = await CreateService(handler, apiKey: "").ExtractClaimStructuredDataAsync(unsigned);

        Assert.Null(handler.Request);
        Assert.False(result.IsSigned);
        Assert.Contains("Missing authorized claimant signature.", result.DetectedAnomalies);
    }
}
