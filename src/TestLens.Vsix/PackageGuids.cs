using System;

namespace TestLens.Vsix;

internal static class PackageGuids
{
    public const string PackageGuidString = "e9a2b534-192a-43cf-8a21-995a9ef02b11";
    public static readonly Guid PackageGuid = new(PackageGuidString);

    public const string CommandSetString = "f1b3c456-2a3b-4c5d-6e7f-8a9b0c1d2e3f";
    public static readonly Guid CommandSet = new(CommandSetString);

    public const int FindCoveringTestsId = 0x0100;
    public const int OpenToolWindowId = 0x0101;
}
