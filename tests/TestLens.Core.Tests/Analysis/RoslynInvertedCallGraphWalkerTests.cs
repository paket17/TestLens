using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TestLens.Core.Analysis;
using TestLens.Core.Detection;
using TestLens.Core.Detection.Detectors;
using TestLens.Core.Tests.Helpers;

namespace TestLens.Core.Tests.Analysis;

public class RoslynInvertedCallGraphWalkerTests
{
    private readonly RoslynInvertedCallGraphWalker _walker;

    public RoslynInvertedCallGraphWalkerTests()
    {
        var detectors = new ITestFrameworkDetector[]
        {
            new XUnitTestDetector(),
            new NUnitTestDetector(),
            new MSTestDetector()
        };
        var testDetector = new CompositeTestDetector(detectors);
        var callerResolver = new CallerResolver();
        _walker = new RoslynInvertedCallGraphWalker(callerResolver, testDetector);
    }

    [Fact]
    public async Task FindCoveringTests_DirectCall_ShouldFindTestWithDepth1()
    {
        var source = """
            using Xunit;

            public class TargetService
            {
                public void TargetMethod() { }
            }

            public class ServiceTests
            {
                [Fact]
                public void MyDirectTest()
                {
                    var service = new TargetService();
                    service.TargetMethod();
                }
            }
            """;

        var (solution, _, document) = TestWorkspaceHelper.CreateWorkspaceFromSource(source);
        var semanticModel = await document.GetSemanticModelAsync();
        var syntaxRoot = await document.GetSyntaxRootAsync();

        var targetMethodSyntax = syntaxRoot!.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == "TargetMethod");

        var targetSymbol = (IMethodSymbol)semanticModel!.GetDeclaredSymbol(targetMethodSyntax)!;

        var result = await _walker.FindCoveringTestsAsync(
            solution,
            targetSymbol,
            new CoverageSearchOptions());

        result.IsCovered.Should().BeTrue();
        result.CoveringTests.Should().ContainSingle();

