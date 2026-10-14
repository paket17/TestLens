namespace TestLens.Core.Models;

/// <summary>
/// Вид вызова в цепочке вызовов.
/// </summary>
public enum CallChainStepKind
{
    /// <summary>
    /// Прямой вызов конкретного метода.
    /// </summary>
    DirectCall = 0,

    /// <summary>
    /// Вызов через метод интерфейса (интерфейс -> реализация).
    /// </summary>
    InterfaceDispatch = 1,

    /// <summary>
    /// Вызов через виртуальный или абстрактный метод базового класса.
    /// </summary>
    VirtualOverrideDispatch = 2,

    /// <summary>
    /// Вызов через делегат или лямбда-выражение.
    /// </summary>
    DelegateOrLambda = 3
}
