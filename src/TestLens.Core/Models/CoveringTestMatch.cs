namespace TestLens.Core.Models;

/// <summary>
/// Информация о тесте, покрывающем целевой метод, со всеми найденными цепочками вызова.
/// Один тест может вызывать целевой метод несколькими разными путями.
/// </summary>
public sealed record CoveringTestMatch
{
    /// <summary>
    /// Описание тестового метода.
    /// </summary>
    public required TestMethodDescriptor TestMethod { get; init; }

    /// <summary>
    /// Все найденные цепочки вызовов от этого теста к целевому методу.
    /// </summary>
    public required IReadOnlyList<CallChain> CallChains { get; init; }

    /// <summary>
    /// Минимальная глубина вызова среди всех найденных цепочек.
    /// 1 = вызывается напрямую.
    /// </summary>
    public int MinDepth => 
        CallChains.Count > 0 ? CallChains.Min(c => c.Depth) : 0;

    /// <summary>
    /// Есть ли среди цепочек прямой вызов.
    /// </summary>
    public bool HasDirectCall => 
        CallChains.Any(c => c.IsDirectCall);
}
