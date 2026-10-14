using Microsoft.CodeAnalysis;
using TestLens.Core.Models;

namespace TestLens.Core.Detection.Detectors;

/// <summary>
/// Детектор тестов NUnit ([Test], [TestCase], [TestCaseSource], [Theory]).
/// </summary>
public sealed class NUnitTestDetector : ITestFrameworkDetector
{
    private static readonly HashSet<string> KnownAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Test",
        "TestAttribute",
        "TestCase",
        "TestCaseAttribute",
        "TestCaseSource",
        "TestCaseSourceAttribute",
        "Theory",
        "TheoryAttribute",
        "NUnit.Framework.TestAttribute",
        "NUnit.Framework.TestCaseAttribute",
        "NUnit.Framework.TestCaseSourceAttribute",
        "NUnit.Framework.TheoryAttribute"
    };

    public TestFrameworkType SupportedFramework => TestFrameworkType.NUnit;

    public bool IsTestMethod(IMethodSymbol methodSymbol, out string? testDisplayName)
    {
        testDisplayName = null;

        foreach (var attribute in methodSymbol.GetAttributes())
        {
            var attrClass = attribute.AttributeClass;
            if (attrClass == null)
                continue;

            if (KnownAttributeNames.Contains(attrClass.Name) ||
                KnownAttributeNames.Contains(attrClass.ToDisplayString()))
            {
                testDisplayName = ExtractDisplayName(attribute);
                return true;
            }

            var baseType = attrClass.BaseType;
            while (baseType != null)
            {
                if (KnownAttributeNames.Contains(baseType.Name) ||
                    KnownAttributeNames.Contains(baseType.ToDisplayString()))
                {
                    testDisplayName = ExtractDisplayName(attribute);
                    return true;
                }

                baseType = baseType.BaseType;
            }
        }

        return false;
    }

    private static string? ExtractDisplayName(AttributeData attribute)
    {
        // 1. Семантический поиск
        foreach (var namedArg in attribute.NamedArguments)
        {
            if ((string.Equals(namedArg.Key, "Description", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(namedArg.Key, "TestName", StringComparison.OrdinalIgnoreCase)) &&
                namedArg.Value.Value is string strVal)
                return strVal;
        }

        // 2. Синтаксический fallback
        if (attribute.ApplicationSyntaxReference?.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax attrSyntax &&
            attrSyntax.ArgumentList != null)
        {
            foreach (var arg in attrSyntax.ArgumentList.Arguments)
            {
                if (arg.NameEquals != null &&
                    (string.Equals(arg.NameEquals.Name.Identifier.Text, "Description", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(arg.NameEquals.Name.Identifier.Text, "TestName", StringComparison.OrdinalIgnoreCase)))
                {
                    if (arg.Expression is Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax lit)
                        return lit.Token.ValueText;
                }
            }
        }

        return null;
    }
}
