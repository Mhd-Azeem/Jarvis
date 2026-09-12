using System.Runtime.CompilerServices;

namespace Jarvis.Windows;

internal static class UpdateBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += OnApplicationIdle;
    }

    private static async void OnApplicationIdle(object? sender, EventArgs e)
    {
        Application.Idle -= OnApplicationIdle;
        await UpdateService.CheckAndOfferUpdateAsync();
    }
}
