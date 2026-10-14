using TestLens.Core.Detection;
using TestLens.Core.Detection.Detectors;
using TestLens.Core.Models;
using TestLens.Core.Tests.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TestLens.Core.Tests.Detection;

public class TestDetectorTests
{
    [Fact]
    public async Task XUnitTestDetector_ShouldIdentifyFactAndTheory()
    {
        var source = """
            using Xunit;
            public class SampleTests
            {
                [Fact(DisplayName = "Custom Fact Name")]
                public void TestMethod1() { }

                [Theory]
                public void TestMethod2() { }

                public void RegularMethod() { }
            }
            """;

        var (_, _, document) = TestWorkspaceHelper.CreateWorkspaceFromSource(source);
        var semanticModel = await document.GetSemanticModelAsync();
        var syntaxRoot = await document.GetSyntaxRootAsync();

        var methods = syntaxRoot!.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        var detector = new XUnitTestDetector();

        // 1. Fact
        var symbol1 = (IMethodSymbol)semanticModel!.GetDeclaredSymbol(methods[0])!;
        var isTest1 = detector.IsTestMethod(symbol1, out var displayName1);
        Assert.True(isTest1);
        Assert.Equal("Custom Fact Name", displayName1);

        // 2. Theory
        var symbol2 = (IMethodSymbol)semanticModel!.GetDeclaredSymbol(methods[1])!;
        var isTest2 = detector.IsTestMethod(symbol2, out var displayName2);
        Assert.True(isTest2);
        Assert.Null(displayName2);

        // 3. Regular
        var symbol3 = (IMethodSymbol)semanticModel!.GetDeclaredSymbol(methods[2])!;
        var isTest3 = detector.IsTestMethod(symbol3, out _);
        Assert.False(isTest3);
    }

    [Fact]
    public async Task CompositeTestDetector_ShouldProduceDescriptor()
    {
        var source = """
            using System;
            namespace MyNamespace
            {
                public class TestClass
                {
                    [Xunit.Fact]
                    public void MyTest() { }
                }
            }
            """;

        var (_, _, document) = TestWorkspaceHelper.CreateWorkspaceFromSource(source);
        var semanticModel = await document.GetSemanticModelAsync();
        var syntaxRoot = await document.GetSyntaxRootAsync();

        var methodDecl = syntaxRoot!.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var methodSymbol = (IMethodSymbol)semanticModel!.GetDeclaredSymbol(methodDecl)!;

        var composite = new CompositeTestDetector(new ITestFrameworkDetector[]
        {
            new XUnitTestDetector(),
            new NUnitTestDetector(),
            new MSTestDetector()
        });

        var result = composite.TryGetTestDescriptor(methodSymbol, out var descriptor);

        Assert.True(result);
        Assert.NotNull(descriptor);
        Assert.Equal("MyTest", descriptor.MethodName);
        Assert.Equal("TestClass", descriptor.ClassName);
        Assert.Equal("MyNamespace", descriptor.Namespace);
        Assert.Equal(TestFrameworkType.XUnit, descriptor.Framework);
        Assert.Equal("MyNamespace.TestClass.MyTest", descriptor.FullyQualifiedName);
    }

    [Fact]
    public async Task NUnitTestDetector_ShouldIdentifyNUnitAttributes()
    {
        var source = """
            namespace NUnit.Framework
            {
                public class TestAttribute : System.Attribute { }
                public class TestCaseAttribute : System.Attribute
                {
                    public string Description { get; set; }
                }
            }

            public class NUnitSample
            {
                [NUnit.Framework.Test]
                public void Test1() { }

                [NUnit.Framework.TestCase(Description = "NUnit description")]
                public void Test2() { }
            }
            """;

        var (_, _, document) = TestWorkspaceHelper.CreateWorkspaceFromSource(source);
        var semanticModel = await document.GetSemanticModelAsync();
        var syntaxRoot = await document.GetSyntaxRootAsync();

        var methods = syntaxRoot!.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => m.Identifier.Text.StartsWith("Test"))
            .ToList();

        var detector = new NUnitTestDetector();

        var isTest1 = detector.IsTestMethod((IMethodSymbol)semanticModel!.GetDeclaredSymbol(methods[0])!, out _);
        Assert.True(isTest1);

        var isTest2 = detector.IsTestMethod((IMethodSymbol)semanticModel!.GetDeclaredSymbol(methods[1])!, out var desc);
        Assert.True(isTest2);
        Assert.Equal("NUnit description", desc);
    }

    [Fact]
    public async Task MSTestDetector_ShouldIdentifyMSTestAttributes()
    {
        var source = """
            namespace Microsoft.VisualStudio.TestTools.UnitTesting
            {
                public class TestMethodAttribute : System.Attribute
                {
                    public string DisplayName { get; set; }
                }
            }

            public class MSTestSample
            {
                [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod(DisplayName = "MSTest method")]
                public void MyMSTest() { }
            }
            """;

        var (_, _, document) = TestWorkspaceHelper.CreateWorkspaceFromSource(source);
        var semanticModel = await document.GetSemanticModelAsync();
        var syntaxRoot = await document.GetSyntaxRootAsync();

        var method = syntaxRoot!.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == "MyMSTest");

        var detector = new MSTestDetector();
        var isTest = detector.IsTestMethod((IMethodSymbol)semanticModel!.GetDeclaredSymbol(method)!, out var name);

        Assert.True(isTest);
        Assert.Equal("MSTest method", name);
    }
}

