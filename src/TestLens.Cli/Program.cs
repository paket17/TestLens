using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using System.Text.Json;
using TestLens.Core;
using TestLens.Core.Analysis;
using TestLens.Core.Execution;
using TestLens.Core.Execution.Models;
using TestLens.Core.Models;

namespace TestLens.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        var solutionPath = GetArgument(args, "--solution", "-s");
        var methodName = GetArgument(args, "--method", "-m");
        var fileName = GetArgument(args, "--file", "-f");
        var depth = int.TryParse(GetArgument(args, "--depth", "-d"), out var d) ? d : 5;
        var run = args.Contains("--run") || args.Contains("-r");
        var json = args.Contains("--json");

        if (string.IsNullOrWhiteSpace(solutionPath) || string.IsNullOrWhiteSpace(methodName))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: The --solution (-s) and --method (-m) options are required.");
            Console.ResetColor();
            PrintUsage();
            return 1;
        }

        var solutionFile = new FileInfo(solutionPath);
        if (!solutionFile.Exists)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Solution file not found: {solutionFile.FullName}");
            Console.ResetColor();
            return 1;
        }

        await RunAsync(solutionFile, methodName, fileName, depth, run, json);
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
TestLens CLI - Covering test discovery by call chain in .NET

Usage:
  dotnet run --project src/TestLens.Cli -- [options]

Options:
  -s, --solution <path>   Path to the solution file (.sln) [required]
  -m, --method <name>     Name of the target method [required]
  -f, --file <name>       Source file name filter (for method overloads)
  -d, --depth <int>       Maximum call chain depth (default: 5)
  -r, --run               Automatically run discovered covering tests
  --json                  Output in JSON format
  -h, --help              Show help information

Examples:
  dotnet run --project src/TestLens.Cli -- -s TestLens.sln -m CreateWorkspaceFromSource -r
  dotnet run --project src/TestLens.Cli -- -s TestLens.sln -m FindCoveringTestsAsync
