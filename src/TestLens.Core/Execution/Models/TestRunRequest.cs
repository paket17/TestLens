using TestLens.Core.Models;

namespace TestLens.Core.Execution.Models;

/// <summary>
/// Запрос на запуск тестов.
/// </summary>
public sealed record TestRunRequest
{
    /// <summary>
    /// Путь к файлу решения (.sln) или файла проекта (.csproj).
    /// </summary>
    public required string SolutionOrProjectPath { get; init; }

    /// <summary>
    /// Тесты для выполнения.
    /// </summary>
    public required IReadOnlyList<TestMethodDescriptor> TestsToRun { get; init; }

    /// <summary>
    /// Запускать ли тесты без предварительной пересборки (--no-build).
    /// По умолчанию false.
    /// </summary>
    public bool NoBuild { get; init; } = false;

    /// <summary>
    /// Таймаут на выполнение теста.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(3);
}
