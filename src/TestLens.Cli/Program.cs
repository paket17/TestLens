using System.Text.Json;
using TestLens.Core.Analysis;
using TestLens.Core.Execution;
using TestLens.Core.Execution.Models;
using TestLens.Core.Extensions;
using TestLens.Core.Models;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.DependencyInjection;

namespace TestLens.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        string? solutionPath = GetArgument(args, "--solution", "-s");
        string? methodName = GetArgument(args, "--method", "-m");
        string? fileName = GetArgument(args, "--file", "-f");
        int depth = int.TryParse(GetArgument(args, "--depth", "-d"), out var d) ? d : 10;
        bool run = args.Contains("--run") || args.Contains("-r");
        bool json = args.Contains("--json");

        if (string.IsNullOrWhiteSpace(solutionPath) || string.IsNullOrWhiteSpace(methodName))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Ошибка: Параметры --solution (-s) и --method (-m) обязательны!");
            Console.ResetColor();
            PrintUsage();
            return 1;
        }

        var solutionFile = new FileInfo(solutionPath);
        if (!solutionFile.Exists)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Ошибка: Файл решения не найден: {solutionFile.FullName}");
            Console.ResetColor();
            return 1;
        }

        await RunAsync(solutionFile, methodName, fileName, depth, run, json);
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
TestLens CLI - Инструмент поиска тестов по методу в .NET

Использование:
  dotnet run --project src/TestLens.Cli -- [параметры]

Параметры:
  -s, --solution <path>   Путь к файлу решения (.sln) [обязательно]
  -m, --method <name>     Имя целевого метода [обязательно]
  -f, --file <name>       Имя файла исходного кода (для фильтрации перегрузок)
  -d, --depth <int>       Максимальная глубина цепочки вызовов (по умолчанию: 10)
  -r, --run               Автоматически запустить найденные тесты
  --json                  Вывод в формате JSON (для интеграции с IDE/VS Code)
  -h, --help              Справка

Пример:
  TestLens.Cli -s MySolution.sln -m ProcessOrder -r
