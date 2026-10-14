using TestLens.Core.Compatibility;
using TestLens.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace TestLens.Core.Analysis;

/// <summary>
/// Реализация резолвера вызывающих методов с учетом полиморфизма Roslyn.
/// </summary>
public sealed class CallerResolver(
    ILogger<CallerResolver>? logger = null) : ICallerResolver
{
    public async Task<IReadOnlyList<DiscoveredCaller>> FindCallersAsync(
        Solution solution,
        IMethodSymbol targetMethod,
        CoverageSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        CompatExtensions.ThrowIfNull(solution, nameof(solution));
        CompatExtensions.ThrowIfNull(targetMethod, nameof(targetMethod));
        CompatExtensions.ThrowIfNull(options, nameof(options));

        var results = new List<DiscoveredCaller>();
        var seenCallKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Прямые вызовы метода
        await CollectCallersForSymbolAsync(
            solution,
            targetMethod,
            targetMethod,
            CallChainStepKind.DirectCall,
            results,
            seenCallKeys,
            cancellationToken);

        // 2. Вызовы через интерфейсы, которые реализует данный метод
        if (options.FollowInterfaces)
        {
            foreach (var ifaceMethod in GetImplementedInterfaceMethods(targetMethod))
            {
                await CollectCallersForSymbolAsync(
                    solution,
                    ifaceMethod,
                    targetMethod,
                    CallChainStepKind.InterfaceDispatch,
                    results,
                    seenCallKeys,
                    cancellationToken);
            }
        }

        // 3. Вызовы через базовые методы (virtual / abstract / override)
        if (options.FollowOverrides && targetMethod.OverriddenMethod != null)
        {
            var baseMethod = targetMethod.OverriddenMethod;
            while (baseMethod != null)
            {
                await CollectCallersForSymbolAsync(
                    solution,
                    baseMethod,
                    targetMethod,
                    CallChainStepKind.VirtualOverrideDispatch,
                    results,
                    seenCallKeys,
                    cancellationToken);

                baseMethod = baseMethod.OverriddenMethod;
            }
        }

        return results;
    }

    private async Task CollectCallersForSymbolAsync(
        Solution solution,
        IMethodSymbol symbolToFindCallersFor,
        IMethodSymbol actualTargetCallee,
        CallChainStepKind stepKind,
        List<DiscoveredCaller> results,
        HashSet<string> seenCallKeys,
        CancellationToken cancellationToken)
    {
        try
        {
            var callers = await SymbolFinder.FindCallersAsync(
                symbolToFindCallersFor,
                solution,
                cancellationToken).ConfigureAwait(false);

            foreach (var callerInfo in callers)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var callerMethod = UnwrapToMethodSymbol(callerInfo.CallingSymbol);
                if (callerMethod == null)
                    continue;

                // Перебираем конкретные места вызова в коде
                foreach (var location in callerInfo.Locations)
                {
                    var lineSpan = location.GetLineSpan();
                    var filePath = lineSpan.Path;
                    var lineNumber = lineSpan.StartLinePosition.Line + 1;

                    // Ключ дедупликации
                    var key = $"{callerMethod.ToDisplayString()}|{filePath}|{lineNumber}|{actualTargetCallee.ToDisplayString()}";
                    if (!seenCallKeys.Add(key))
                        continue;

                    string? snippet = null;
                    if (location.SourceTree != null)
                    {
                        var text = await location.SourceTree.GetTextAsync(cancellationToken).ConfigureAwait(false);
                        var lineIndex = lineSpan.StartLinePosition.Line;
                        if (lineIndex >= 0 && lineIndex < text.Lines.Count)
                            snippet = text.Lines[lineIndex].ToString().Trim();
                    }

                    results.Add(new DiscoveredCaller
                    {
                        CallerSymbol = callerMethod,
                        CalleeSymbol = actualTargetCallee,
                        FilePath = filePath,
                        LineNumber = lineNumber,
                        CodeSnippet = snippet,
                        StepKind = stepKind
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Ошибка при поиске вызовов для символа {Symbol}", symbolToFindCallersFor.ToDisplayString());
        }
    }

    /// <summary>
    /// Разворачивает локальные функции, анонимные методы и лямбды до их охватывающего метода.
    /// </summary>
    private static IMethodSymbol? UnwrapToMethodSymbol(ISymbol symbol)
    {
        var current = symbol;
        while (current != null)
        {
            if (current is IMethodSymbol method)
            {
                // Если это нормальный метод или геттер/сеттер свойства
                if (method.MethodKind is MethodKind.Ordinary
                    or MethodKind.PropertyGet
                    or MethodKind.PropertySet
                    or MethodKind.Constructor
                    or MethodKind.StaticConstructor)
                {
                    return method;
                }

                // Если это локальная функция или лямбда, поднимаемся выше
                if (method.MethodKind is MethodKind.LocalFunction
                    or MethodKind.AnonymousFunction)
                {
                    current = method.ContainingSymbol;
                    continue;
                }

                return method;
            }

            if (current is IPropertySymbol prop)
                return prop.GetMethod ?? prop.SetMethod;

            current = current.ContainingSymbol;
        }

        return null;
    }

    /// <summary>
    /// Возвращает все методы интерфейсов (явные и неявные), которые реализует данный метод.
    /// </summary>
    private static IEnumerable<IMethodSymbol> GetImplementedInterfaceMethods(IMethodSymbol method)
    {
        // 1. Явно реализованные методы интерфейсов
        foreach (var explicitImpl in method.ExplicitInterfaceImplementations)
            yield return explicitImpl;

        // 2. Неявно реализованные методы интерфейсов
        var containingType = method.ContainingType;
        if (containingType != null)
        {
            foreach (var iface in containingType.AllInterfaces)
            {
                foreach (var ifaceMember in iface.GetMembers().OfType<IMethodSymbol>())
                {
                    var impl = containingType.FindImplementationForInterfaceMember(ifaceMember);
                    if (SymbolEqualityComparer.Default.Equals(impl, method))
                        yield return ifaceMember;
                }
            }
        }
    }
}

