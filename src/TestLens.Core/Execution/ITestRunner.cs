using TestLens.Core.Execution.Models;
using TestLens.Core.Models;

namespace TestLens.Core.Execution;

/// <summary>
/// Сервис выполнения автоматизированных тестов.
/// </summary>
public interface ITestRunner
{
    /// <summary>
    /// Выполняет один тест.
    /// </summary>
    Task<TestRunResult> RunSingleTestAsync(
        string solutionOrProjectPath,
        TestMethodDescriptor test,
        Action<string>? onOutputLine = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Выполняет пакет тестов по запросу.
    /// </summary>
    Task<TestRunResult> RunTestsAsync(
        TestRunRequest request,
        Action<string>? onOutputLine = null,
        CancellationToken cancellationToken = default);
}