""");
    }

    private static string? GetArgument(string[] args, params string[] aliases)
    {
        for (int i = 0; i < args.Length; i++)
        {
            foreach (var alias in aliases)
            {
                if (string.Equals(args[i], alias, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    return args[i + 1];
                }
            }
        }
        return null;
    }

    private static async Task RunAsync(
        FileInfo solutionFile,
        string methodName,
        string? fileName,
        int depth,
        bool runTests,
        bool outputJson)
    {
        // Регистрация экземпляра MSBuild
        if (!MSBuildLocator.IsRegistered)
        {
            var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
            if (instances.Length > 0)
            {
                MSBuildLocator.RegisterInstance(instances.OrderByDescending(i => i.Version).First());
            }
            else
            {
                MSBuildLocator.RegisterDefaults();
            }
        }

        // Настройка DI через общий фабричный метод Core
        var serviceProvider = TestLens.Core.TestLensServices.CreateServiceProvider();

        var testFinder = serviceProvider.GetRequiredService<ICoveringTestFinder>();
        var testRunner = serviceProvider.GetRequiredService<ITestRunner>();

        if (!outputJson)
        {
            Console.WriteLine("╔════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║   TestLens CLI - Поиск тестов по цепочке вызовов Roslyn   ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine($"Загрузка решения: {solutionFile.Name}...");
        }

        using var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, e) =>
        {
            if (!outputJson && e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[MSBuild Diagnostic] {e.Diagnostic.Message}");
                Console.ResetColor();
            }
        };

        var solution = await workspace.OpenSolutionAsync(solutionFile.FullName);

        if (!outputJson)
        {
            Console.WriteLine($"Поиск метода '{methodName}' в решении...");
        }

        var candidateMethods = new List<(IMethodSymbol Symbol, Document Document)>();

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (fileName != null && !document.Name.Contains(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!document.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var syntaxRoot = await document.GetSyntaxRootAsync();
                if (syntaxRoot == null) continue;

                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel == null) continue;

                var methodDeclarations = syntaxRoot.DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .Where(m => string.Equals(m.Identifier.Text, methodName, StringComparison.OrdinalIgnoreCase));

                foreach (var methodDecl in methodDeclarations)
                {
                    var symbol = semanticModel.GetDeclaredSymbol(methodDecl);
                    if (symbol is IMethodSymbol methodSymbol)
                    {
                        candidateMethods.Add((methodSymbol, document));
                    }
                }
            }
        }

        if (candidateMethods.Count == 0)
        {
            if (outputJson)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { error = $"Метод '{methodName}' не найден в решении." }));
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Метод с именем '{methodName}' не найден в коде.");
                Console.ResetColor();
            }
            return;
        }

        var targetEntry = candidateMethods[0];
        if (candidateMethods.Count > 1 && !outputJson)
        {
            Console.WriteLine($"Найдено методов: {candidateMethods.Count}. Используется первый найденный:");
            Console.WriteLine($"  -> {targetEntry.Symbol.ToDisplayString()} ({targetEntry.Document.FilePath})");
        }

        var targetMethod = targetEntry.Symbol;

        var options = new CoverageSearchOptions
        {
            MaxCallDepth = depth,
            FollowInterfaces = true,
            FollowOverrides = true
        };

        var result = await testFinder.FindCoveringTestsAsync(solution, targetMethod, options);

        if (outputJson)
        {
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return;
        }

        Console.WriteLine();
        Console.WriteLine("------------------------------------------------------------------------");
        Console.Write("Целевой метод: ");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(result.TargetMethodFullyQualifiedName);
        Console.ResetColor();

        Console.Write("Файл: ");
        Console.WriteLine($"{result.TargetFilePath}:{result.TargetLineNumber}");

        Console.Write("Статус покрытия: ");
        if (result.IsCovered)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"ПОКРЫТ (найдено тестов: {result.TotalCoveringTestsCount})");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("НЕ ПОКРЫТ ТЕСТАМИ (0 вызовов из тестов)");
        }
        Console.ResetColor();

        Console.WriteLine($"Время анализа: {result.ElapsedTime.TotalMilliseconds:F1} мс");
        Console.WriteLine("------------------------------------------------------------------------");

        if (result.IsCovered)
        {
            Console.WriteLine();
            Console.WriteLine("Список тестов и цепочек вызовов:");

            for (int i = 0; i < result.CoveringTests.Count; i++)
            {
                var match = result.CoveringTests[i];
                var test = match.TestMethod;

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"[{i + 1}] [{test.Framework}] ");
                Console.ResetColor();
                Console.Write($"{test.FullyQualifiedName} ");

                Console.ForegroundColor = match.HasDirectCall ? ConsoleColor.Green : ConsoleColor.DarkYellow;
                Console.WriteLine($"({(match.HasDirectCall ? "Прямой вызов" : $"Косвенный, глубина: {match.MinDepth}")})");
                Console.ResetColor();

                if (!string.IsNullOrEmpty(test.DisplayName))
                {
                    Console.WriteLine($"    Display: \"{test.DisplayName}\"");
                }

                foreach (var chain in match.CallChains)
                {
                    Console.WriteLine("    Цепочка вызова:");
                    for (int s = 0; s < chain.Steps.Count; s++)
                    {
                        var step = chain.Steps[s];
                        var prefix = s == chain.Steps.Count - 1 ? "    └─ " : "    ├─ ";
                        var stepKindStr = step.StepKind != CallChainStepKind.DirectCall ? $" [{step.StepKind}]" : "";
                        var snippet = !string.IsNullOrEmpty(step.CodeSnippet) ? $" : \"{step.CodeSnippet}\"" : "";

                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write(prefix);
                        Console.ResetColor();
                        Console.WriteLine($"{step.CallerName} -> {step.CalleeName}{stepKindStr} (Line {step.LineNumber}){snippet}");
                    }
                }
            }

            if (runTests)
            {
                Console.WriteLine();
                Console.WriteLine("════════════════════════════════════════════════════════════════════════");
                Console.WriteLine("Запуск найденных тестов через `dotnet test`...");
                Console.WriteLine("════════════════════════════════════════════════════════════════════════");

                var runRequest = new TestRunRequest
                {
                    SolutionOrProjectPath = solutionFile.FullName,
                    TestsToRun = result.CoveringTests.Select(t => t.TestMethod).ToList()
                };

                var runResult = await testRunner.RunTestsAsync(runRequest, line =>
                {
                    Console.WriteLine($"  [Test Output] {line}");
                });

                Console.WriteLine();
                if (runResult.Success)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✓ ВСЕ ТЕСТЫ ПРОЙДЕНЫ! (Пройдено: {runResult.PassedCount}, Время: {runResult.Duration.TotalSeconds:F2}с)");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"✗ ОШИБКА: Тесты завершились неудачей! (Провалено: {runResult.FailedCount}, Пройдено: {runResult.PassedCount})");
                    if (!string.IsNullOrEmpty(runResult.ErrorMessage))
                    {
                        Console.WriteLine($"  {runResult.ErrorMessage}");
                    }
                }
                Console.ResetColor();
            }
        }
    }
}
