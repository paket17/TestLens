using Microsoft.CodeAnalysis;
using TestLens.Core.Models;

namespace TestLens.Core.Analysis;

/// <summary>
/// Информация об обнаруженном вызове метода.
/// </summary>
public sealed record DiscoveredCaller
{
    /// <summary>
    /// Метод, осуществляющий вызов.
    /// </summary>
    public required IMethodSymbol CallerSymbol { get; init; }

    /// <summary>
    /// Вызываемый метод (может быть как сам целевой метод, так и интерфейс/базовый метод).
    /// </summary>
    public required IMethodSymbol CalleeSymbol { get; init; }

    /// <summary>
    /// Путь к файлу с вызовом.
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// Номер строки (1-based).
    /// </summary>
    public int LineNumber { get; init; }

    /// <summary>
    /// Фрагмент кода или синтаксис места вызова.
    /// </summary>
    public string? CodeSnippet { get; init; }

    /// <summary>
    /// Характер вызова (прямой, через интерфейс, override и т.д.).
    /// </summary>
    public CallChainStepKind StepKind { get; init; } = CallChainStepKind.DirectCall;
}
