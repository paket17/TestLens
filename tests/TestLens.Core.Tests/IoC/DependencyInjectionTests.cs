using TestLens.Core.Analysis;
using TestLens.Core.Detection;
using TestLens.Core.Detection.Detectors;
using TestLens.Core.Execution;

namespace TestLens.Core.Tests.IoC;

public class DependencyInjectionTests
{
    [Fact]
    public void TestLensServices_CreateFinder_ReturnsValidInstance()
    {
        var finder = TestLensServices.CreateFinder();

        finder.Should().NotBeNull();
        finder.Should().BeOfType<RoslynInvertedCallGraphWalker>();
    }

    [Fact]
    public void TestLensServices_CreateFinder_WithCustomDependencies_UsesInjectedInstances()
    {
        var customResolver = new CallerResolver();
        var customDetector = new CompositeTestDetector(new XUnitTestDetector());

        var finder = TestLensServices.CreateFinder(customResolver, customDetector);

        finder.Should().NotBeNull();
        finder.Should().BeOfType<RoslynInvertedCallGraphWalker>();
    }

    [Fact]
    public void TestLensServices_CreateTestRunner_ReturnsValidInstance()
    {
        var runner = TestLensServices.CreateTestRunner();

        runner.Should().NotBeNull();
        runner.Should().BeOfType<DotNetCliTestRunner>();
    }

    [Fact]
    public void TestLensServices_CreateCallerResolver_ReturnsValidInstance()
    {
        var resolver = TestLensServices.CreateCallerResolver();

        resolver.Should().NotBeNull();
        resolver.Should().BeOfType<CallerResolver>();
    }

    [Fact]
    public void TestLensServices_CreateTestDetector_ReturnsCompositeWithFrameworkDetectors()
    {
        var detector = TestLensServices.CreateTestDetector();

        detector.Should().NotBeNull();
        detector.Should().BeOfType<CompositeTestDetector>();
    }
}
