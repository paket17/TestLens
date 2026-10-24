using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using TestLens.Vsix.ToolWindows;
using Microsoft.VisualStudio.Shell;

namespace TestLens.Vsix.Commands;

[Command(PackageGuids.CommandSetString, PackageGuids.OpenToolWindowId)]
internal sealed class OpenToolWindowCommand : BaseCommand<OpenToolWindowCommand>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        await TestLensToolWindow.ShowAsync();
    }
}
