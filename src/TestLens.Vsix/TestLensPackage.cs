using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using TestLens.Core;
using TestLens.Core.Extensions;
using TestLens.Vsix.ToolWindows;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;

namespace TestLens.Vsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuids.PackageGuidString)]
[ProvideBindingPath]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideToolWindow(typeof(TestLensToolWindow.Pane), Style = VsDockStyle.Tabbed, Window = WindowGuids.SolutionExplorer)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.CSharpProject_string, PackageAutoLoadFlags.BackgroundLoad)]
public sealed class TestLensPackage : ToolkitPackage
{
    public static IServiceProvider? Services { get; private set; }

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        Services = TestLensServices.CreateServiceProvider();

        await this.RegisterCommandsAsync();
        this.RegisterToolWindows();
    }
}
