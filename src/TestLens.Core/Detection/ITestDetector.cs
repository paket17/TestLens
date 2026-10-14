using Microsoft.CodeAnalysis;
using System.Diagnostics.CodeAnalysis;
using TestLens.Core.Models;

namespace TestLens.Core.Detection;

/// <summary>
/// Агрегирующий сервис обнаружения тестов в коде.
/// </summary>
public interface ITestDetector
{
    /// <summary>
    /// Проверяет, является ли метод тестовым, и формирует его дескриптор.
    /// </summary>
    bool TryGetTestDescriptor(
        IMethodSymbol methodSymbol,
        [NotNullWhen(true)] out TestMethodDescriptor? testDescriptor);
}