""");
    }

    private static string? GetArgument(string[] args, params string[] aliases)
    {
        for (var i = 0; i < args.Length; i++)
        {
            foreach (var alias in aliases)
            {
                if (string.Equals(args[i], alias, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    return args[i + 1];
            }
        }

        return null;
    }

    private static async Task RunAsync(
        FileInfo solutionFile,
        string methodName,
        string? fileName,
        int depth,
        bool runTests,
        bool outputJson)
    {
        EnsureMSBuildRegistered();

        var testFinder = TestLensServices.CreateFinder();
        var testRunner = TestLensServices.CreateTestRunner();

        if (!outputJson)
            PrintBanner(solutionFile.Name);

        var solution = await OpenSolutionAsync(solutionFile.FullName, outputJson);

        if (!outputJson)
            Console.WriteLine($"Searching for method '{methodName}' in solution...");

        var candidateMethods = await FindCandidateMethodsAsync(solution, methodName, fileName);
        if (candidateMethods.Count == 0)
        {
            HandleMethodNotFound(methodName, outputJson);
            return;
        }

        var targetMethod = SelectTargetMethod(candidateMethods, outputJson);

        var options = new CoverageSearchOptions
        {
            MaxCallDepth = depth,
            FollowInterfaces = true,
            FollowOverrides = true
        };

        var progress = outputJson ? null : new Progress<string>(msg => Console.WriteLine($"[TestLens] {msg}"));
        var result = await testFinder.FindCoveringTestsAsync(solution, targetMethod, options, progress);

        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        PrintCoverageResult(result);

        if (runTests && result.IsCovered)
            await RunCoveringTestsAsync(testRunner, solutionFile.FullName, result.CoveringTests);
    }

    private static void EnsureMSBuildRegistered()
    {
        if (MSBuildLocator.IsRegistered)
            return;

        var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
        if (instances.Length > 0)
            MSBuildLocator.RegisterInstance(instances.OrderByDescending(i => i.Version).First());
        else
            MSBuildLocator.RegisterDefaults();
    }

    private static void PrintBanner(string solutionFileName)
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║        TestLens CLI - Roslyn Inverted Call Graph Test Finder           ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine($"Loading solution: {solutionFileName}...");
    }

    private static async Task<Solution> OpenSolutionAsync(string solutionFilePath, bool outputJson)
    {
        using var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, e) =>
        {
            if (!outputJson && e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[MSBuild Diagnostic] {e.Diagnostic.Message}");
                Console.ResetColor();
            }
        };

        return await workspace.OpenSolutionAsync(solutionFilePath);
    }

    private static async Task<List<(IMethodSymbol Symbol, Document Document)>> FindCandidateMethodsAsync(
        Solution solution,
        string methodName,
        string? fileName)
    {
        var candidates = new List<(IMethodSymbol Symbol, Document Document)>();

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (fileName != null && !document.Name.Contains(fileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!document.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                await CollectMethodsFromDocumentAsync(document, methodName, candidates);
            }
        }

        return candidates;
    }

    private static async Task CollectMethodsFromDocumentAsync(
        Document document,
        string methodName,
        List<(IMethodSymbol Symbol, Document Document)> candidates)
    {
        var syntaxRoot = await document.GetSyntaxRootAsync();
        if (syntaxRoot == null)
            return;

        var semanticModel = await document.GetSemanticModelAsync();
        if (semanticModel == null)
            return;

        var methodDeclarations = syntaxRoot.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(m => string.Equals(m.Identifier.Text, methodName, StringComparison.OrdinalIgnoreCase));

        foreach (var methodDecl in methodDeclarations)
        {
            var symbol = semanticModel.GetDeclaredSymbol(methodDecl);
            if (symbol is IMethodSymbol methodSymbol)
                candidates.Add((methodSymbol, document));
        }
    }

    private static void HandleMethodNotFound(string methodName, bool outputJson)
    {
        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { error = $"Method '{methodName}' not found in solution." }));
            return;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Method named '{methodName}' not found in code.");
        Console.ResetColor();
    }

    private static IMethodSymbol SelectTargetMethod(
        IReadOnlyList<(IMethodSymbol Symbol, Document Document)> candidateMethods,
        bool outputJson)
    {
        var targetEntry = candidateMethods[0];
        if (candidateMethods.Count > 1 && !outputJson)
        {
            Console.WriteLine($"Methods found: {candidateMethods.Count}. Using first match:");
            Console.WriteLine($"  -> {targetEntry.Symbol.ToDisplayString()} ({targetEntry.Document.FilePath})");
        }

        return targetEntry.Symbol;
    }

    private static void PrintCoverageResult(MethodCoverageResult result)
    {
        Console.WriteLine();
        Console.WriteLine("------------------------------------------------------------------------");
        Console.Write("Target method: ");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(result.TargetMethodFullyQualifiedName);
        Console.ResetColor();

        Console.Write("File: ");
        Console.WriteLine($"{result.TargetFilePath}:{result.TargetLineNumber}");

        Console.Write("Coverage status: ");
        if (result.IsCovered)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"COVERED (found {result.TotalCoveringTestsCount} test(s))");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("NOT COVERED BY TESTS (0 calls from tests found)");
        }

        Console.ResetColor();
        Console.WriteLine($"Analysis time: {result.ElapsedTime.TotalMilliseconds:F1} ms");
        Console.WriteLine("------------------------------------------------------------------------");

        if (result.IsCovered)
            PrintCoveringTests(result.CoveringTests);
    }

    private static void PrintCoveringTests(IReadOnlyList<CoveringTestMatch> coveringTests)
    {
        Console.WriteLine();
        Console.WriteLine("Covering tests and call chains:");

        for (var i = 0; i < coveringTests.Count; i++)
        {
            var match = coveringTests[i];
            var test = match.TestMethod;

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"[{i + 1}] [{test.Framework}] ");
            Console.ResetColor();
            Console.Write($"{test.FullyQualifiedName} ");

            Console.ForegroundColor = match.HasDirectCall ? ConsoleColor.Green : ConsoleColor.DarkYellow;
            Console.WriteLine($"({(match.HasDirectCall ? "Direct call" : $"Indirect call, depth: {match.MinDepth}")})");
            Console.ResetColor();

            if (!string.IsNullOrEmpty(test.DisplayName))
                Console.WriteLine($"    Display: \"{test.DisplayName}\"");

            foreach (var chain in match.CallChains)
                PrintCallChain(chain);
        }
    }

    private static void PrintCallChain(CallChain chain)
    {
        Console.WriteLine("    Call chain:");
        for (var s = 0; s < chain.Steps.Count; s++)
        {
            var step = chain.Steps[s];
            var prefix = s == chain.Steps.Count - 1 ? "    └─ " : "    ├─ ";
            var stepKindStr = step.StepKind != CallChainStepKind.DirectCall ? $" [{step.StepKind}]" : string.Empty;
            var snippet = !string.IsNullOrEmpty(step.CodeSnippet) ? $" : \"{step.CodeSnippet}\"" : string.Empty;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(prefix);
            Console.ResetColor();
            Console.WriteLine($"{step.CallerName} -> {step.CalleeName}{stepKindStr} (Line {step.LineNumber}){snippet}");
        }
    }

    private static async Task RunCoveringTestsAsync(
        ITestRunner testRunner,
        string solutionPath,
        IReadOnlyList<CoveringTestMatch> coveringTests)
    {
        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════════════════════════════════════");
        Console.WriteLine("Running discovered tests via 'dotnet test'...");
        Console.WriteLine("════════════════════════════════════════════════════════════════════════");

        var runRequest = new TestRunRequest
        {
            SolutionOrProjectPath = solutionPath,
            TestsToRun = coveringTests.Select(t => t.TestMethod).ToList()
        };

        var runResult = await testRunner.RunTestsAsync(runRequest, line =>
        {
            Console.WriteLine($"  [Test Output] {line}");
        });

        Console.WriteLine();
        if (runResult.Success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ ALL TESTS PASSED! (Passed: {runResult.PassedCount}, Duration: {runResult.Duration.TotalSeconds:F2}s)");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ FAILED: Tests failed! (Failed: {runResult.FailedCount}, Passed: {runResult.PassedCount})");
            if (!string.IsNullOrEmpty(runResult.ErrorMessage))
                Console.WriteLine($"  {runResult.ErrorMessage}");
        }

        Console.ResetColor();
    }
}
