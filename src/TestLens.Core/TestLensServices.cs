using TestLens.Core.Analysis;
using TestLens.Core.Detection;
using TestLens.Core.Detection.Detectors;
using TestLens.Core.Execution;

namespace TestLens.Core;

/// <summary>
/// Фабрика для прямого создания сервисов TestLens (Pure DI).
/// </summary>
public static class TestLensServices
{
    /// <summary>
    /// Создает экземпляр обходчика графа вызовов для поиска покрывающих тестов.
    /// </summary>
    public static ICoveringTestFinder CreateFinder(
        ICallerResolver? callerResolver = null,
        ITestDetector? testDetector = null)
    {
        return new RoslynInvertedCallGraphWalker(
            callerResolver ?? CreateCallerResolver(),
            testDetector ?? CreateTestDetector());
    }

    /// <summary>
    /// Создает резолвер вызывающих методов Roslyn.
    /// </summary>
    public static ICallerResolver CreateCallerResolver() =>
        new CallerResolver();

    /// <summary>
    /// Создает композитный детектор тестовых методов со всеми поддерживаемыми фреймворками (xUnit, NUnit, MSTest).
    /// </summary>
    public static ITestDetector CreateTestDetector() =>
        new CompositeTestDetector(
            new XUnitTestDetector(),
            new NUnitTestDetector(),
            new MSTestDetector());

    /// <summary>
    /// Создает раннер выполнения тестов через CLI dotnet test.
    /// </summary>
    public static ITestRunner CreateTestRunner() =>
        new DotNetCliTestRunner();
}
