using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TestLens.Core.Models;

namespace TestLens.Core.Detection.Detectors;

/// <summary>
/// Детектор тестов xUnit ([Fact], [Theory]).
/// </summary>
public sealed class XUnitTestDetector : ITestFrameworkDetector
{
    private static readonly HashSet<string> KnownAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fact",
        "FactAttribute",
        "Theory",
        "TheoryAttribute",
        "Xunit.FactAttribute",
        "Xunit.TheoryAttribute"
    };

    public TestFrameworkType SupportedFramework => TestFrameworkType.XUnit;

    public bool IsTestMethod(IMethodSymbol methodSymbol, out string? testDisplayName)
    {
        testDisplayName = null;

        foreach (var attribute in methodSymbol.GetAttributes())
        {
            var attrClass = attribute.AttributeClass;
            if (attrClass == null)
                continue;

            // Проверка по имени или полному имени атрибута
            if (KnownAttributeNames.Contains(attrClass.Name) ||
                KnownAttributeNames.Contains(attrClass.ToDisplayString()))
            {
                testDisplayName = ExtractDisplayName(attribute);
                return true;
            }

            // Проверка по базовым классам атрибута (на случай кастомных фактов: [SkippableFact] и т.п.)
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
        // 1. Проверяем именованные параметры, например: [Fact(DisplayName = "Custom test name")]
        foreach (var namedArg in attribute.NamedArguments)
        {
            if (string.Equals(namedArg.Key, "DisplayName", StringComparison.OrdinalIgnoreCase) &&
                namedArg.Value.Value is string stringValue)
            {
                return stringValue;
            }
        }

        // 2. Синтаксический fallback
        if (attribute.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax attrSyntax &&
            attrSyntax.ArgumentList != null)
        {
            foreach (var arg in attrSyntax.ArgumentList.Arguments)
            {
                if (arg.NameEquals != null &&
                    string.Equals(arg.NameEquals.Name.Identifier.Text, "DisplayName", StringComparison.OrdinalIgnoreCase))
                {
                    if (arg.Expression is LiteralExpressionSyntax literalExpressionSyntax)
                        return literalExpressionSyntax.Token.ValueText;
                }
            }
        }

        return null;
    }
}
