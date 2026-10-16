using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using TestLens.Core.Models;

namespace TestLens.Vsix.ViewModels;

public enum TestExecutionStatus
{
    NotRun,
    Running,
    Passed,
    Failed,
    Skipped
}

public enum CoverageState
{
    Undetermined,
    Covered,
    NotCovered
}

public sealed class TestLensViewModel : ViewModelBase
{
    private static readonly Brush NeutralBackground = CreateFrozenBrush(0x2a, 0x38, 0x82, 0xf6);
    private static readonly Brush NeutralBorder = CreateFrozenBrush(0x55, 0x38, 0x82, 0xf6);
    private static readonly Brush NeutralText = CreateFrozenBrush(0xff, 0x93, 0xc5, 0xfd);

    private static readonly Brush CoveredBackground = CreateFrozenBrush(0x22, 0x22, 0xc5, 0x5e);
    private static readonly Brush CoveredBorder = CreateFrozenBrush(0x50, 0x22, 0xc5, 0x5e);
    private static readonly Brush CoveredText = CreateFrozenBrush(0xff, 0x4a, 0xde, 0x80);

    private static readonly Brush NotCoveredBackground = CreateFrozenBrush(0x22, 0xef, 0x44, 0x44);
    private static readonly Brush NotCoveredBorder = CreateFrozenBrush(0x50, 0xef, 0x44, 0x44);
    private static readonly Brush NotCoveredText = CreateFrozenBrush(0xff, 0xf8, 0x71, 0x71);

    private string _targetMethodName = "No method selected";
    private string _targetMethodFqn = string.Empty;
    private string _targetLocation = string.Empty;
    private string _statusText = "Select a method in the code editor and right-click 'TestLens: Find Covering Tests...'";
    private bool _isCovered;
    private CoverageState _coverageState = CoverageState.Undetermined;
    private int _testsCount;
    private string _elapsedTimeText = string.Empty;

    private bool _isSearching;
    private bool _isRunningAllTests;
    private bool _isRunningSingleTest;

    public string TargetMethodName
    {
        get => _targetMethodName;
        set => SetProperty(ref _targetMethodName, value);
    }

    public string TargetMethodFqn
    {
        get => _targetMethodFqn;
        set => SetProperty(ref _targetMethodFqn, value);
    }

