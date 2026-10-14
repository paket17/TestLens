namespace TestLens.Core.Execution.Models;

/// <summary>
/// Результат прогона тестов.
/// </summary>
public sealed record TestRunResult
{
    /// <summary>
    /// Успешен ли был общий запуск (все выбранные тесты завершились успешно).
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Общее количество пройденных тестов.
    /// </summary>
    public int PassedCount { get; init; }

    /// <summary>
    /// Общее количество проваленных тестов.
    /// </summary>
    public int FailedCount { get; init; }

    /// <summary>
    /// Общее количество пропущенных тестов.
    /// </summary>
    public int SkippedCount { get; init; }

    /// <summary>
    /// Время выполнения.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Полный текстовый вывод консоли (stdout + stderr).
    /// </summary>
    public string StandardOutput { get; init; } = string.Empty;

    /// <summary>
    /// Сообщения об ошибках при запуске процесса (если возникли).
    /// </summary>
    public string? ErrorMessage { get; init; }
}
