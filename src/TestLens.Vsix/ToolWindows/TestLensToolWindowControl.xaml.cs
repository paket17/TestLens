using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Community.VisualStudio.Toolkit;
using EnvDTE;
using TestLens.Core.Execution;
using TestLens.Core.Execution.Models;
using TestLens.Vsix.Commands;
using TestLens.Vsix.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;

namespace TestLens.Vsix.ToolWindows;

public partial class TestLensToolWindowControl : UserControl
{
    private readonly ITestRunner _testRunner;
    public TestLensViewModel ViewModel { get; } = new();

    private System.Threading.CancellationTokenSource? _testRunCts;

    public TestLensToolWindowControl()
    {
        InitializeComponent();
        _testRunner = TestLensPackage.Services?.GetService<ITestRunner>() ?? new DotNetCliTestRunner();
        DataContext = ViewModel;
    }

    private void BtnRunAll_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsLoading)
            return;

        _ = RunAllTestsAsync();
    }

    private async Task RunAllTestsAsync()
    {
        if (ViewModel.Tests.Count == 0)
            return;

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var dte = await VS.GetServiceAsync<DTE, DTE>();
        var solutionPath = await ResolveSolutionPathAsync(dte);
        if (string.IsNullOrEmpty(solutionPath))
        {
            await VS.MessageBox.ShowWarningAsync("TestLens", "Could not locate active solution or project file (.sln).");
            return;
        }

        _testRunCts?.Cancel();
        _testRunCts?.Dispose();
        _testRunCts = new System.Threading.CancellationTokenSource();
        var cancellationToken = _testRunCts.Token;

        var testsToRun = ViewModel.Tests.Select(t => t.Match.TestMethod).ToList();
        var request = new TestRunRequest
        {
            SolutionOrProjectPath = solutionPath!,
            TestsToRun = testsToRun
        };

        foreach (var t in ViewModel.Tests)
            t.Status = TestExecutionStatus.Running;

        ViewModel.IsLoading = true;
        ViewModel.StatusText = $"Running {testsToRun.Count} covering test(s)...";

        try
        {
            var pane = await VS.Windows.CreateOutputWindowPaneAsync("TestLens");
            if (pane != null)
            {
                await pane.ActivateAsync();
                await pane.ClearAsync();
                await pane.WriteLineAsync($"=== TestLens: Running {testsToRun.Count} covering test(s) ===");
                await pane.WriteLineAsync($"Solution: {solutionPath}");
                await pane.WriteLineAsync(string.Empty);
            }

            if (dte != null && ViewModel.Tests.Count > 0)
            {
                var firstTest = ViewModel.Tests[0];
                await OpenDocumentAtLineAsync(firstTest.Match.TestMethod.FilePath, firstTest.Match.TestMethod.LineNumber, dte);
                await TriggerTestExplorerAsync(dte);
            }

            var result = await _testRunner.RunTestsAsync(request, line =>
            {
                pane?.WriteLine(line);
            }, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                ViewModel.StatusText = "Test execution stopped.";
                ResetRunningTests();
                return;
            }

            if (pane != null)
            {
                await pane.WriteLineAsync(string.Empty);
                await pane.WriteLineAsync($"=== Result: {(result.Success ? "PASSED" : "FAILED")} | Passed: {result.PassedCount}, Failed: {result.FailedCount}, Skipped: {result.SkippedCount}, Duration: {result.Duration.TotalSeconds:F2}s ===");
            }

            UpdateAllTestsStatus(result);
        }
        catch (OperationCanceledException)
        {
            ViewModel.StatusText = "Test execution stopped.";
            ResetRunningTests();
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"Error executing tests: {ex.Message}";
            await VS.MessageBox.ShowErrorAsync("TestLens", $"Error running tests: {ex.Message}");
        }
        finally
        {
            ViewModel.IsLoading = false;
        }
    }

    private void UpdateAllTestsStatus(TestRunResult result)
    {
        var passed = result.FailedCount == 0 && (result.PassedCount > 0 || result.Success);
        var targetStatus = passed ? TestExecutionStatus.Passed : TestExecutionStatus.Failed;

        foreach (var t in ViewModel.Tests)
            t.Status = targetStatus;

        if (result.FailedCount == 0 && result.PassedCount > 0)
            ViewModel.StatusText = $"Tests passed: {result.PassedCount} passed ({result.Duration.TotalSeconds:F2}s)";
        else if (result.FailedCount > 0)
            ViewModel.StatusText = $"Tests failed: {result.FailedCount} failed, {result.PassedCount} passed ({result.Duration.TotalSeconds:F2}s)";
        else if (!result.Success)
            ViewModel.StatusText = "Test execution failed. See TestLens Output window for details.";
        else
            ViewModel.StatusText = $"Completed: {result.PassedCount} passed ({result.Duration.TotalSeconds:F2}s)";
    }

    private void ResetRunningTests()
    {
        foreach (var t in ViewModel.Tests)
        {
            if (t.Status == TestExecutionStatus.Running)
                t.Status = TestExecutionStatus.NotRun;
        }
    }

    private void BtnRunSingleTest_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsLoading)
            return;

        if (sender is FrameworkElement { DataContext: CoveringTestItemViewModel item })
            _ = RunSingleTestAsync(item);
    }

    private async Task RunSingleTestAsync(CoveringTestItemViewModel item)
    {
        if (ViewModel.IsLoading)
            return;

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var dte = await VS.GetServiceAsync<DTE, DTE>();
        var solutionPath = await ResolveSolutionPathAsync(dte);
        if (string.IsNullOrEmpty(solutionPath))
        {
            await VS.MessageBox.ShowWarningAsync("TestLens", "Could not locate active solution or project file (.sln).");
            return;
        }

        _testRunCts?.Cancel();
        _testRunCts?.Dispose();
        _testRunCts = new System.Threading.CancellationTokenSource();
        var cancellationToken = _testRunCts.Token;

        item.Status = TestExecutionStatus.Running;
        ViewModel.IsLoading = true;
        ViewModel.StatusText = $"Running test '{item.DisplayName}'...";

        await OpenDocumentAtLineAsync(item.Match.TestMethod.FilePath, item.Match.TestMethod.LineNumber, dte);
        if (dte != null)
            await TriggerTestExplorerAsync(dte);

        try
        {
            var pane = await VS.Windows.CreateOutputWindowPaneAsync("TestLens");
            if (pane != null)
            {
                await pane.ActivateAsync();
                await pane.WriteLineAsync($"=== TestLens: Running test: {item.FullyQualifiedName} ===");
            }

            var result = await _testRunner.RunSingleTestAsync(solutionPath!, item.Match.TestMethod, line =>
            {
                pane?.WriteLine(line);
            }, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                item.Status = TestExecutionStatus.NotRun;
                ViewModel.StatusText = $"Test '{item.DisplayName}' stopped.";
                return;
            }

            if (pane != null)
                await pane.WriteLineAsync($"=== Test completed: {(result.Success ? "PASSED" : "FAILED")} (Passed: {result.PassedCount}, Failed: {result.FailedCount}, Duration: {result.Duration.TotalSeconds:F2}s) ===");

            if (result.FailedCount == 0 && (result.PassedCount > 0 || result.Success))
            {
                item.Status = TestExecutionStatus.Passed;
                ViewModel.StatusText = $"Test '{item.DisplayName}' passed ({result.Duration.TotalSeconds:F2}s)";
            }
            else
            {
                item.Status = TestExecutionStatus.Failed;
                ViewModel.StatusText = $"Test '{item.DisplayName}' failed ({result.Duration.TotalSeconds:F2}s)";
            }
        }
        catch (OperationCanceledException)
        {
            item.Status = TestExecutionStatus.NotRun;
            ViewModel.StatusText = $"Test '{item.DisplayName}' stopped.";
        }
        catch (Exception ex)
        {
            item.Status = TestExecutionStatus.Failed;
            ViewModel.StatusText = $"Error running test: {ex.Message}";
            await VS.MessageBox.ShowErrorAsync("TestLens", $"Error running test: {ex.Message}");
        }
        finally
        {
            ViewModel.IsLoading = false;
        }
    }

    private void BtnNavigateTest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CoveringTestItemViewModel item })
            _ = OpenDocumentAtLineAsync(item.Match.TestMethod.FilePath, item.Match.TestMethod.LineNumber);
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsLoading)
        {
            FindCoveringTestsCommand.CancelActiveSearch();
            _testRunCts?.Cancel();
            ViewModel.StatusText = "Cancellation requested...";
            return;
        }

        _ = RefreshTestsAsync();
    }

    private async Task RefreshTestsAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        await FindCoveringTestsCommand.StartSearchAsync();
    }

    private void TreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeView tree)
            return;

        if (tree.SelectedItem is CoveringTestItemViewModel testVm)
            _ = OpenDocumentAtLineAsync(testVm.Match.TestMethod.FilePath, testVm.Match.TestMethod.LineNumber);
        else if (tree.SelectedItem is CallChainStepViewModel stepVm)
            _ = OpenDocumentAtLineAsync(stepVm.Step.FilePath, stepVm.Step.LineNumber);
    }

    private void TreeView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && sender is DependencyObject dep)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(dep);
            if (scrollViewer != null)
            {
                if (e.Delta < 0)
                    scrollViewer.LineRight();
                else
                    scrollViewer.LineLeft();

                e.Handled = true;
            }
        }
    }

    private static async Task OpenDocumentAtLineAsync(string? filePath, int lineNumber = 0, DTE? dte = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            await VS.MessageBox.ShowWarningAsync("TestLens", "Source file location is not available.");
            return;
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        try
        {
            if (!File.Exists(filePath))
            {
                await VS.MessageBox.ShowWarningAsync("TestLens", $"File does not exist: {filePath}");
                return;
            }

            dte ??= await VS.GetServiceAsync<DTE, DTE>();
            if (dte != null)
            {
                dte.ItemOperations.OpenFile(filePath);
                if (lineNumber > 0 && dte.ActiveDocument?.Selection is TextSelection sel)
                    sel.GotoLine(lineNumber, false);
                return;
            }

            var docView = await VS.Documents.OpenAsync(filePath!);
            if (docView?.TextView != null && lineNumber > 0)
            {
                var line = docView.TextBuffer?.CurrentSnapshot.GetLineFromLineNumber(Math.Max(0, lineNumber - 1));
                if (line != null)
                    docView.TextView.Caret.MoveTo(line.Start);
            }
        }
        catch (Exception ex)
        {
            await VS.MessageBox.ShowErrorAsync("TestLens", $"Failed to open file: {ex.Message}");
        }
    }

    private static async Task TriggerTestExplorerAsync(DTE dte)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        try
        {
            dte.ExecuteCommand("TestExplorer.ShowTestExplorer");
        }
        catch { }

        try
        {
            dte.ExecuteCommand("Test.RunTestsInCurrentContext");
            return;
        }
        catch { }

        try
        {
            dte.ExecuteCommand("TestExplorer.RunSelectedTests");
            return;
        }
        catch { }

        try
        {
            dte.ExecuteCommand("EditorContextMenus.CodeWindow.RunTests");
        }
        catch { }
    }

    private static async Task<string?> ResolveSolutionPathAsync(DTE? dte)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var solutionPath = dte?.Solution?.FullName;
        if (!string.IsNullOrEmpty(solutionPath) && File.Exists(solutionPath))
            return solutionPath;

        var componentModel = await VS.GetServiceAsync<SComponentModel, IComponentModel>();
        var workspace = componentModel?.GetService<VisualStudioWorkspace>();
        var wsSolutionPath = workspace?.CurrentSolution?.FilePath;
        if (!string.IsNullOrEmpty(wsSolutionPath) && File.Exists(wsSolutionPath))
            return wsSolutionPath;

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
                return descendant;
        }

        return null;
    }

    private void MenuCopyTestName_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: CoveringTestItemViewModel item })
            Clipboard.SetText(item.DisplayName);
    }

    private void MenuCopyTestFqn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: CoveringTestItemViewModel item })
            Clipboard.SetText(item.FullyQualifiedName);
    }

    private void MenuCopyTestFilePath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: CoveringTestItemViewModel item } && !string.IsNullOrEmpty(item.Match.TestMethod.FilePath))
            Clipboard.SetText(item.Match.TestMethod.FilePath!);
    }

    private void MenuCopyStepCall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: CallChainStepViewModel step })
            Clipboard.SetText($"{step.CallerName} -> {step.CalleeName} [{step.StepKind}]");
    }

    private void MenuCopyStepLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: CallChainStepViewModel step })
            Clipboard.SetText(step.Location);
    }
}
