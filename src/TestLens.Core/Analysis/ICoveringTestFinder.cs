using TestLens.Core.Models;
using Microsoft.CodeAnalysis;

namespace TestLens.Core.Analysis;

/// <summary>
/// Главный сервис поиска тестов, покрывающих метод.
/// </summary>
public interface ICoveringTestFinder
{
    /// <summary>
    /// Выполняет поиск всех тестов, вызывающих указанный метод (напрямую или косвенно).
    /// </summary>
    /// <param name="solution">Решение Roslyn со всеми проектами.</param>
    /// <param name="targetMethod">Символ целевого метода.</param>
    /// <param name="options">Параметры поиска.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат покрытия метода со списком найденных тестов и цепочками вызовов.</returns>
    Task<MethodCoverageResult> FindCoveringTestsAsync(
        Solution solution,
        IMethodSymbol targetMethod,
        CoverageSearchOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Выполняет поиск тестов с потоковым уведомлением о каждом найденном тесте.
    /// </summary>
    Task<MethodCoverageResult> FindCoveringTestsAsync(
        Solution solution,
        IMethodSymbol targetMethod,
        CoverageSearchOptions options,
        IProgress<string>? progress,
        IProgress<CoveringTestMatch>? onTestFound,
        CancellationToken cancellationToken = default);
}
