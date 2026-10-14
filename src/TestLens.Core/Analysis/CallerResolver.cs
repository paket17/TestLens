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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ISymbol, IReadOnlyList<DiscoveredCaller>> _cache =
        new(SymbolEqualityComparer.Default);

    public async Task<IReadOnlyList<DiscoveredCaller>> FindCallersAsync(
        Solution solution,
        IMethodSymbol targetMethod,
        CoverageSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        CompatExtensions.ThrowIfNull(solution, nameof(solution));
        CompatExtensions.ThrowIfNull(targetMethod, nameof(targetMethod));
        CompatExtensions.ThrowIfNull(options, nameof(options));

        if (_cache.TryGetValue(targetMethod, out var cached))
            return cached;

        var results = new List<DiscoveredCaller>();
        var seenCallKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Direct callers of the method
        await CollectCallersForSymbolAsync(
            solution,
            targetMethod,
            targetMethod,
            CallChainStepKind.DirectCall,
            results,
            seenCallKeys,
            options,
            cancellationToken);

        // 2. Interface calls if method implements interfaces (excluding standard System interfaces)
        if (options.FollowInterfaces && results.Count < options.MaxCallersPerMethod)
        {
            foreach (var ifaceMethod in GetImplementedInterfaceMethods(targetMethod))
            {
                if (results.Count >= options.MaxCallersPerMethod)
                    break;

                var ns = ifaceMethod.ContainingNamespace?.ToDisplayString();
                if (ns != null && (ns.StartsWith("System.") || ns == "System"))
                    continue;

                await CollectCallersForSymbolAsync(
                    solution,
                    ifaceMethod,
                    targetMethod,
                    CallChainStepKind.InterfaceDispatch,
                    results,
                    seenCallKeys,
                    options,
                    cancellationToken);
            }
        }

        // 3. Virtual / abstract base method callers (excluding System.Object)
        if (options.FollowOverrides && targetMethod.OverriddenMethod != null && results.Count < options.MaxCallersPerMethod)
        {
            var baseMethod = targetMethod.OverriddenMethod;
            var baseDepth = 0;
            while (baseMethod != null && baseDepth < 2 && results.Count < options.MaxCallersPerMethod)
            {
                var containingType = baseMethod.ContainingType;
                if (containingType?.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType)
                    break;

                var ns = containingType?.ContainingNamespace?.ToDisplayString();
                if (ns != null && (ns.StartsWith("System.") || ns == "System"))
                    break;

                await CollectCallersForSymbolAsync(
                    solution,
                    baseMethod,
                    targetMethod,
                    CallChainStepKind.VirtualOverrideDispatch,
                    results,
                    seenCallKeys,
                    options,
                    cancellationToken);

                baseMethod = baseMethod.OverriddenMethod;
                baseDepth++;
            }
        }

        _cache[targetMethod] = results;
        return results;
    }

    private async Task CollectCallersForSymbolAsync(
        Solution solution,
        IMethodSymbol symbolToFindCallersFor,
        IMethodSymbol actualTargetCallee,
        CallChainStepKind stepKind,
        List<DiscoveredCaller> results,
        HashSet<string> seenCallKeys,
        CoverageSearchOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var candidateDocs = GetCandidateDocuments(solution, symbolToFindCallersFor);
            var callers = await SymbolFinder.FindCallersAsync(
                symbolToFindCallersFor,
                solution,
                documents: candidateDocs,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var callerInfo in callers)
            {
                if (cancellationToken.IsCancellationRequested || results.Count >= options.MaxCallersPerMethod)
                    break;

                var callerMethod = UnwrapToMethodSymbol(callerInfo.CallingSymbol);
                if (callerMethod == null)
                    continue;

                foreach (var location in callerInfo.Locations)
                {
                    if (results.Count >= options.MaxCallersPerMethod)
                        break;

                    var lineSpan = location.GetLineSpan();
                    var filePath = lineSpan.Path;
                    var lineNumber = lineSpan.StartLinePosition.Line + 1;

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
            logger?.LogWarning(ex, "Error finding callers for symbol {Symbol}", symbolToFindCallersFor.ToDisplayString());
        }
    }

    /// <summary>
    /// Ограничивает область поиска только проектами, зависящими от сборки целевого символа.
    /// Это дает колоссальное ускорение в крупных решениях с сотнями проектов.
    /// </summary>
    private static System.Collections.Immutable.IImmutableSet<Document>? GetCandidateDocuments(
        Solution solution,
        IMethodSymbol symbol)
    {
        var assemblyName = symbol.ContainingAssembly?.Name;
        if (string.IsNullOrEmpty(assemblyName))
            return null;

        var declaringProject = solution.Projects.FirstOrDefault(
            p => string.Equals(p.AssemblyName, assemblyName, StringComparison.OrdinalIgnoreCase));
        if (declaringProject == null)
            return null;

        var depGraph = solution.GetProjectDependencyGraph();
        var dependentProjectIds = depGraph.GetProjectsThatTransitivelyDependOnThisProject(declaringProject.Id);

        var docSet = new HashSet<Document>();
        foreach (var doc in declaringProject.Documents)
            docSet.Add(doc);

        foreach (var depId in dependentProjectIds)
        {
            var depProject = solution.GetProject(depId);
            if (depProject != null)
            {
                foreach (var doc in depProject.Documents)
                    docSet.Add(doc);
            }
        }

        return System.Collections.Immutable.ImmutableHashSet.ToImmutableHashSet(docSet);
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

