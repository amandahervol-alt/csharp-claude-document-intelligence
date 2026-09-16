using System.Text.Json;
using ClaudeDocumentIntelligence.Models;
using ClaudeDocumentIntelligence.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClaudeDocumentIntelligence;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("===============================================================================");
        Console.WriteLine("   C# .NET 8 Document Intelligence & Human-in-the-Loop Orchestrator (Claude)   ");
        Console.WriteLine("===============================================================================");
        Console.ResetColor();

        // 1. Build Configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        // 2. Setup Dependency Injection
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        services.AddHttpClient<IClaudeIntelligenceService, ClaudeIntelligenceService>();
        services.AddScoped<IWorkflowOrchestrator, WorkflowOrchestrator>();

        var serviceProvider = services.BuildServiceProvider();
        var orchestrator = serviceProvider.GetRequiredService<IWorkflowOrchestrator>();

        // 3. Define Sample Test Scenarios (Clean, Missing Signature, and High-Risk/Disputed)
        var testDocuments = new List<RawDocument>
        {
            new(
                DocumentId: "DOC-2026-001",
                DocumentType: "ClassActionClaimForm",
                Content: "Claimant: Elena Vance. Case: CLM-2026-8841. Date: 2025-11-14. Amount: $2,450.00. Signed: Elena Vance. Statement: Requesting standard settlement distribution for unauthorized subscription charges.",
                ReceivedAtUtc: DateTime.UtcNow.AddHours(-2)
            ),
            new(
                DocumentId: "DOC-2026-002",
                DocumentType: "ProofOfClaim",
                Content: "Claimant: Marcus Brody. Case: CLM-2026-9012. Amount: $1,200.00. Statement: Medical reimbursement claim under class settlement terms. (Note: Signature field was left blank by claimant).",
                ReceivedAtUtc: DateTime.UtcNow.AddHours(-1)
            ),
            new(
                DocumentId: "DOC-2026-003",
                DocumentType: "RestructuringCreditorFiling",
                Content: "Claimant: Apex Logistics Corp. Case: RST-2026-1104. Amount: $18,750.00. Signed: Authorized Officer. Statement: Disputed contract breach settlement. Inconsistent termination penalty calculations attached.",
                ReceivedAtUtc: DateTime.UtcNow
            )
        };

        Console.WriteLine($"\n[INFO] Ingesting and processing {testDocuments.Count} test documents through AI extraction & HITL gate...\n");

        foreach (var doc in testDocuments)
        {
            Console.WriteLine(new string('-', 75));
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[INGESTION] Document ID: {doc.DocumentId} | Type: {doc.DocumentType}");
            Console.ResetColor();

            var outcome = await orchestrator.ProcessDocumentWorkflowAsync(doc);

            // Display Results
            Console.ForegroundColor = outcome.Action switch
            {
                RoutingAction.AutoApprove => ConsoleColor.Green,
                RoutingAction.RequiresHumanReview => ConsoleColor.Yellow,
                RoutingAction.FlaggedForAudit => ConsoleColor.Red,
                _ => ConsoleColor.DarkGray
            };

            Console.WriteLine($"-> DECISION: [{outcome.Action}]");
            Console.WriteLine($"-> REASON:   {outcome.RoutingReason}");
            Console.ResetColor();

            Console.WriteLine($"-> Extracted Claimant: {outcome.ExtractedData.ClaimantName}");
            Console.WriteLine($"-> Claim Amount:       ${outcome.ExtractedData.RequestedAmount:N2}");
            Console.WriteLine($"-> Confidence Score:   {outcome.ExtractedData.ExtractionConfidence:P0}");
            Console.WriteLine($"-> Execution Latency:  {outcome.ProcessingDuration.TotalMilliseconds:F1}ms");

            if (outcome.ExtractedData.DetectedAnomalies.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"-> Anomalies:          {string.Join(", ", outcome.ExtractedData.DetectedAnomalies)}");
                Console.ResetColor();
            }
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n===============================================================================");
        Console.WriteLine("   All workflows successfully orchestrated and audited!                        ");
        Console.WriteLine("===============================================================================\n");
        Console.ResetColor();
    }
}
