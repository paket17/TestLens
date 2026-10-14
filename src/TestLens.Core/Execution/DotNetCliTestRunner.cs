using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using TestLens.Core.Compatibility;
using TestLens.Core.Execution.Models;
using TestLens.Core.Models;

namespace TestLens.Core.Execution;

/// <summary>
/// Реализация тестового раннера на основе команды `dotnet test`.
/// </summary>
public sealed class DotNetCliTestRunner(
    ILogger<DotNetCliTestRunner>? logger = null) : ITestRunner
{
    public Task<TestRunResult> RunSingleTestAsync(
        string solutionOrProjectPath,
        TestMethodDescriptor test,
        Action<string>? onOutputLine = null,
        CancellationToken cancellationToken = default)
    {
        CompatExtensions.ThrowIfNull(solutionOrProjectPath, nameof(solutionOrProjectPath));
        CompatExtensions.ThrowIfNull(test, nameof(test));

        var request = new TestRunRequest
        {
            SolutionOrProjectPath = solutionOrProjectPath,
            TestsToRun = [test]
        };

        return RunTestsAsync(request, onOutputLine, cancellationToken);
    }

    public async Task<TestRunResult> RunTestsAsync(
        TestRunRequest request,
        Action<string>? onOutputLine = null,
        CancellationToken cancellationToken = default)
    {
        CompatExtensions.ThrowIfNull(request, nameof(request));

        if (request.TestsToRun.Count == 0)
        {
            return new TestRunResult
            {
                Success = true,
                PassedCount = 0,
                Duration = TimeSpan.Zero,
                StandardOutput = "Нет тестов для запуска."
            };
        }

        var filter = BuildFilter(request.TestsToRun);
        var args = new StringBuilder();
        args.Append("test ");
        args.Append($"\"{request.SolutionOrProjectPath}\" ");
        args.Append($"--filter \"{filter}\" ");
        args.Append("--logger \"console;verbosity=normal\" ");

        if (request.NoBuild)
            args.Append("--no-build ");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args.ToString(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var outputBuilder = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var process = new Process { StartInfo = startInfo };
            using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancellationTokenSource.CancelAfter(request.Timeout);

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    lock (outputBuilder)
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                    onOutputLine?.Invoke(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    lock (outputBuilder)
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                    onOutputLine?.Invoke(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationTokenSource.Token).ConfigureAwait(false);
            stopwatch.Stop();

            var fullOutput = outputBuilder.ToString();
            var (passed, failed, skipped) = ParseSummary(fullOutput);

            return new TestRunResult
            {
                Success = process.ExitCode == 0 && failed == 0,
                PassedCount = passed,
                FailedCount = failed,
                SkippedCount = skipped,
                Duration = stopwatch.Elapsed,
                StandardOutput = fullOutput
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new TestRunResult
            {
                Success = false,
                Duration = stopwatch.Elapsed,
                StandardOutput = outputBuilder.ToString(),
                ErrorMessage = "Test execution was interrupted due to a timeout or cancellation."
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger?.LogError(ex, "Startup error dotnet test");
            return new TestRunResult
            {
                Success = false,
                Duration = stopwatch.Elapsed,
                StandardOutput = outputBuilder.ToString(),
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Собирает фильтр VSTest для dotnet test: FullyQualifiedName=Test1|FullyQualifiedName=Test2
    /// </summary>
    private static string BuildFilter(IReadOnlyList<TestMethodDescriptor> tests)
    {
        // Исключаем спецсимволы в FQN если необходимо
        var filterExpressions = tests
            .Select(t => $"FullyQualifiedName={t.FullyQualifiedName}");

        return string.Join("|", filterExpressions);
    }

    /// <summary>
    /// Извлекает статистику из консольного вывода dotnet test.
    /// Например: "Passed: 3, Failed: 0, Skipped: 0"
    /// </summary>
    private static (int Passed, int Failed, int Skipped) ParseSummary(string output)
    {
        var passed = 0;
        var failed = 0;
        var skipped = 0;

        // Поиск Passed! - Failed: 0, Passed: 3, Skipped: 0
        var passedMatch = Regex.Match(output, @"Passed:\s*(\d+)", RegexOptions.IgnoreCase);
        if (passedMatch.Success && int.TryParse(passedMatch.Groups[1].Value, out var p))
            passed = p;

        var failedMatch = Regex.Match(output, @"Failed:\s*(\d+)", RegexOptions.IgnoreCase);
        if (failedMatch.Success && int.TryParse(failedMatch.Groups[1].Value, out var f))
            failed = f;

        var skippedMatch = Regex.Match(output, @"Skipped:\s*(\d+)", RegexOptions.IgnoreCase);
        if (skippedMatch.Success && int.TryParse(skippedMatch.Groups[1].Value, out var s))
            skipped = s;

        return (passed, failed, skipped);
    }
}
