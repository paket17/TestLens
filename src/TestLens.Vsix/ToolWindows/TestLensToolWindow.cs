using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Imaging;

namespace TestLens.Vsix.ToolWindows;

public class TestLensToolWindow : BaseToolWindow<TestLensToolWindow>
{
    public override string GetTitle(int toolWindowId) => "TestLens";

    public override Type PaneType => typeof(Pane);

    public override Task<FrameworkElement> CreateAsync(int toolWindowId, CancellationToken cancellationToken)
    {
        return Task.FromResult<FrameworkElement>(new TestLensToolWindowControl());
    }

    [Guid("d4e5f6a7-b8c9-4012-9345-6789abcdef01")]
    public class Pane : ToolkitToolWindowPane
    {
        public Pane()
        {
            BitmapImageMoniker = KnownMonikers.Test;
        }
    }
}
