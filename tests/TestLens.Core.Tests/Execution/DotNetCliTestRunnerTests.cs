using TestLens.Core.Execution;
using TestLens.Core.Execution.Models;
using TestLens.Core.Models;

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
            TestsToRun = Array.Empty<TestMethodDescriptor>()
        };

        var result = await runner.RunTestsAsync(request);

        Assert.True(result.Success);
        Assert.Equal(0, result.PassedCount);
        Assert.Equal(0, result.FailedCount);
    }
}