    public string TargetLocation
    {
        get => _targetLocation;
        set => SetProperty(ref _targetLocation, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsCovered
    {
        get => _isCovered;
        set => SetProperty(ref _isCovered, value);
    }

    public CoverageState CoverageState
    {
        get => _coverageState;
        set
        {
            if (!SetProperty(ref _coverageState, value))
                return;

            OnPropertyChanged(nameof(StatusBadgeBackground));
            OnPropertyChanged(nameof(StatusBadgeBorderBrush));
            OnPropertyChanged(nameof(StatusTextColor));
        }
    }

    public Brush StatusBadgeBackground => CoverageState switch
    {
        CoverageState.Covered => CoveredBackground,
        CoverageState.NotCovered => NotCoveredBackground,
        _ => NeutralBackground
    };

    public Brush StatusBadgeBorderBrush => CoverageState switch
    {
        CoverageState.Covered => CoveredBorder,
        CoverageState.NotCovered => NotCoveredBorder,
        _ => NeutralBorder
    };

    public Brush StatusTextColor => CoverageState switch
    {
        CoverageState.Covered => CoveredText,
        CoverageState.NotCovered => NotCoveredText,
        _ => NeutralText
    };

    public bool IsSearching
    {
        get => _isSearching;
        set
        {
            if (SetProperty(ref _isSearching, value))
                NotifyExecutionStateChanged();
        }
    }

    public bool IsRunningAllTests
    {
        get => _isRunningAllTests;
        set
        {
            if (SetProperty(ref _isRunningAllTests, value))
                NotifyExecutionStateChanged();
        }
    }

    public bool IsRunningSingleTest
    {
        get => _isRunningSingleTest;
        set
        {
            if (SetProperty(ref _isRunningSingleTest, value))
                NotifyExecutionStateChanged();
        }
    }

    public bool IsRunningTests => _isRunningAllTests || _isRunningSingleTest;
    public bool IsLoading => _isSearching || IsRunningTests;
    public bool IsRunAllEnabled => !_isSearching && (!_isRunningSingleTest || _isRunningAllTests);

    public string RefreshButtonText => IsSearching ? "⏹ Stop" : "🔄 Refresh";
    public string RefreshButtonToolTip => IsSearching ? "Stop covering test search" : "Refresh covering tests";

    public string RunAllButtonText => IsRunningAllTests ? "⏹ Stop" : "▶ Run All";
    public string RunAllButtonToolTip => IsRunningAllTests ? "Stop test execution" : "Run all covering tests";

    public int TestsCount
    {
        get => _testsCount;
        set => SetProperty(ref _testsCount, value);
    }

    public string ElapsedTimeText
    {
        get => _elapsedTimeText;
        set => SetProperty(ref _elapsedTimeText, value);
    }

    public ObservableCollection<CoveringTestItemViewModel> Tests { get; } = [];

    public void ResetForSearch(string methodName, string methodFqn, string location)
    {
        Tests.Clear();
        TestsCount = 0;
        TargetMethodName = methodName;
        TargetMethodFqn = methodFqn;
        TargetLocation = location;
        CoverageState = CoverageState.Undetermined;
        IsSearching = true;
        StatusText = "Searching covering tests across dependency graph...";
    }

    public void AddOrUpdateDiscoveredMatch(CoveringTestMatch match)
    {
        var existing = Tests.FirstOrDefault(t =>
            string.Equals(t.FullyQualifiedName, match.TestMethod.FullyQualifiedName, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
            Tests.Add(new CoveringTestItemViewModel(match, this));
        else
            existing.UpdateMatch(match);

        TestsCount = Tests.Count;
        IsCovered = Tests.Count > 0;
        if (IsCovered)
            CoverageState = CoverageState.Covered;
    }

    public void UpdateFromResult(MethodCoverageResult result)
    {
        TargetMethodName = result.TargetMethodName;
        TargetMethodFqn = result.TargetMethodFullyQualifiedName;
        TargetLocation = $"{result.TargetFilePath}:{result.TargetLineNumber}";
        IsCovered = result.IsCovered;
        CoverageState = result.IsCovered ? CoverageState.Covered : CoverageState.NotCovered;
        TestsCount = result.TotalCoveringTestsCount;
        ElapsedTimeText = $"Analysis completed in {result.ElapsedTime.TotalMilliseconds:F0} ms";

        StatusText = result.IsCovered
            ? $"Covered ({result.TotalCoveringTestsCount} {(result.TotalCoveringTestsCount == 1 ? "test" : "tests")})"
            : "Not covered by tests (0 calls found)";

        Tests.Clear();
        foreach (var match in result.CoveringTests)
            Tests.Add(new CoveringTestItemViewModel(match, this));
    }

    private void NotifyExecutionStateChanged()
    {
        OnPropertyChanged(nameof(IsRunningTests));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsRunAllEnabled));
        OnPropertyChanged(nameof(RefreshButtonText));
        OnPropertyChanged(nameof(RefreshButtonToolTip));
        OnPropertyChanged(nameof(RunAllButtonText));
        OnPropertyChanged(nameof(RunAllButtonToolTip));

        foreach (var test in Tests)
            test.NotifyRunButtonState();
    }

    private static Brush CreateFrozenBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}

public sealed class CoveringTestItemViewModel : ViewModelBase
{
    private readonly TestLensViewModel? _parent;
    private TestExecutionStatus _status = TestExecutionStatus.NotRun;
    private bool _isRunningSelf;
    private string _executionMessage = string.Empty;

    public CoveringTestItemViewModel(CoveringTestMatch match, TestLensViewModel? parent = null)
    {
        Match = match;
        _parent = parent;
        foreach (var c in match.CallChains)
            Chains.Add(new CallChainViewModel(c));
    }

    public CoveringTestMatch Match { get; private set; }

    public string DisplayName => Match.TestMethod.MethodName;
    public string FullyQualifiedName => Match.TestMethod.FullyQualifiedName;
    public string Framework => Match.TestMethod.Framework.ToString();
    public int MinDepth => Match.MinDepth;
    public bool HasDirectCall => Match.HasDirectCall;

    public string DepthText => Match.HasDirectCall
        ? "Direct Call (Depth 1)"
        : $"Indirect Call (Depth {Match.MinDepth})";

    public bool IsRunningSelf
    {
        get => _isRunningSelf;
        set
        {
            if (!SetProperty(ref _isRunningSelf, value))
                return;

            OnPropertyChanged(nameof(RunButtonText));
            OnPropertyChanged(nameof(RunButtonToolTip));
            OnPropertyChanged(nameof(IsRunButtonEnabled));
        }
    }

    public bool IsRunButtonEnabled => IsRunningSelf || !(_parent?.IsLoading ?? false);

    public string RunButtonText => IsRunningSelf ? "⏹ Stop" : "▶ Run";
    public string RunButtonToolTip => IsRunningSelf
        ? "Stop this test"
        : (_parent?.IsLoading ?? false ? "Execution or search in progress" : "Run this test");

    public TestExecutionStatus Status
    {
        get => _status;
        set
        {
            if (!SetProperty(ref _status, value))
                return;

            OnPropertyChanged(nameof(StatusIcon));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(StatusBadgeText));
        }
    }

    public string ExecutionMessage
    {
        get => _executionMessage;
        set => SetProperty(ref _executionMessage, value);
    }

    public string StatusIcon => Status switch
    {
        TestExecutionStatus.Running => "⏳",
        TestExecutionStatus.Passed => "✔️",
        TestExecutionStatus.Failed => "❌",
        TestExecutionStatus.Skipped => "⚠",
        _ => "🧪"
    };

    public string StatusColor => Status switch
    {
        TestExecutionStatus.Passed => "#16a34a",
        TestExecutionStatus.Failed => "#dc2626",
        TestExecutionStatus.Skipped => "#eab308",
        TestExecutionStatus.Running => "#2563eb",
        _ => "#94a3b8"
    };

    public string StatusBadgeText => Status switch
    {
        TestExecutionStatus.Running => "Running...",
        TestExecutionStatus.Passed => "Passed",
        TestExecutionStatus.Failed => "Failed",
        TestExecutionStatus.Skipped => "Skipped",
        _ => string.Empty
    };

    public ObservableCollection<CallChainViewModel> Chains { get; } = [];

    public void UpdateMatch(CoveringTestMatch updatedMatch)
    {
        Match = updatedMatch;
        Chains.Clear();
        foreach (var c in updatedMatch.CallChains)
            Chains.Add(new CallChainViewModel(c));

        OnPropertyChanged(nameof(Match));
        OnPropertyChanged(nameof(MinDepth));
        OnPropertyChanged(nameof(HasDirectCall));
        OnPropertyChanged(nameof(DepthText));
    }

    public void NotifyRunButtonState()
    {
        OnPropertyChanged(nameof(IsRunButtonEnabled));
        OnPropertyChanged(nameof(RunButtonToolTip));
    }
}

public sealed class CallChainViewModel(CallChain chain)
{
    public CallChain Chain { get; } = chain;
    public ObservableCollection<CallChainStepViewModel> Steps { get; } =
        new(chain.Steps.Select(s => new CallChainStepViewModel(s)));
}

public sealed class CallChainStepViewModel(CallChainStep step)
{
    public CallChainStep Step { get; } = step;

    public string CallerName => Step.CallerName;
    public string CalleeName => Step.CalleeName;
    public string Location => $"{Step.FilePath}:{Step.LineNumber}";
    public string? CodeSnippet => Step.CodeSnippet;
    public string StepKind => Step.StepKind != CallChainStepKind.DirectCall ? $"[{Step.StepKind}]" : string.Empty;
}
