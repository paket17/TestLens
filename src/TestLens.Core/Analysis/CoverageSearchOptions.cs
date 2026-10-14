namespace TestLens.Core.Analysis;

/// <summary>
/// Параметры поиска тестов для метода.
/// </summary>
public sealed record CoverageSearchOptions
{
    /// <summary>
    /// Максимальная глубина цепочки вызовов при обратном обходе.
    /// По умолчанию 10.
    /// </summary>
    public int MaxCallDepth { get; init; } = 10;

    /// <summary>
    /// Разрешать ли вызовы через интерфейсы (если вызывается метод интерфейса, искать вызовы метода реализации).
    /// По умолчанию true.
    /// </summary>
    public bool FollowInterfaces { get; init; } = true;

    /// <summary>
    /// Разрешать ли вызовы виртуальных/абстрактных методов базовых классов.
    /// По умолчанию true.
    /// </summary>
    public bool FollowOverrides { get; init; } = true;

    /// <summary>
    /// Максимальное количество уникальных тестов для возврата (0 = без ограничений).
    /// </summary>
    public int MaxTestResults { get; init; } = 0;

    /// <summary>
    /// Фильтр по именам проектов, в которых искать тесты (null = искать по всему решению).
    /// </summary>
    public IReadOnlyCollection<string>? TestProjectFilter { get; init; }
}
