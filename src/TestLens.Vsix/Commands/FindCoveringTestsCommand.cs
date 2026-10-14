using System;
using System.Linq;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using TestLens.Core.Analysis;
using TestLens.Core.Detection;
using TestLens.Core.Detection.Detectors;
using TestLens.Core.Models;
using TestLens.Vsix.ToolWindows;
using TestLens.Vsix.ViewModels;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace TestLens.Vsix.Commands;

[Command(PackageGuids.CommandSetString, PackageGuids.FindCoveringTestsId)]
internal sealed class FindCoveringTestsCommand : BaseCommand<FindCoveringTestsCommand>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e) =>
        await StartSearchAsync();

    private static System.Threading.CancellationTokenSource? _activeSearchCts;
    private static Task? _activeSearchTask;
    private static int _activeSearchId = 0;
    private static DocumentId? _lastDocumentId;
    private static int _lastMethodPosition = -1;

    public static void CancelActiveSearch() =>
        _activeSearchCts?.Cancel();

    public static async Task StartSearchAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        // 1. Cancel and await any previous in-flight search so tasks never overlap
        _activeSearchCts?.Cancel();
        _activeSearchCts?.Dispose();
        _activeSearchCts = null;

        if (_activeSearchTask != null)
        {
            try
            {
                await _activeSearchTask;
            }
            catch { }
        }

        var searchId = System.Threading.Interlocked.Increment(ref _activeSearchId);

        var componentModel = await VS.GetServiceAsync<SComponentModel, IComponentModel>();
        var workspace = componentModel?.GetService<VisualStudioWorkspace>();
        if (workspace == null)
        {
            await VS.MessageBox.ShowWarningAsync("TestLens", "Failed to connect to VisualStudioWorkspace.");
            return;
        }

        var docView = await VS.Documents.GetActiveDocumentViewAsync();
        var caretPosition = docView?.TextView?.Caret.Position.BufferPosition.Position ?? -1;
        var filePath = docView?.FilePath;

        Document? document = null;
        if (!string.IsNullOrEmpty(filePath))
        {
            var documentId = workspace.CurrentSolution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
            if (documentId != null)
                document = workspace.CurrentSolution.GetDocument(documentId);
        }

        // Fallback to last searched document if active document is not a C# code file in the solution
        if (document == null && _lastDocumentId != null && workspace.CurrentSolution.ContainsDocument(_lastDocumentId))
        {
            document = workspace.CurrentSolution.GetDocument(_lastDocumentId);
            if (caretPosition < 0)
                caretPosition = _lastMethodPosition;
        }

        if (document == null)
        {
            await VS.MessageBox.ShowWarningAsync("TestLens", "Please open a C# file containing a method or select a method first.");
            return;
        }

        var syntaxRoot = await document.GetSyntaxRootAsync();
        var semanticModel = await document.GetSemanticModelAsync();
        if (syntaxRoot == null || semanticModel == null)
            return;

        MethodDeclarationSyntax? methodDecl = null;
        if (caretPosition >= 0 && caretPosition < syntaxRoot.FullSpan.Length)
        {
            var token = syntaxRoot.FindToken(caretPosition);
            methodDecl = token.Parent?.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        }

        // Fallback to last method position in document if caret wasn't inside a method
        if (methodDecl == null && _lastMethodPosition >= 0 && _lastMethodPosition < syntaxRoot.FullSpan.Length)
        {
            var token = syntaxRoot.FindToken(_lastMethodPosition);
            methodDecl = token.Parent?.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        }

        if (methodDecl == null)
        {
            await VS.MessageBox.ShowWarningAsync("TestLens", "Place the caret inside a method declaration or body to find covering tests.");
            return;
        }

        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl) as IMethodSymbol;
        if (methodSymbol == null)
        {
            await VS.MessageBox.ShowWarningAsync("TestLens", "Failed to resolve method symbol.");
            return;
        }

        _lastDocumentId = document.Id;
        _lastMethodPosition = methodDecl.SpanStart;

        // Open Tool Window
        var toolWindow = await TestLensToolWindow.ShowAsync();
        if (toolWindow?.Content is not TestLensToolWindowControl control)
            return;

        control.ViewModel.Tests.Clear();
        control.ViewModel.TestsCount = 0;
        control.ViewModel.TargetMethodName = methodSymbol.Name;
        control.ViewModel.TargetMethodFqn = methodSymbol.ToDisplayString();
        control.ViewModel.TargetLocation = $"{document.FilePath}:{methodDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1}";
        control.ViewModel.CoverageState = CoverageState.Undetermined;
        control.ViewModel.IsLoading = true;
        control.ViewModel.StatusText = "Searching covering tests across dependency graph...";

        _activeSearchCts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(45));
        var searchToken = _activeSearchCts.Token;

        var progress = new System.Progress<string>(msg =>
        {
            if (searchId == _activeSearchId && control.ViewModel.IsLoading)
                control.ViewModel.StatusText = msg;
        });

        var testProgress = new System.Progress<CoveringTestMatch>(match =>
        {
            if (searchId == _activeSearchId && control.ViewModel.IsLoading)
                control.ViewModel.AddOrUpdateDiscoveredMatch(match);
        });

        // Background analysis
        _activeSearchTask = Task.Run(async () =>
        {
            try
            {
                var finder = TestLensPackage.Services?.GetService<ICoveringTestFinder>()
                    ?? new RoslynInvertedCallGraphWalker(
                        new CallerResolver(),
                        new CompositeTestDetector(new ITestFrameworkDetector[]
                        {
                            new XUnitTestDetector(),
                            new NUnitTestDetector(),
                            new MSTestDetector()
                        }));

                var options = new CoverageSearchOptions
                {
                    MaxCallDepth = 5,
                    MaxExploredMethods = 300,
                    MaxCallersPerMethod = 50,
                    FollowInterfaces = true,
                    FollowOverrides = true
                };

                var result = await finder.FindCoveringTestsAsync(workspace.CurrentSolution, methodSymbol, options, progress, testProgress, searchToken);

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (searchId != _activeSearchId)
                    return;

                control.ViewModel.IsLoading = false;
                control.ViewModel.UpdateFromResult(result);

                if (searchToken.IsCancellationRequested)
                {
                    if (result.CoveringTests.Count == 0)
                        control.ViewModel.CoverageState = CoverageState.Undetermined;
                    else
                        control.ViewModel.CoverageState = CoverageState.Covered;

                    control.ViewModel.StatusText = result.CoveringTests.Count == 0
                        ? $"Search stopped: no tests found yet ({result.ElapsedTime.TotalSeconds:F1}s)"
                        : $"Search stopped: found {result.CoveringTests.Count} test(s) ({result.ElapsedTime.TotalSeconds:F1}s)";
                }
            }
            catch (OperationCanceledException)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (searchId != _activeSearchId)
                    return;

                control.ViewModel.IsLoading = false;
                if (control.ViewModel.Tests.Count == 0)
                    control.ViewModel.CoverageState = CoverageState.Undetermined;
                else
                    control.ViewModel.CoverageState = CoverageState.Covered;

                control.ViewModel.StatusText = control.ViewModel.Tests.Count == 0
                    ? "Search was stopped: no tests found yet."
                    : $"Search stopped: found {control.ViewModel.Tests.Count} test(s).";
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (searchId != _activeSearchId)
                    return;

                control.ViewModel.IsLoading = false;
                control.ViewModel.StatusText = $"Error during search: {ex.Message}";
            }
        }, searchToken);
    }
}