        var match = result.CoveringTests[0];
        match.TestMethod.MethodName.Should().Be("MyDirectTest");
        match.MinDepth.Should().Be(1);
        match.HasDirectCall.Should().BeTrue();
        match.CallChains.Should().ContainSingle();
        match.CallChains[0].Depth.Should().Be(1);
    }

    [Fact]
    public async Task FindCoveringTests_DeepCallChain_ShouldFindIndirectTest()
    {
        var source = """
            using Xunit;

            public class CoreService
            {
                public void TargetMethod() { }
            }

            public class BusinessWorkflow
            {
                private readonly CoreService _core = new();
                public void Execute() => _core.TargetMethod();
            }

            public class WorkflowFacade
            {
                private readonly BusinessWorkflow _wf = new();
                public void RunWorkflow() => _wf.Execute();
            }

            public class IntegrationTests
            {
                [Fact]
                public void RunEndToEnd()
                {
                    var facade = new WorkflowFacade();
                    facade.RunWorkflow();
                }
            }
            """;

        var (solution, _, document) = TestWorkspaceHelper.CreateWorkspaceFromSource(source);
        var semanticModel = await document.GetSemanticModelAsync();
        var syntaxRoot = await document.GetSyntaxRootAsync();

        var targetMethodSyntax = syntaxRoot!.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == "TargetMethod");

        var targetSymbol = (IMethodSymbol)semanticModel!.GetDeclaredSymbol(targetMethodSyntax)!;

        var result = await _walker.FindCoveringTestsAsync(
            solution,
            targetSymbol,
            new CoverageSearchOptions());

        result.IsCovered.Should().BeTrue();
        result.CoveringTests.Should().ContainSingle();

        var match = result.CoveringTests[0];
        match.TestMethod.MethodName.Should().Be("RunEndToEnd");
        match.MinDepth.Should().Be(3);
        match.HasDirectCall.Should().BeFalse();

        var chain = match.CallChains[0];
        chain.Depth.Should().Be(3);
        chain.Steps[0].CallerName.Should().Be("RunEndToEnd");
        chain.Steps[0].CalleeName.Should().Be("RunWorkflow");
        chain.Steps[1].CallerName.Should().Be("RunWorkflow");
        chain.Steps[1].CalleeName.Should().Be("Execute");
        chain.Steps[2].CallerName.Should().Be("Execute");
        chain.Steps[2].CalleeName.Should().Be("TargetMethod");
    }

    [Fact]
    public async Task FindCoveringTests_InterfaceDispatch_ShouldFindCallerViaInterface()
    {
        var source = """
            using Xunit;

            public interface ICalculator
            {
                int Calculate();
            }

            public class Calculator : ICalculator
            {
                public int Calculate() => 42;
            }

            public class CalculatorTests
            {
                [Fact]
                public void TestThroughInterface()
                {
                    ICalculator calc = new Calculator();
                    calc.Calculate();
                }
            }
            """;

        var (solution, _, document) = TestWorkspaceHelper.CreateWorkspaceFromSource(source);
        var semanticModel = await document.GetSemanticModelAsync();
        var syntaxRoot = await document.GetSyntaxRootAsync();

        var calcClassSyntax = syntaxRoot!.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == "Calculator");

        var targetMethodSyntax = calcClassSyntax.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == "Calculate");

        var targetSymbol = (IMethodSymbol)semanticModel!.GetDeclaredSymbol(targetMethodSyntax)!;

        var result = await _walker.FindCoveringTestsAsync(
            solution,
            targetSymbol,
            new CoverageSearchOptions { FollowInterfaces = true });

        result.IsCovered.Should().BeTrue();
        result.CoveringTests.Should().ContainSingle();
        result.CoveringTests[0].TestMethod.MethodName.Should().Be("TestThroughInterface");
    }

    [Fact]
    public async Task FindCoveringTests_UncoveredMethod_ShouldReturnNotCovered()
    {
        var source = """
            using Xunit;

            public class OrphanService
            {
                public void NeverCalledMethod() { }
            }

            public class OtherTests
            {
                [Fact]
                public void UnrelatedTest() { }
            }
            """;

        var (solution, _, document) = TestWorkspaceHelper.CreateWorkspaceFromSource(source);
        var semanticModel = await document.GetSemanticModelAsync();
        var syntaxRoot = await document.GetSyntaxRootAsync();

        var targetMethodSyntax = syntaxRoot!.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == "NeverCalledMethod");

        var targetSymbol = (IMethodSymbol)semanticModel!.GetDeclaredSymbol(targetMethodSyntax)!;

        var result = await _walker.FindCoveringTestsAsync(
            solution,
            targetSymbol,
            new CoverageSearchOptions());

        result.IsCovered.Should().BeFalse();
        result.CoveringTests.Should().BeEmpty();
        result.TotalCoveringTestsCount.Should().Be(0);
    }

    [Fact]
    public async Task FindCoveringTests_WithCyclesInCallGraph_ShouldTerminateAndFindTest()
    {
        var source = """
            using Xunit;

            public class LoopingService
            {
                public void TargetMethod() { }

                public void Ping(bool stop)
                {
                    if (!stop) Pong(true);
                    TargetMethod();
                }

                public void Pong(bool stop)
                {
                    Ping(stop);
                }
            }

            public class CycleTests
            {
                [Fact]
                public void TestPing()
                {
                    var service = new LoopingService();
                    service.Ping(false);
                }
            }
            """;

        var (solution, _, document) = TestWorkspaceHelper.CreateWorkspaceFromSource(source);
        var semanticModel = await document.GetSemanticModelAsync();
        var syntaxRoot = await document.GetSyntaxRootAsync();

        var targetMethodSyntax = syntaxRoot!.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == "TargetMethod");

        var targetSymbol = (IMethodSymbol)semanticModel!.GetDeclaredSymbol(targetMethodSyntax)!;

        var result = await _walker.FindCoveringTestsAsync(
            solution,
            targetSymbol,
            new CoverageSearchOptions());

        result.IsCovered.Should().BeTrue();
        result.CoveringTests.Should().Contain(t => t.TestMethod.MethodName == "TestPing");
    }
}
