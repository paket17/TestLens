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
    private static readonly Regex PassedRegex = new(@"Passed:\s*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FailedRegex = new(@"Failed:\s*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SkippedRegex = new(@"Skipped:\s*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
                StandardOutput = "No tests to run."
            };
        }

        var filter = BuildFilter(request.TestsToRun);
        var sb = new StringBuilder();
        sb.Append("test ");
        sb.Append($"\"{request.SolutionOrProjectPath}\" ");
        sb.Append($"--filter \"{filter}\" ");
        sb.Append("--logger \"console;verbosity=normal\" ");

        if (request.NoBuild)
            sb.Append("--no-build ");

        var workingDirectory = File.Exists(request.SolutionOrProjectPath)
            ? Path.GetDirectoryName(request.SolutionOrProjectPath)
            : Directory.Exists(request.SolutionOrProjectPath)
                ? request.SolutionOrProjectPath
                : string.Empty;

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = sb.ToString(),
            WorkingDirectory = workingDirectory ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var outputBuilder = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();

        using var process = new Process { StartInfo = startInfo };

        try
        {
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
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch { }

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
            logger?.LogError(ex, "Error running 'dotnet test'");
            return new TestRunResult
            {
                Success = false,
                Duration = stopwatch.Elapsed,
                StandardOutput = outputBuilder.ToString(),
                ErrorMessage = ex.Message
            };
        }
    }

    private static string BuildFilter(IReadOnlyList<TestMethodDescriptor> tests)
    {
        var filterExpressions = tests
            .Select(t => $"FullyQualifiedName~{t.FullyQualifiedName}");

        return string.Join("|", filterExpressions);
    }

    /// <summary>
    /// Parses test execution summary from dotnet test console output.
    /// Example: "Passed: 3, Failed: 0, Skipped: 0"
    /// </summary>
    private static (int Passed, int Failed, int Skipped) ParseSummary(string output)
    {
        var passed = 0;
        var failed = 0;
        var skipped = 0;

        var passedMatch = PassedRegex.Match(output);
        if (passedMatch.Success && int.TryParse(passedMatch.Groups[1].Value, out var p))
            passed = p;

        var failedMatch = FailedRegex.Match(output);
        if (failedMatch.Success && int.TryParse(failedMatch.Groups[1].Value, out var f))
            failed = f;

        var skippedMatch = SkippedRegex.Match(output);
        if (skippedMatch.Success && int.TryParse(skippedMatch.Groups[1].Value, out var s))
            skipped = s;

        if (passed == 0 && failed == 0 && output.Contains("Passed!"))
            passed = 1;

        return (passed, failed, skipped);
    }
}
