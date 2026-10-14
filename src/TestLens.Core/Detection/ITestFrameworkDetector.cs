using Microsoft.CodeAnalysis;
using TestLens.Core.Models;

namespace TestLens.Core.Detection;

/// <summary>
/// Интерфейс детектора тестового фреймворка.
/// </summary>
public interface ITestFrameworkDetector
{
    /// <summary>
    /// Идентифицируемый фреймворк.
    /// </summary>
    TestFrameworkType SupportedFramework { get; }

    /// <summary>
    /// Проверяет, является ли переданный метод тестом.
    /// </summary>
    /// <param name="methodSymbol">Символ метода Roslyn.</param>
    /// <param name="testDisplayName">Отображаемое имя теста (если задано в атрибуте).</param>
    /// <returns>true, если метод признан тестом данного фреймворка.</returns>
    bool IsTestMethod(IMethodSymbol methodSymbol, out string? testDisplayName);
}
