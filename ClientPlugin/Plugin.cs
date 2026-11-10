using System.Collections.Generic;
using System.Reflection;
using ClientPlugin.DoomHost;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Layouts;
using HarmonyLib;
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
public class Plugin : IPlugin
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

        // TODO: Put your one time initialization code here.
        var harmony = new Harmony(Name);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        MyLog.Default.WriteLine("DoomForSe: loaded, " + Config.Current.OpenKey + " opens Doom");
    }

    public void Dispose()
    {
        // IMPORTANT: Do NOT call harmony.UnpatchAll() here! It may break other plugins.
        DoomScreen.Current?.CloseScreen();

        Instance = null;
    }

    public void Update()
    {
        // The open key (Ctrl+Shift+D by default) opens the Doom screen, and closes it if it is already open.
        var input = MyInput.Static;
        if (input != null && Config.Current.OpenKey.Key != MyKeys.None && Config.Current.OpenKey.HasPressed(input))
        {
            MyLog.Default.WriteLine("DoomForSe: hotkey pressed");
            DoomScreen.Toggle();
        }
    }

    // ReSharper disable once UnusedMember.Global
    public void OpenConfigDialog()
    {
        Instance.settingsGenerator.SetLayout<Simple>();
        MyGuiSandbox.AddScreen(Instance.settingsGenerator.Dialog);
    }

    private static IReadOnlyDictionary<string, string> assets;

    /// <summary>Called by Pulsar with the image files named in the plugin manifest.</summary>
    public void LoadAssets(IReadOnlyDictionary<string, string> files)
    {
        assets = files;
    }

    /// <summary>
    /// The Freedoom IWAD that comes with the plugin (the "Freedoom" asset in the manifest), or null.
    /// DOOMFORSE_FREEDOOM can point at a file or folder for local test runs.
    /// </summary>
    public static string FindBundledWad()
    {
        var roots = new List<string>();
        if (assets != null && assets.TryGetValue("Freedoom", out var asset))
            roots.Add(asset);
        var env = System.Environment.GetEnvironmentVariable("DOOMFORSE_FREEDOOM");
        if (!string.IsNullOrEmpty(env))
            roots.Add(env);

        foreach (var root in roots)
        {
            if (System.IO.File.Exists(root) && root.EndsWith(".wad", System.StringComparison.OrdinalIgnoreCase))
                return root;
            if (!System.IO.Directory.Exists(root))
                continue;

            foreach (var name in new[] { "freedoom1.wad", "freedoom2.wad" })
            {
                foreach (var file in System.IO.Directory.EnumerateFiles(root, name, System.IO.SearchOption.AllDirectories))
                    return file;
            }
        }

        return null;
    }

    /// <summary>
    /// The General MIDI sound font used for music (the "SoundFont" asset), or null.
    /// DOOMFORSE_ASSETS can point at the repository's asset folder for local test runs.
    /// </summary>
    public static string FindSoundFont()
    {
        if (assets != null && assets.TryGetValue("SoundFont", out var path) && System.IO.File.Exists(path))
            return path;

        var folder = System.Environment.GetEnvironmentVariable("DOOMFORSE_ASSETS");
        if (!string.IsNullOrEmpty(folder))
        {
            var file = System.IO.Path.Combine(folder, "Audio", "TimGM6mb.sf2");
            if (System.IO.File.Exists(file))
                return file;
        }

        return null;
    }

    /// <summary>The path of an image asset from the manifest, or null if it is missing.</summary>
    public static string Texture(string name)
    {
        if (assets != null && assets.TryGetValue(name, out var path))
            return path;

        // Local test runs (test.ps1) deploy only the DLL, so they point at the repository's images instead.
        var folder = System.Environment.GetEnvironmentVariable("DOOMFORSE_ASSETS");
        if (!string.IsNullOrEmpty(folder))
        {
            var file = System.IO.Path.Combine(folder, name + ".png");
            if (System.IO.File.Exists(file))
                return file;
        }

        return null;
    }
}
