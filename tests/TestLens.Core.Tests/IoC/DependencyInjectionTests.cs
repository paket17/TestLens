using System;
using System.Collections.Generic;
using TestLens.Core.Analysis;
using TestLens.Core.Detection;
using TestLens.Core.Detection.Detectors;
using TestLens.Core.Execution;
using TestLens.Core.IoC;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TestLens.Core.Tests.IoC;

public class DependencyInjectionTests
{
    [Fact]
    public void TestLensServices_CreateServiceProvider_ResolvesAllCoreServices()
    {
        // Act
        var provider = TestLensServices.CreateServiceProvider();

        // Assert
        Assert.NotNull(provider);

        var testFinder = provider.GetService(typeof(ICoveringTestFinder)) as ICoveringTestFinder;
        Assert.NotNull(testFinder);

        var testRunner = provider.GetService(typeof(ITestRunner)) as ITestRunner;
        Assert.NotNull(testRunner);

        var callerResolver = provider.GetService(typeof(ICallerResolver)) as ICallerResolver;
        Assert.NotNull(callerResolver);

        var testDetector = provider.GetService(typeof(ITestDetector)) as ITestDetector;
        Assert.NotNull(testDetector);
    }

    [Fact]
    public void CoreServiceProvider_ResolvesIEnumerableOfDetectors()
    {
        // Arrange
        var provider = TestLensServices.CreateServiceProvider();

        // Act
        var detectors = provider.GetService(typeof(IEnumerable<ITestFrameworkDetector>)) as IEnumerable<ITestFrameworkDetector>;

        // Assert
        Assert.NotNull(detectors);
        var detectorList = new List<ITestFrameworkDetector>(detectors!);
        Assert.Equal(3, detectorList.Count);
    }

    [Fact]
    public void CoreServiceProvider_SingletonLifetime_ReturnsSameInstance()
    {
        // Arrange
        var services = new CoreServiceCollection();
        services.AddSingleton<ITestDetector, CompositeTestDetector>();
        services.AddSingleton<ITestFrameworkDetector, XUnitTestDetector>();

        var provider = services.BuildServiceProvider();

        // Act
        var first = provider.GetService(typeof(ITestDetector));
        var second = provider.GetService(typeof(ITestDetector));

        // Assert
        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void CoreServiceProvider_TransientLifetime_ReturnsDifferentInstances()
    {
        // Arrange
        var services = new CoreServiceCollection();
        services.AddTransient<ICallerResolver, CallerResolver>();

        var provider = services.BuildServiceProvider();

        // Act
        var first = provider.GetService(typeof(ICallerResolver));
        var second = provider.GetService(typeof(ICallerResolver));

        // Assert
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void CoreServiceProvider_ReturnsNull_ForUnregisteredService()
    {
        // Arrange
        var provider = TestLensServices.CreateServiceProvider();

        // Act
        var service = provider.GetService(typeof(IDisposable));

        // Assert
        Assert.Null(service);
    }
}
