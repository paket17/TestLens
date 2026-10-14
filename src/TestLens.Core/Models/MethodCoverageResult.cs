namespace TestLens.Core.Models;

/// <summary>
/// Полный отчет о покрытии метода тестами.
/// </summary>
public sealed record MethodCoverageResult
{
    /// <summary>
    /// Имя целевого метода.
    /// </summary>
    public required string TargetMethodName { get; init; }

    /// <summary>
    /// Полное имя целевого метода (Namespace.ClassName.MethodName).
    /// </summary>
    public required string TargetMethodFullyQualifiedName { get; init; }

    /// <summary>
    /// Проект, в котором находится целевой метод.
    /// </summary>
    public required string TargetProjectName { get; init; }

    /// <summary>
    /// Путь к файлу целевого метода.
    /// </summary>
    public string? TargetFilePath { get; init; }

    /// <summary>
    /// Номер строки целевого метода.
    /// </summary>
    public int TargetLineNumber { get; init; }

    /// <summary>
    /// Список найденных тестов, покрывающих метод.
    /// </summary>
    public required IReadOnlyList<CoveringTestMatch> CoveringTests { get; init; }

    /// <summary>
    /// Флаг: покрыт ли метод хотя бы одним тестом.
    /// </summary>
    public bool IsCovered => 
        CoveringTests.Count > 0;

    /// <summary>
    /// Общее количество уникальных тестов, покрывающих метод.
    /// </summary>
    public int TotalCoveringTestsCount => 
        CoveringTests.Count;

    /// <summary>
    /// Время, затраченное на анализ.
    /// </summary>
    public TimeSpan ElapsedTime { get; init; }

    /// <summary>
    /// Список предупреждений или диагностических сообщений, возникших при анализе.
    /// </summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}
