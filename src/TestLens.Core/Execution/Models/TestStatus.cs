namespace TestLens.Core.Execution.Models;

/// <summary>
/// Статус выполнения теста.
/// </summary>
public enum TestStatus
{
    NotRun = 0,
    Passed = 1,
    Failed = 2,
    Skipped = 3,
    Error = 4
}
