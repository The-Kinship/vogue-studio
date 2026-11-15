using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace VogueStudio;

public sealed class Plugin : IDalamudPlugin
{
    private static readonly string[] CommandNames = { "/vogue", "/voguestudio" };

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    internal static Configuration Config { get; private set; } = null!;

    private readonly WindowSystem windows = new("VogueStudio");
    private readonly MainWindow window;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        window = new MainWindow();
        windows.AddWindow(window);

        foreach (var cmd in CommandNames)
            CommandManager.AddHandler(cmd, new CommandInfo(OnCommand) { HelpMessage = "Open or close Rafter Cat's Vogue Studio (simple posing for GPose)." });

        // Stay visible in GPose (Dalamud hides plugin windows there unless told otherwise).
        PluginInterface.UiBuilder.DisableGposeUiHide = true;
        PluginInterface.UiBuilder.Draw += DrawAll;
        PluginInterface.UiBuilder.OpenMainUi += Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += Toggle;
    }

    /// <summary>Circles only while the Vogue Studio window is open, so they never get in the way otherwise.</summary>
    private void DrawAll()
    {
        if (window.IsOpen) Overlay.Draw();
        windows.Draw();
    }

    private void OnCommand(string command, string args) => Toggle();

    private void Toggle() => window.Toggle();

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= DrawAll;
        PluginInterface.UiBuilder.OpenMainUi -= Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= Toggle;
        foreach (var cmd in CommandNames) CommandManager.RemoveHandler(cmd);
        windows.RemoveAllWindows();
    }
}
