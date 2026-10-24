using Microsoft.CodeAnalysis;

namespace TestLens.Core.Analysis;

/// <summary>
/// Резолвер вызывающих методов (Callers) с поддержкой интерфейсов и виртуального полиморфизма.
/// </summary>
public interface ICallerResolver
{
    /// <summary>
    /// Находит всех вызывающих для указанного метода в рамках решения (Solution).
    /// </summary>
    Task<IReadOnlyList<DiscoveredCaller>> FindCallersAsync(
        Solution solution,
        IMethodSymbol targetMethod,
        CoverageSearchOptions options,
        CancellationToken cancellationToken = default);
}
