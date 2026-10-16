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
        CancellationToken cancellationToken = default)
    {
        return FindCoveringTestsAsync(solution, targetMethod, options, progress, null, cancellationToken);
    }

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
        var context = new TraversalContext(targetMethod, options);

        await TraverseCallGraphAsync(solution, context, progress, onTestFound, cancellationToken)
            .ConfigureAwait(false);

        stopwatch.Stop();
        return BuildCoverageResult(context, stopwatch.Elapsed);
    }

    private async Task TraverseCallGraphAsync(
        Solution solution,
        TraversalContext context,
        IProgress<string>? progress,
        IProgress<CoveringTestMatch>? onTestFound,
        CancellationToken cancellationToken)
    {
        while (context.Queue.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                context.Diagnostics.Add("Search was interrupted by user request or timeout.");
                return;
            }

            var currentNode = context.Queue.Dequeue();
            var currentDepth = currentNode.StepsFromCurrentToTarget.Count;

            if (currentDepth >= context.Options.MaxCallDepth)
                continue;

            context.ExploredCount++;
            if (context.ExploredCount > context.Options.MaxExploredMethods)
            {
                context.Diagnostics.Add($"Explored limit ({context.Options.MaxExploredMethods} methods) reached. Search completed.");
                return;
            }

            ReportProgress(progress, context, currentDepth);

            var callers = await FetchCallersAsync(solution, currentNode.CurrentMethod, context, cancellationToken)
                .ConfigureAwait(false);

            if (callers == null)
                return;

            ProcessCallers(callers, currentNode, context, onTestFound, cancellationToken);

            if (context.HasReachedMaxResults())
                return;
        }
    }

    private async Task<IReadOnlyList<DiscoveredCaller>?> FetchCallersAsync(
        Solution solution,
        IMethodSymbol method,
        TraversalContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _callerResolver.FindCallersAsync(
                solution,
                method,
                context.Options,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            context.Diagnostics.Add("Search was canceled.");
            return null;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Error finding callers for symbol {Symbol}", method.ToDisplayString());
            context.Diagnostics.Add($"Analysis error for {method.Name}: {ex.Message}");
            return [];
        }
    }

    private void ProcessCallers(
        IReadOnlyList<DiscoveredCaller> callers,
        PathNode currentNode,
        TraversalContext context,
        IProgress<CoveringTestMatch>? onTestFound,
        CancellationToken cancellationToken)
    {
        foreach (var caller in callers)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // Защита от циклов в текущей ветке вызовов
            if (currentNode.VisitedSymbols.Contains(caller.CallerSymbol))
                continue;

            var updatedSteps = CreateSteps(currentNode, caller);

            if (TryHandleTestCaller(caller.CallerSymbol, updatedSteps, context, onTestFound))
            {
                if (context.HasReachedMaxResults())
                    break;

                continue;
            }

            EnqueueNonTestCaller(caller.CallerSymbol, updatedSteps, currentNode, context);
        }
    }

    private static List<CallChainStep> CreateSteps(PathNode currentNode, DiscoveredCaller caller)
    {
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

        var steps = new List<CallChainStep>(currentNode.StepsFromCurrentToTarget.Count + 1) { step };
        steps.AddRange(currentNode.StepsFromCurrentToTarget);
        return steps;
    }

    private bool TryHandleTestCaller(
        IMethodSymbol callerSymbol,
        List<CallChainStep> steps,
        TraversalContext context,
        IProgress<CoveringTestMatch>? onTestFound)
    {
        if (!_testDetector.TryGetTestDescriptor(callerSymbol, out var testDescriptor))
            return false;

        if (context.Options.TestProjectFilter is { Count: > 0 } &&
            !context.Options.TestProjectFilter.Contains(testDescriptor.ProjectName, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        context.RegisterFoundTest(testDescriptor, steps, onTestFound);
        return true;
    }

    private static void EnqueueNonTestCaller(
        IMethodSymbol callerSymbol,
        List<CallChainStep> steps,
        PathNode currentNode,
        TraversalContext context)
    {
        var nextDepth = steps.Count;
        if (context.MinDepthByMethod.TryGetValue(callerSymbol, out var prevMinDepth) && nextDepth >= prevMinDepth)
            return;

        context.MinDepthByMethod[callerSymbol] = nextDepth;

        var visitedSymbols = new HashSet<ISymbol>(currentNode.VisitedSymbols, SymbolEqualityComparer.Default)
        {
            callerSymbol
        };

        context.Queue.Enqueue(new PathNode(callerSymbol, steps, visitedSymbols));
    }

    private static void ReportProgress(IProgress<string>? progress, TraversalContext context, int currentDepth) =>
        progress?.Report($"Depth {currentDepth + 1}/{context.Options.MaxCallDepth} | Checked {context.ExploredCount} methods | Found {context.FoundTests.Count} test(s)...");

    private static MethodCoverageResult BuildCoverageResult(
        TraversalContext context,
        TimeSpan elapsed)
    {
        var targetMethod = context.TargetMethod;
        var targetLocation = targetMethod.Locations.FirstOrDefault(l => l.IsInSource);
        var targetLineSpan = targetLocation?.GetLineSpan();
        var targetFilePath = targetLineSpan?.Path;
        var targetLineNumber = targetLineSpan.HasValue ? targetLineSpan.Value.StartLinePosition.Line + 1 : 0;
        var targetProjectName = targetMethod.ContainingAssembly?.Name ?? string.Empty;

        var targetFqn = targetMethod.ContainingType != null
            ? $"{targetMethod.ContainingType.ToDisplayString()}.{targetMethod.Name}"
            : targetMethod.Name;

        var matches = context.FoundTests.Values
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
            ElapsedTime = elapsed,
            Diagnostics = context.Diagnostics
        };
    }

    private sealed class TraversalContext(IMethodSymbol targetMethod, CoverageSearchOptions options)
    {
        public IMethodSymbol TargetMethod { get; } = targetMethod;

        public CoverageSearchOptions Options { get; } = options;

        public List<string> Diagnostics { get; } = [];

        public Dictionary<string, (TestMethodDescriptor Descriptor, List<CallChain> Chains)> FoundTests { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<ISymbol, int> MinDepthByMethod { get; } =
            new(SymbolEqualityComparer.Default) { [targetMethod] = 0 };

        public Queue<PathNode> Queue { get; } =
            new([new PathNode(targetMethod, [], new HashSet<ISymbol>(SymbolEqualityComparer.Default) { targetMethod })]);

        public int ExploredCount { get; set; }

        public bool HasReachedMaxResults() =>
            Options.MaxTestResults > 0 && FoundTests.Count >= Options.MaxTestResults;

        public void RegisterFoundTest(
            TestMethodDescriptor descriptor,
            List<CallChainStep> steps,
            IProgress<CoveringTestMatch>? onTestFound)
        {
            var callChain = new CallChain
            {
                TestMethod = descriptor,
                Steps = steps
            };

            if (!FoundTests.TryGetValue(descriptor.FullyQualifiedName, out var entry))
            {
                entry = (descriptor, []);
                FoundTests[descriptor.FullyQualifiedName] = entry;
            }

            entry.Chains.Add(callChain);

            onTestFound?.Report(new CoveringTestMatch
            {
                TestMethod = descriptor,
                CallChains = [.. entry.Chains]
            });
        }
    }

    private sealed record class PathNode(
        IMethodSymbol CurrentMethod,
        List<CallChainStep> StepsFromCurrentToTarget,
        HashSet<ISymbol> VisitedSymbols);
}
