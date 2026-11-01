namespace Gocgun.Diagnostics.Tooling.Tests;

public static class TestEntryPoint
{
    public static Task<int> Main(string[] args)
    {
        string[] filtered = args
            .Where(static arg => !string.Equals(arg, "--nologo", StringComparison.Ordinal))
            .ToArray();

        return Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(
            filtered,
            SelfRegisteredExtensions.AddSelfRegisteredExtensions);
    }
}
