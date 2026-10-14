using Microsoft.Extensions.DependencyInjection;
using TestLens.Core.Analysis;
using TestLens.Core.Compatibility;
using TestLens.Core.Detection;
using TestLens.Core.Detection.Detectors;
using TestLens.Core.Execution;

namespace TestLens.Core.Extensions;

/// <summary>
/// Методы расширения для регистрации сервисов анализа тестов в DI контейнере.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует основные службы поиска и запуска тестов.
    /// </summary>
    public static IServiceCollection AddTestLensCore(this IServiceCollection services)
    {
        CompatExtensions.ThrowIfNull(services, nameof(services));

        // Регистрация тестовых детекторов
        services.AddSingleton<ITestFrameworkDetector, XUnitTestDetector>();
        services.AddSingleton<ITestFrameworkDetector, NUnitTestDetector>();
        services.AddSingleton<ITestFrameworkDetector, MSTestDetector>();
        services.AddSingleton<ITestDetector, CompositeTestDetector>();

        // Регистрация резолверов и обходчика графа вызовов
        services.AddTransient<ICallerResolver, CallerResolver>();
        services.AddTransient<ICoveringTestFinder, RoslynInvertedCallGraphWalker>();

        // Регистрация раннера
        services.AddTransient<ITestRunner, DotNetCliTestRunner>();

        return services;
    }
}
