using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Solution = Microsoft.CodeAnalysis.Solution;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using TestLens.Core;
using TestLens.Core.Analysis;
using TestLens.Core.Models;
using TestLens.Vsix.ToolWindows;
using TestLens.Vsix.ViewModels;

namespace TestLens.Vsix.Commands
{

[Command(PackageGuids.CommandSetString, PackageGuids.FindCoveringTestsId)]
internal sealed class FindCoveringTestsCommand : BaseCommand<FindCoveringTestsCommand>
{
    private static CancellationTokenSource? _activeSearchCts;
    private static Task? _activeSearchTask;
    private static int _activeSearchId;

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e) =>
        await StartSearchAsync();

    public static void CancelActiveSearch() =>
        _activeSearchCts?.Cancel();

    public static async Task StartSearchAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        await EnsurePreviousSearchCompletedAsync();
        var searchId = Interlocked.Increment(ref _activeSearchId);

        var control = await ShowToolWindowControlAsync();
        if (control == null)
            return;

        var workspace = await ResolveWorkspaceAsync();
        if (workspace == null)
        {
            control.ViewModel.CoverageState = CoverageState.Undetermined;
            control.ViewModel.IsSearching = false;
            control.ViewModel.StatusText = "Failed to connect to Visual Studio workspace.";
            return;
        }

        var (target, statusMessage) = await ResolveTargetMethodAsync(workspace);
        if (target == null)
        {
            control.ViewModel.CoverageState = CoverageState.Undetermined;
            control.ViewModel.IsSearching = false;
            control.ViewModel.StatusText = statusMessage ?? "Place caret on a method and click '🔄 Refresh' to find covering tests.";
            return;
        }

        var targetLine = target.MethodDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        control.ViewModel.ResetForSearch(
            target.MethodSymbol.Name,
            target.MethodSymbol.ToDisplayString(),
            $"{target.Document.FilePath}:{targetLine}");

        _activeSearchCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var searchToken = _activeSearchCts.Token;

        var progress = new Progress<string>(msg =>
        {
            if (searchId == _activeSearchId && control.ViewModel.IsSearching)
                control.ViewModel.StatusText = msg;
        });

        var testProgress = new Progress<CoveringTestMatch>(match =>
        {
            if (searchId == _activeSearchId && control.ViewModel.IsSearching)
                control.ViewModel.AddOrUpdateDiscoveredMatch(match);
        });

        _activeSearchTask = Task.Run(() => RunSearchInBackgroundAsync(
            workspace.CurrentSolution,
            target.MethodSymbol,
            control.ViewModel,
            searchId,
            progress,
            testProgress,
            searchToken), searchToken);
    }

    private static async Task EnsurePreviousSearchCompletedAsync()
    {
        _activeSearchCts?.Cancel();
        _activeSearchCts?.Dispose();
        _activeSearchCts = null;

        if (_activeSearchTask != null)
        {
            try
            {
                await _activeSearchTask;
            }
            catch
            {
                // Ignore cancellations or failures from previous runs
            }
        }
    }

    private static async Task<VisualStudioWorkspace?> ResolveWorkspaceAsync()
    {
        var componentModel = await VS.GetServiceAsync<SComponentModel, IComponentModel>();
        return componentModel?.GetService<VisualStudioWorkspace>();
    }

    private static async Task<TestLensToolWindowControl?> ShowToolWindowControlAsync()
    {
        var toolWindow = await TestLensToolWindow.ShowAsync();
        return toolWindow?.Content as TestLensToolWindowControl;
    }

    private static async Task RunSearchInBackgroundAsync(
        Solution solution,
        IMethodSymbol targetSymbol,
        TestLensViewModel viewModel,
        int searchId,
        IProgress<string> progress,
        IProgress<CoveringTestMatch> testProgress,
        CancellationToken searchToken)
    {
        try
        {
            var finder = TestLensServices.CreateFinder();
            var options = new CoverageSearchOptions
            {
                MaxCallDepth = viewModel.SearchDepth > 0 ? viewModel.SearchDepth : 10,
                MaxExploredMethods = 300,
                MaxCallersPerMethod = 50,
                FollowInterfaces = true,
                FollowOverrides = true
            };

            var result = await finder.FindCoveringTestsAsync(
                solution,
                targetSymbol,
                options,
                progress,
                testProgress,
                searchToken);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (searchId != _activeSearchId)
                return;

            viewModel.IsSearching = false;
            viewModel.UpdateFromResult(result);

            if (searchToken.IsCancellationRequested)
                ApplySearchStoppedStatus(viewModel, result.CoveringTests.Count, result.ElapsedTime.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (searchId != _activeSearchId)
                return;

            viewModel.IsSearching = false;
            ApplySearchStoppedStatus(viewModel, viewModel.Tests.Count);
        }
        catch (Exception ex)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (searchId != _activeSearchId)
                return;

            viewModel.IsSearching = false;
            viewModel.CoverageState = CoverageState.Undetermined;
            viewModel.StatusText = $"Error during search: {ex.Message}";
        }
    }

    private static async Task<(TargetMethodContext? Target, string? StatusMessage)> ResolveTargetMethodAsync(VisualStudioWorkspace workspace)
    {
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

        if (document == null)
            return (null, "Please open a C# file containing a method or select a method first.");

        if (caretPosition < 0)
            return (null, "Place caret on a method and click '🔄 Refresh' to find covering tests.");

        var syntaxRoot = await document.GetSyntaxRootAsync();
        var semanticModel = await document.GetSemanticModelAsync();
        if (syntaxRoot == null || semanticModel == null)
            return (null, "Failed to load syntax or semantic model for the document.");

        var methodDecl = FindMethodDeclaration(syntaxRoot, caretPosition);
        if (methodDecl == null)
            return (null, "Place caret on a method and click '🔄 Refresh' to find covering tests.");

        if (semanticModel.GetDeclaredSymbol(methodDecl) is not IMethodSymbol methodSymbol)
            return (null, "Failed to resolve method symbol at caret.");

        return (new TargetMethodContext(document, methodDecl, methodSymbol), null);
    }

    private static MethodDeclarationSyntax? FindMethodDeclaration(SyntaxNode syntaxRoot, int position)
    {
        if (position >= 0 && position < syntaxRoot.FullSpan.Length)
        {
            var token = syntaxRoot.FindToken(position);
            return token.Parent?.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        }

        return null;
    }

    private static void ApplySearchStoppedStatus(TestLensViewModel viewModel, int count, double? elapsedSeconds = null)
    {
        viewModel.CoverageState = count > 0 ? CoverageState.Covered : CoverageState.Undetermined;
        var elapsed = elapsedSeconds.HasValue ? $" ({elapsedSeconds.Value:F1}s)" : string.Empty;

        viewModel.StatusText = count == 0
            ? $"Search stopped: no tests found yet{elapsed}."
            : $"Search stopped: found {count} test(s){elapsed}.";
    }

    private sealed record TargetMethodContext(
        Document Document, 
        MethodDeclarationSyntax MethodDecl, 
        IMethodSymbol MethodSymbol);
}
}

namespace System.Runtime.CompilerServices
{
    // Required by the C# compiler to enable 'record' and 'init' property accessors in .NET Framework 4.8.
    // In modern .NET (6/8/10) this marker type is built-in, but in .NET Framework 4.8 it is absent from the BCL.
    internal static class IsExternalInit
    {
    }
}
