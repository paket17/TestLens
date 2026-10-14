using TestLens.Core.Analysis;
using TestLens.Core.Detection;
using TestLens.Core.Detection.Detectors;
using TestLens.Core.Tests.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

        Assert.True(result.IsCovered);
        Assert.Single(result.CoveringTests);

        var match = result.CoveringTests[0];
        Assert.Equal("MyDirectTest", match.TestMethod.MethodName);
        Assert.Equal(1, match.MinDepth);
        Assert.True(match.HasDirectCall);
        Assert.Single(match.CallChains);
        Assert.Equal(1, match.CallChains[0].Depth);
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

        Assert.True(result.IsCovered);
        Assert.Single(result.CoveringTests);

        var match = result.CoveringTests[0];
        Assert.Equal("RunEndToEnd", match.TestMethod.MethodName);
        Assert.Equal(3, match.MinDepth);
        Assert.False(match.HasDirectCall);

        var chain = match.CallChains[0];
        Assert.Equal(3, chain.Depth);
        Assert.Equal("RunEndToEnd", chain.Steps[0].CallerName);
        Assert.Equal("RunWorkflow", chain.Steps[0].CalleeName);
        Assert.Equal("RunWorkflow", chain.Steps[1].CallerName);
        Assert.Equal("Execute", chain.Steps[1].CalleeName);
        Assert.Equal("Execute", chain.Steps[2].CallerName);
        Assert.Equal("TargetMethod", chain.Steps[2].CalleeName);
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

        Assert.True(result.IsCovered);
        Assert.Single(result.CoveringTests);
        Assert.Equal("TestThroughInterface", result.CoveringTests[0].TestMethod.MethodName);
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

        Assert.False(result.IsCovered);
        Assert.Empty(result.CoveringTests);
        Assert.Equal(0, result.TotalCoveringTestsCount);
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

        Assert.True(result.IsCovered);
        Assert.Contains(result.CoveringTests, t => t.TestMethod.MethodName == "TestPing");
    }
}
