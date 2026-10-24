using ClaudeDocumentIntelligence.Models;
using ClaudeDocumentIntelligence.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ClaudeDocumentIntelligence.Tests;

public class WorkflowOrchestratorTests
{
    private readonly Mock<IClaudeIntelligenceService> _mockAiService = new();
    private readonly Mock<ILogger<WorkflowOrchestrator>> _mockLogger = new();

    [Fact]
    public async Task ProcessDocument_WithCleanHighConfidenceClaim_AutoApproves()
    {
        // Arrange
        var testDoc = new RawDocument("DOC-001", "Claim", "Content", DateTime.UtcNow);
        var extractedData = new ExtractedClaimData
        {
            ClaimantName = "Jane Doe",
            CaseReferenceNumber = "CLM-100",
            RequestedAmount = 1500.00m,
            IsSigned = true,
            ExtractionConfidence = 0.95,
            DetectedAnomalies = []
        };

        _mockAiService
            .Setup(x => x.ExtractClaimStructuredDataAsync(testDoc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractedData);

        var orchestrator = new WorkflowOrchestrator(_mockAiService.Object, _mockLogger.Object);

        // Act
        var outcome = await orchestrator.ProcessDocumentWorkflowAsync(testDoc);

        // Assert
        Assert.Equal(RoutingAction.AutoApprove, outcome.Action);
        Assert.Contains("meets auto-approval criteria", outcome.RoutingReason);
    }

    [Fact]
    public async Task ProcessDocument_WithoutSignature_RoutesToHumanReview()
    {
        // Arrange
        var testDoc = new RawDocument("DOC-002", "Claim", "Content", DateTime.UtcNow);
        var extractedData = new ExtractedClaimData
        {
            ClaimantName = "John Smith",
            RequestedAmount = 800.00m,
            IsSigned = false, // Missing signature
            ExtractionConfidence = 0.95,
            DetectedAnomalies = []
        };

        _mockAiService
            .Setup(x => x.ExtractClaimStructuredDataAsync(testDoc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractedData);

        var orchestrator = new WorkflowOrchestrator(_mockAiService.Object, _mockLogger.Object);

        // Act
        var outcome = await orchestrator.ProcessDocumentWorkflowAsync(testDoc);

        // Assert
        Assert.Equal(RoutingAction.RequiresHumanReview, outcome.Action);
        Assert.Contains("lacks verified signature", outcome.RoutingReason);
    }

    [Fact]
    public async Task ProcessDocument_WithAnomalies_FlagsForAudit()
    {
        // Arrange
        var testDoc = new RawDocument("DOC-003", "Claim", "Content", DateTime.UtcNow);
        var extractedData = new ExtractedClaimData
        {
            ClaimantName = "Acme Corp",
            RequestedAmount = 10000.00m,
            IsSigned = true,
            ExtractionConfidence = 0.85,
            DetectedAnomalies = ["Conflicting incident dates", "Missing invoice attachment"]
        };

        _mockAiService
            .Setup(x => x.ExtractClaimStructuredDataAsync(testDoc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractedData);

        var orchestrator = new WorkflowOrchestrator(_mockAiService.Object, _mockLogger.Object);

        // Act
        var outcome = await orchestrator.ProcessDocumentWorkflowAsync(testDoc);

        // Assert
        Assert.Equal(RoutingAction.FlaggedForAudit, outcome.Action);
        Assert.Contains("Anomalies flagged", outcome.RoutingReason);
    }

    [Fact]
    public async Task ProcessDocument_WithHighAmountOver5000_RequiresHumanReview()
    {
        // Arrange
        var testDoc = new RawDocument("DOC-004", "Claim", "Content", DateTime.UtcNow);
        var extractedData = new ExtractedClaimData
        {
            ClaimantName = "Robert Paulson",
            RequestedAmount = 7500.00m, // > $5000
            IsSigned = true,
            ExtractionConfidence = 0.99,
            DetectedAnomalies = []
        };

        _mockAiService
            .Setup(x => x.ExtractClaimStructuredDataAsync(testDoc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractedData);

        var orchestrator = new WorkflowOrchestrator(_mockAiService.Object, _mockLogger.Object);

        // Act
        var outcome = await orchestrator.ProcessDocumentWorkflowAsync(testDoc);

        // Assert
        Assert.Equal(RoutingAction.RequiresHumanReview, outcome.Action);
        Assert.Contains("exceeds autonomous approval threshold", outcome.RoutingReason);
    }

    [Fact]
    public async Task ProcessDocument_RecordsModelFromService()
    {
        var testDoc = new RawDocument("DOC-M", "Claim", "Content", DateTime.UtcNow);
        _mockAiService.SetupGet(x => x.ModelName).Returns("claude-sonnet-5-5");
        _mockAiService
            .Setup(x => x.ExtractClaimStructuredDataAsync(testDoc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExtractedClaimData { IsSigned = true, ExtractionConfidence = 0.95, RequestedAmount = 10m, DetectedAnomalies = [] });

        var outcome = await new WorkflowOrchestrator(_mockAiService.Object, _mockLogger.Object).ProcessDocumentWorkflowAsync(testDoc);

        Assert.Equal("claude-sonnet-5-5", outcome.ModelUsed);
    }
}
