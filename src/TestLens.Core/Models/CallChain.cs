namespace TestLens.Core.Models;

/// <summary>
/// Полная цепочка вызова от корневого теста до целевого метода.
/// </summary>
public sealed record CallChain
{
    /// <summary>
    /// Тест, с которого начинается цепочка.
    /// </summary>
    public required TestMethodDescriptor TestMethod { get; init; }

    /// <summary>
    /// Последовательность шагов вызова от теста к целевому методу.
    /// </summary>
    public required IReadOnlyList<CallChainStep> Steps { get; init; }

    /// <summary>
    /// Глубина цепочки (количество шагов вызова).
    /// Прямой вызов из теста = 1.
    /// </summary>
    public int Depth => Steps.Count;

    /// <summary>
    /// Является ли вызов прямым (тест вызывает метод напрямую).
    /// </summary>
    public bool IsDirectCall => Steps.Count == 1;

    public override string ToString()
    {
        var stepsStr = string.Join(" -> ", Steps.Select(s => s.CallerName));
        return $"{TestMethod.MethodName} (Depth: {Depth}): {stepsStr} -> [Target]";
    }
}
