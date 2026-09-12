namespace Jarvis.Windows;

internal static class BuildInfo
{
    // GitHub Actions overwrites this value with GITHUB_RUN_NUMBER before publishing.
    internal const int ReleaseBuild = 8;
}
