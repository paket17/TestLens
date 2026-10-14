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

    public async Task<MethodCoverageResult> FindCoveringTestsAsync(
        Solution solution,
        IMethodSymbol targetMethod,
        CoverageSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        CompatExtensions.ThrowIfNull(solution, nameof(solution));
        CompatExtensions.ThrowIfNull(targetMethod, nameof(targetMethod));
        CompatExtensions.ThrowIfNull(options, nameof(options));

        var stopwatch = Stopwatch.StartNew();
        var diagnostics = new List<string>();

        // Извлекаем метаданные целевого метода
        var targetLocation = targetMethod.Locations.FirstOrDefault(l => l.IsInSource);
        var targetLineSpan = targetLocation?.GetLineSpan();
        var targetFilePath = targetLineSpan?.Path;
        var targetLineNumber = targetLineSpan.HasValue ? targetLineSpan.Value.StartLinePosition.Line + 1 : 0;
        var targetProjectName = targetMethod.ContainingAssembly?.Name ?? string.Empty;

        var targetFqn = targetMethod.ContainingType != null
            ? $"{targetMethod.ContainingType.ToDisplayString()}.{targetMethod.Name}"
            : targetMethod.Name;

        // Словарь найденных тестов: FQN теста -> Список цепочек вызовов
        var foundTests = new Dictionary<string, (TestMethodDescriptor Descriptor, List<CallChain> Chains)>(StringComparer.OrdinalIgnoreCase);

        // Очередь для BFS поиска (обратный поиск от целевого метода)
        var queue = new Queue<PathNode>();

        // Начальный узел (целевой метод, 0 шагов)
        queue.Enqueue(new PathNode
        {
            CurrentMethod = targetMethod,
            StepsFromCurrentToTarget = new List<CallChainStep>(),
            VisitedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { targetMethod }
        });

        while (queue.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                diagnostics.Add("Поиск был прерван по запросу пользователя.");
                break;
            }

            var currentNode = queue.Dequeue();
            var currentDepth = currentNode.StepsFromCurrentToTarget.Count;

            // Если достигли максимальной глубины - глубже не идем
            if (currentDepth >= options.MaxCallDepth)
                continue;

            // Находим всех вызывающих для текущего метода
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
                diagnostics.Add("Поиск был отменен.");
                break;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Ошибка при поиске вызывающих для {Symbol}", currentNode.CurrentMethod.ToDisplayString());
                diagnostics.Add($"Ошибка анализа для {currentNode.CurrentMethod.Name}: {ex.Message}");
                continue;
            }

            foreach (var caller in callers)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                // Предотвращение циклов (рекурсии) в текущей ветке
                if (currentNode.VisitedSymbols.Contains(caller.CallerSymbol))
                    continue;

                // Создаем шаг вызова: Caller -> Callee
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

                // Новая цепочка шагов (добавляем шаг в начало, чтобы порядок был от корня к цели)
                var updatedSteps = new List<CallChainStep> { step };
                updatedSteps.AddRange(currentNode.StepsFromCurrentToTarget);

                // Проверяем, является ли вызывающий метод тестовым методом
                if (_testDetector.TryGetTestDescriptor(caller.CallerSymbol, out var testDescriptor))
                {
                    // Проверка фильтра по проектам
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

                    if (options.MaxTestResults > 0 && foundTests.Count >= options.MaxTestResults)
                        break;

                    // Если вызывающий является тестом, дальше по этой ветке не углубляемся
                    continue;
                }

                // Если метод не является тестом, продолжаем поиск вглубь (BFS)
                var newVisited = new HashSet<ISymbol>(currentNode.VisitedSymbols, SymbolEqualityComparer.Default)
                {
                    caller.CallerSymbol
                };

                queue.Enqueue(new PathNode
                {
                    CurrentMethod = caller.CallerSymbol,
                    StepsFromCurrentToTarget = updatedSteps,
                    VisitedSymbols = new HashSet<ISymbol>(newVisited, SymbolEqualityComparer.Default)
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
