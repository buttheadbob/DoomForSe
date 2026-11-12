using System;
using System.Collections.Generic;
using System.Reflection;
using ClientPlugin.DoomHost;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Layouts;
using Sandbox.Graphics.GUI;
using VRage.Input;
using VRage.Plugins;
using VRage.Utils;

// Define assembly version when compiled by Pulsar
#if !LOCAL_BUILD
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

#endif
namespace ClientPlugin;

// ReSharper disable once UnusedType.Global
public partial class Plugin : IPlugin
{
    public const string Name = "DoomForSe";
    public static Plugin Instance { get; private set; }
    private SettingsGenerator settingsGenerator;

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    public void Init(object gameInstance)
    {
        Instance = this;
        Instance.settingsGenerator = new SettingsGenerator();

        MyLog.Default.WriteLine("DoomForSe: loaded, " + Config.Current.OpenKey + " opens Doom");
    }

    public void Dispose()
    {
        // The game is shutting down and runs no more screen updates, so Doom is stopped and saved here.
        var screen = DoomScreen.Current;
        screen?.Shutdown();
        screen?.CloseScreen();
        ManagedDoom.Audio.DoomMusic.ReleaseSoundFont();

        Instance = null;
    }

    public void Update()
    {
        // The open key (Ctrl+Shift+D by default) opens the Doom screen, and closes it if it is already open.
        var input = MyInput.Static;
        if (input != null && Config.Current.OpenKey.Key != MyKeys.None && Config.Current.OpenKey.HasPressed(input))
        {
            DoomScreen.Toggle();
        }

        DoomScreen.RunPending();
        SelfTestUpdate();
    }

    /// <summary>Hook for the development self-test (DoomHost/SelfTest.cs, debug builds only).</summary>
    partial void SelfTestUpdate();

    // ReSharper disable once UnusedMember.Global
    public void OpenConfigDialog()
    {
        Instance.settingsGenerator.SetLayout<Simple>();
        MyGuiSandbox.AddScreen(Instance.settingsGenerator.Dialog);
    }

    /// <summary>
    /// The folder holding the plugin's files: the manifest's single "Assets" folder asset (ClientPlugin/Assets).
    /// Pulsar copies a folder asset with its file names kept, both for hub installs and for development folders.
    /// </summary>
    private static string assetFolder;

    /// <summary>Every file in the asset folder, scanned once: path relative to the folder (with '/') to full path.</summary>
    private static Dictionary<string, string> assetFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The images directly in the asset folder: name without ".png" to full path.</summary>
    private static Dictionary<string, string> textures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Called by Pulsar with the assets named in the plugin manifest.</summary>
    public void LoadAssets(IReadOnlyDictionary<string, string> files)
    {
        assetFolder = files != null && files.TryGetValue("Assets", out var folder) && System.IO.Directory.Exists(folder)
            ? folder
            : null;

        assetFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        textures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (assetFolder == null)
        {
            MyLog.Default.WriteLine("DoomForSe: the Assets folder asset was not found; the screen has no artwork, music or bundled Freedoom");
            return;
        }

        foreach (var path in System.IO.Directory.EnumerateFiles(assetFolder, "*", System.IO.SearchOption.AllDirectories))
        {
            var relative = path.Substring(assetFolder.Length).TrimStart('\\', '/').Replace('\\', '/');
            assetFiles[relative] = path;

            if (relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && relative.IndexOf('/') < 0)
                textures[relative.Substring(0, relative.Length - 4)] = path;
        }
    }

    /// <summary>The path of a file inside the asset folder (relative path with '/'), or null if it is missing.</summary>
    private static string AssetFile(string relative) => assetFiles.TryGetValue(relative, out var path) ? path : null;

    /// <summary>The Freedoom IWAD that comes with the plugin, or null.</summary>
    public static string FindBundledWad() => AssetFile("Freedoom/freedoom1.wad");

    /// <summary>The General MIDI sound font used for music, or null.</summary>
    public static string FindSoundFont() => AssetFile("Audio/TimGM6mb.sf2");

    /// <summary>The path of an image (name without ".png"), or null if it is missing.</summary>
    public static string Texture(string name) => textures.TryGetValue(name, out var path) ? path : null;
}
