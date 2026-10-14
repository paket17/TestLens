namespace TestLens.Core.Models;

/// <summary>
/// Один шаг в цепочке вызовов (от вызывающего к вызываемому).
/// </summary>
public sealed record CallChainStep
{
    /// <summary>
    /// Имя вызывающего метода/символа.
    /// </summary>
    public required string CallerName { get; init; }

    /// <summary>
    /// Полное имя вызывающего метода.
    /// </summary>
    public required string CallerFullyQualifiedName { get; init; }

    /// <summary>
    /// Имя вызываемого метода/символа.
    /// </summary>
    public required string CalleeName { get; init; }

    /// <summary>
    /// Полное имя вызываемого метода.
    /// </summary>
    public required string CalleeFullyQualifiedName { get; init; }

    /// <summary>
    /// Путь к файлу, в котором происходит вызов.
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// Номер строки вызова (1-based).
    /// </summary>
    public int LineNumber { get; init; }

    /// <summary>
    /// Фрагмент кода или синтаксис вызова.
    /// </summary>
    public string? CodeSnippet { get; init; }

    /// <summary>
    /// Тип вызова (прямой, виртуальный, интерфейс и т.д.).
    /// </summary>
    public CallChainStepKind StepKind { get; init; } = CallChainStepKind.DirectCall;

    public override string ToString() =>
        $"{CallerName} -> {CalleeName} (Line {LineNumber}, {StepKind})";
}
