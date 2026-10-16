using TestLens.Core.Execution;
using TestLens.Core.Execution.Models;

namespace TestLens.Core.Tests.Execution;

public class DotNetCliTestRunnerTests
{
    [Fact]
    public async Task RunTestsAsync_WithEmptyList_ShouldReturnSuccessImmediately()
    {
        var runner = new DotNetCliTestRunner();
        var request = new TestRunRequest
        {
            SolutionOrProjectPath = "dummy.sln",
            TestsToRun = []
        };

        var result = await runner.RunTestsAsync(request);

        result.Success.Should().BeTrue();
        result.PassedCount.Should().Be(0);
        result.FailedCount.Should().Be(0);
    }
}
