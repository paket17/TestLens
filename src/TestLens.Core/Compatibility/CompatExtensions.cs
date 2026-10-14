using System.Diagnostics;

namespace TestLens.Core.Compatibility;

internal static class CompatExtensions
{
    public static void ThrowIfNull<T>(T? argument, string paramName)
    {
        if (argument == null)
            throw new ArgumentNullException(paramName);
    }

#if NETSTANDARD2_0
    public static Task WaitForExitAsync(this Process process, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>();
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => tcs.TrySetResult(true);

        if (process.HasExited)
            tcs.TrySetResult(true);

        if (cancellationToken.CanBeCanceled)
            cancellationToken.Register(() => tcs.TrySetCanceled());

        return tcs.Task;
    }
#endif
}
