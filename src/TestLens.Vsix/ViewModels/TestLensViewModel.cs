using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using TestLens.Core.Models;

namespace TestLens.Vsix.ViewModels;

public enum TestExecutionStatus
{
    NotRun,
    Running,
    Passed,
    Failed
}

public enum CoverageState
{
    Undetermined,
    Covered,
    NotCovered
}

public sealed class TestLensViewModel : INotifyPropertyChanged
{
    private static readonly System.Windows.Media.Brush NeutralBackground = CreateFrozenBrush(0x2a, 0x38, 0x82, 0xf6);
    private static readonly System.Windows.Media.Brush NeutralBorder = CreateFrozenBrush(0x55, 0x38, 0x82, 0xf6);
    private static readonly System.Windows.Media.Brush NeutralText = CreateFrozenBrush(0xff, 0x93, 0xc5, 0xfd);

    private static readonly System.Windows.Media.Brush CoveredBackground = CreateFrozenBrush(0x22, 0x22, 0xc5, 0x5e);
    private static readonly System.Windows.Media.Brush CoveredBorder = CreateFrozenBrush(0x50, 0x22, 0xc5, 0x5e);
    private static readonly System.Windows.Media.Brush CoveredText = CreateFrozenBrush(0xff, 0x4a, 0xde, 0x80);

    private static readonly System.Windows.Media.Brush NotCoveredBackground = CreateFrozenBrush(0x22, 0xef, 0x44, 0x44);
    private static readonly System.Windows.Media.Brush NotCoveredBorder = CreateFrozenBrush(0x50, 0xef, 0x44, 0x44);
    private static readonly System.Windows.Media.Brush NotCoveredText = CreateFrozenBrush(0xff, 0xf8, 0x71, 0x71);

    private static System.Windows.Media.Brush CreateFrozenBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private string _targetMethodName = "No method selected";
    private string _targetMethodFqn = string.Empty;
    private string _targetLocation = string.Empty;
    private string _statusText = "Select a method in the code editor and right-click 'TestLens: Find Covering Tests...'";
    private bool _isCovered;
    private CoverageState _coverageState = CoverageState.Undetermined;
    private bool _isLoading;
    private int _testsCount;
    private string _elapsedTimeText = string.Empty;

    public string TargetMethodName
    {
        get => _targetMethodName;
        set => SetField(ref _targetMethodName, value);
    }

    public string TargetMethodFqn
    {
        get => _targetMethodFqn;
        set => SetField(ref _targetMethodFqn, value);
    }

    public string TargetLocation
    {
        get => _targetLocation;
        set => SetField(ref _targetLocation, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public bool IsCovered
    {
        get => _isCovered;
        set => SetField(ref _isCovered, value);
    }

    public CoverageState CoverageState
    {
        get => _coverageState;
        set
        {
            if (!SetField(ref _coverageState, value))
                return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusBadgeBackground)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusBadgeBorderBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusTextColor)));
        }
    }

    public System.Windows.Media.Brush StatusBadgeBackground => CoverageState switch
    {
        CoverageState.Covered => CoveredBackground,
        CoverageState.NotCovered => NotCoveredBackground,
        _ => NeutralBackground
    };

    public System.Windows.Media.Brush StatusBadgeBorderBrush => CoverageState switch
    {
        CoverageState.Covered => CoveredBorder,
        CoverageState.NotCovered => NotCoveredBorder,
        _ => NeutralBorder
    };

    public System.Windows.Media.Brush StatusTextColor => CoverageState switch
    {
        CoverageState.Covered => CoveredText,
        CoverageState.NotCovered => NotCoveredText,
        _ => NeutralText
    };

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (!SetField(ref _isLoading, value))
                return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RefreshButtonText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RefreshButtonToolTip)));
        }
    }

    public string RefreshButtonText => IsLoading ? "⏹ Stop" : "🔄 Refresh";
    public string RefreshButtonToolTip => IsLoading ? "Stop search or test run" : "Refresh covering tests";

    public int TestsCount
    {
        get => _testsCount;
        set => SetField(ref _testsCount, value);
    }

    public string ElapsedTimeText
    {
        get => _elapsedTimeText;
        set => SetField(ref _elapsedTimeText, value);
    }

    public ObservableCollection<CoveringTestItemViewModel> Tests { get; } = new();

    public void AddOrUpdateDiscoveredMatch(CoveringTestMatch match)
    {
        var existing = Tests.FirstOrDefault(t => string.Equals(t.FullyQualifiedName, match.TestMethod.FullyQualifiedName, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
            Tests.Add(new CoveringTestItemViewModel(match));
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
            Tests.Add(new CoveringTestItemViewModel(match));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class CoveringTestItemViewModel(CoveringTestMatch match) : INotifyPropertyChanged
{
    private TestExecutionStatus _status = TestExecutionStatus.NotRun;
    private string _executionMessage = string.Empty;

    public CoveringTestMatch Match { get; private set; } = match;

    public string DisplayName => Match.TestMethod.MethodName;
    public string FullyQualifiedName => Match.TestMethod.FullyQualifiedName;
    public string Framework => Match.TestMethod.Framework.ToString();
    public int MinDepth => Match.MinDepth;
    public bool HasDirectCall => Match.HasDirectCall;

    public string DepthText => Match.HasDirectCall
        ? "Direct Call (Depth 1)"
        : $"Indirect Call (Depth {Match.MinDepth})";

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

    public TestExecutionStatus Status
    {
        get => _status;
        set
        {
            if (_status == value)
                return;
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusIcon));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(StatusBadgeText));
        }
    }

    public string ExecutionMessage
    {
        get => _executionMessage;
        set
        {
            if (_executionMessage == value)
                return;
            _executionMessage = value;
            OnPropertyChanged();
        }
    }

    public string StatusIcon => Status switch
    {
        TestExecutionStatus.Running => "⏳",
        TestExecutionStatus.Passed => "✔",
        TestExecutionStatus.Failed => "❌",
        _ => "🧪"
    };

    public string StatusColor => Status switch
    {
        TestExecutionStatus.Passed => "#16a34a",
        TestExecutionStatus.Failed => "#dc2626",
        TestExecutionStatus.Running => "#2563eb",
        _ => "#94a3b8"
    };

    public string StatusBadgeText => Status switch
    {
        TestExecutionStatus.Running => "Running...",
        TestExecutionStatus.Passed => "Passed",
        TestExecutionStatus.Failed => "Failed",
        _ => string.Empty
    };

    public ObservableCollection<CallChainViewModel> Chains { get; } =
        new(match.CallChains.Select(c => new CallChainViewModel(c)));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
