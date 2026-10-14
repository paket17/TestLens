using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using TestLens.Core.Compatibility;
using TestLens.Core.Detection;
using TestLens.Core.Models;

namespace TestLens.Core.Analysis;

/// <summary>
/// Реализация поиска тестов на основе обратного обхода графа вызовов (Roslyn Inverted Call Graph).
/// </summary>
public sealed class RoslynInvertedCallGraphWalker(
    ICallerResolver callerResolver,
    ITestDetector testDetector,
    ILogger<RoslynInvertedCallGraphWalker>? logger = null) : ICoveringTestFinder
{
    private readonly ICallerResolver _callerResolver = 
        callerResolver ?? throw new ArgumentNullException(nameof(callerResolver));
    private readonly ITestDetector _testDetector = 
        testDetector ?? throw new ArgumentNullException(nameof(testDetector));

    public Task<MethodCoverageResult> FindCoveringTestsAsync(
        Solution solution,
        IMethodSymbol targetMethod,
        CoverageSearchOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        FindCoveringTestsAsync(solution, targetMethod, options, progress, null, cancellationToken);

    public async Task<MethodCoverageResult> FindCoveringTestsAsync(
        Solution solution,
        IMethodSymbol targetMethod,
        CoverageSearchOptions options,
        IProgress<string>? progress,
        IProgress<CoveringTestMatch>? onTestFound,
        CancellationToken cancellationToken = default)
    {
        CompatExtensions.ThrowIfNull(solution, nameof(solution));
        CompatExtensions.ThrowIfNull(targetMethod, nameof(targetMethod));
        CompatExtensions.ThrowIfNull(options, nameof(options));

        var stopwatch = Stopwatch.StartNew();
        var diagnostics = new List<string>();

        // Target method metadata
        var targetLocation = targetMethod.Locations.FirstOrDefault(l => l.IsInSource);
        var targetLineSpan = targetLocation?.GetLineSpan();
        var targetFilePath = targetLineSpan?.Path;
        var targetLineNumber = targetLineSpan.HasValue ? targetLineSpan.Value.StartLinePosition.Line + 1 : 0;
        var targetProjectName = targetMethod.ContainingAssembly?.Name ?? string.Empty;

        var targetFqn = targetMethod.ContainingType != null
            ? $"{targetMethod.ContainingType.ToDisplayString()}.{targetMethod.Name}"
            : targetMethod.Name;

        // Discovered tests: FQN -> (Descriptor, Chains)
        var foundTests = new Dictionary<string, (TestMethodDescriptor Descriptor, List<CallChain> Chains)>(StringComparer.OrdinalIgnoreCase);

        // Tracks minimum depth each method was visited to eliminate exponential graph explosion
        var minDepthByMethod = new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default)
        {
            [targetMethod] = 0
        };

        // BFS queue for reverse call graph traversal
        var queue = new Queue<PathNode>();
        queue.Enqueue(new PathNode
        {
            CurrentMethod = targetMethod,
            StepsFromCurrentToTarget = new List<CallChainStep>(),
            VisitedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { targetMethod }
        });

        var exploredCount = 0;

        while (queue.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                diagnostics.Add("Search was interrupted by user request or timeout.");
                break;
            }

            var currentNode = queue.Dequeue();
            var currentDepth = currentNode.StepsFromCurrentToTarget.Count;

            if (currentDepth >= options.MaxCallDepth)
                continue;

            exploredCount++;
            if (exploredCount > options.MaxExploredMethods)
            {
                diagnostics.Add($"Explored limit ({options.MaxExploredMethods} methods) reached. Search completed.");
                break;
            }

            progress?.Report($"Depth {currentDepth + 1}/{options.MaxCallDepth} | Checked {exploredCount} methods | Found {foundTests.Count} test(s)...");

            IReadOnlyList<DiscoveredCaller> callers;
            try
            {
                callers = await _callerResolver.FindCallersAsync(
                    solution,
                    currentNode.CurrentMethod,
                    options,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                diagnostics.Add("Search was canceled.");
                break;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error finding callers for symbol {Symbol}", currentNode.CurrentMethod.ToDisplayString());
                diagnostics.Add($"Analysis error for {currentNode.CurrentMethod.Name}: {ex.Message}");
                continue;
            }

            foreach (var caller in callers)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                // Cycle prevention in current branch
                if (currentNode.VisitedSymbols.Contains(caller.CallerSymbol))
                    continue;

                var step = new CallChainStep
                {
                    CallerName = caller.CallerSymbol.Name,
                    CallerFullyQualifiedName = caller.CallerSymbol.ToDisplayString(),
                    CalleeName = caller.CalleeSymbol.Name,
                    CalleeFullyQualifiedName = caller.CalleeSymbol.ToDisplayString(),
                    FilePath = caller.FilePath,
                    LineNumber = caller.LineNumber,
                    CodeSnippet = caller.CodeSnippet,
                    StepKind = caller.StepKind
                };

                var updatedSteps = new List<CallChainStep> { step };
                updatedSteps.AddRange(currentNode.StepsFromCurrentToTarget);

                // Check if caller is a test method
                if (_testDetector.TryGetTestDescriptor(caller.CallerSymbol, out var testDescriptor))
                {
                    if (options.TestProjectFilter != null &&
                        options.TestProjectFilter.Count > 0 &&
                        !options.TestProjectFilter.Contains(testDescriptor.ProjectName, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var callChain = new CallChain
                    {
                        TestMethod = testDescriptor,
                        Steps = updatedSteps
                    };

                    if (!foundTests.TryGetValue(testDescriptor.FullyQualifiedName, out var entry))
                    {
                        entry = (testDescriptor, new List<CallChain>());
                        foundTests[testDescriptor.FullyQualifiedName] = entry;
                    }

                    entry.Chains.Add(callChain);

                    onTestFound?.Report(new CoveringTestMatch
                    {
                        TestMethod = testDescriptor,
                        CallChains = new List<CallChain>(entry.Chains)
                    });

                    if (options.MaxTestResults > 0 && foundTests.Count >= options.MaxTestResults)
                        break;

                    continue;
                }

                // If non-test, check if we've already reached this method at a shorter or equal depth
                var nextDepth = currentDepth + 1;
                if (minDepthByMethod.TryGetValue(caller.CallerSymbol, out var prevMinDepth) && nextDepth >= prevMinDepth)
                    continue;

                minDepthByMethod[caller.CallerSymbol] = nextDepth;

                var newVisited = new HashSet<ISymbol>(currentNode.VisitedSymbols, SymbolEqualityComparer.Default)
                {
                    caller.CallerSymbol
                };

                queue.Enqueue(new PathNode
                {
                    CurrentMethod = caller.CallerSymbol,
                    StepsFromCurrentToTarget = updatedSteps,
                    VisitedSymbols = newVisited
                });
            }

            if (options.MaxTestResults > 0 && foundTests.Count >= options.MaxTestResults)
                break;
        }

        stopwatch.Stop();

        // Формируем результирующий список тестов, отсортированный по минимальной глубине
        var matches = foundTests.Values
            .Select(v => new CoveringTestMatch
            {
                TestMethod = v.Descriptor,
                CallChains = [.. v.Chains.OrderBy(c => c.Depth)]
            })
            .OrderBy(m => m.MinDepth)
            .ThenBy(m => m.TestMethod.MethodName)
            .ToList();

        return new MethodCoverageResult
        {
            TargetMethodName = targetMethod.Name,
            TargetMethodFullyQualifiedName = targetFqn,
            TargetProjectName = targetProjectName,
            TargetFilePath = targetFilePath,
            TargetLineNumber = targetLineNumber,
            CoveringTests = matches,
            ElapsedTime = stopwatch.Elapsed,
            Diagnostics = diagnostics
        };
    }

    private sealed class PathNode
    {
        public required IMethodSymbol CurrentMethod { get; init; }
        public required List<CallChainStep> StepsFromCurrentToTarget { get; init; }
        public required HashSet<ISymbol> VisitedSymbols { get; init; }
    }
}
