using Microsoft.CodeAnalysis;
using System.Diagnostics.CodeAnalysis;
using TestLens.Core.Models;

namespace TestLens.Core.Detection;

/// <summary>
/// Композитный детектор тестов, объединяющий детекторы различных фреймворков.
/// </summary>
public sealed class CompositeTestDetector(
    IEnumerable<ITestFrameworkDetector> detectors) : ITestDetector
{
    private readonly IReadOnlyList<ITestFrameworkDetector> _detectors = 
        detectors?.ToList() ?? throw new ArgumentNullException(nameof(detectors));

    public bool TryGetTestDescriptor(
        IMethodSymbol methodSymbol,
        [NotNullWhen(true)] out TestMethodDescriptor? testDescriptor)
    {
        testDescriptor = null;

        if (methodSymbol == null)
            return false;

        foreach (var detector in _detectors)
        {
            if (!detector.IsTestMethod(methodSymbol, out var displayName))
                continue;

            var location = methodSymbol.Locations.FirstOrDefault(l => l.IsInSource);
            var lineSpan = location?.GetLineSpan();
            var filePath = lineSpan?.Path;
            var lineNumber = lineSpan.HasValue ? lineSpan.Value.StartLinePosition.Line + 1 : 0;

            var containingType = methodSymbol.ContainingType;
            var className = containingType?.Name ?? string.Empty;
            var ns = containingType?.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            var projectName = methodSymbol.ContainingAssembly?.Name ?? string.Empty;

            var fqn = containingType != null
                ? $"{containingType.ToDisplayString()}.{methodSymbol.Name}"
                : methodSymbol.Name;

            testDescriptor = new TestMethodDescriptor
            {
                FullyQualifiedName = fqn,
                MethodName = methodSymbol.Name,
                ClassName = className,
                Namespace = ns,
                ProjectName = projectName,
                FilePath = filePath,
                LineNumber = lineNumber,
                Framework = detector.SupportedFramework,
                DisplayName = displayName
            };

            return true;
        }

        return false;
    }
}
