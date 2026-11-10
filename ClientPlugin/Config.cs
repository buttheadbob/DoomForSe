using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Elements;
using ClientPlugin.Settings.Tools;
using VRage.Input;

namespace ClientPlugin;

/// <summary>
/// Doom's picture size. The values are the dropdown's item order, so they must stay 0, 1, 2, ...
/// The names are what the settings dialog shows.
/// </summary>
public enum DoomResolution
{
    Auto_MatchScreen,
    Low_640x400,
    Medium_960x600,
    For1080p_1280x800,
    For1440p_1600x1000,
    Sharp1440p_1920x1200,
    For4K_2560x1600,
}

public class Config : INotifyPropertyChanged
{
    public const int MaxVolume = 100;
    public const int VolumeStep = 10;

    #region Options

    private int volume = 70;
    private DoomResolution resolution = DoomResolution.Auto_MatchScreen;
    private Binding openKey = new Binding(MyKeys.D, ctrl: true, shift: true);
    private Binding volumeDownKey = new Binding(MyKeys.PageDown);
    private Binding volumeUpKey = new Binding(MyKeys.PageUp);
    private bool recoil = true;
    private bool hitMarkers = true;
    private bool extraBlood = true;
    private Binding zoomKey = new Binding(MyKeys.Z);

    #endregion

    #region User interface

    public readonly string Title = "Doom for Space Engineers";

    [Separator("Audio")]
    [Slider(
        0f,
        MaxVolume,
        5f,
        SliderAttribute.SliderType.Integer,
        label: "Doom volume",
        description: "Loudness of Doom's music and sound effects, separate from the game's own volume. "
            + "Also changed with the volume keys below while playing. Doom's Options menu balances music against effects.",
        unit: "%"
    )]
    public int Volume
    {
        get => volume;
        set => SetField(ref volume, Math.Max(0, Math.Min(MaxVolume, value)));
    }

    [Separator("Video")]
    [Dropdown(label: "Resolution", description: "Size of Doom's picture before it is scaled to your screen. "
        + "Auto picks the size that matches your screen: 1280x800 on 1080p, 1920x1200 on 1440p, 2560x1600 on 4K. "
        + "Higher than your screen gains nothing and costs CPU; lower is softer but cheaper. "
        + "Takes effect the next time the Doom screen opens.")]
    public DoomResolution Resolution
    {
        get => resolution;
        set => SetField(ref resolution, value);
    }

    [Separator("Gameplay extras")]
    [Checkbox(label: "Weapon recoil", description: "The weapon kicks back and shakes a little when it fires. Looks only; aim and damage are unchanged.")]
    public bool Recoil
    {
        get => recoil;
        set => SetField(ref recoil, value);
    }

    [Checkbox(label: "Hit markers", description: "A small cross flashes on the crosshair when your shot hits a monster, red on a kill.")]
    public bool HitMarkers
    {
        get => hitMarkers;
        set => SetField(ref hitMarkers, value);
    }

    [Checkbox(label: "Extra blood", description: "More blood when monsters are hit. Looks only; it does not change the game's randomness, so demos and gameplay stay as in the original.")]
    public bool ExtraBlood
    {
        get => extraBlood;
        set => SetField(ref extraBlood, value);
    }

    [Keybind(description: "Hold to zoom the view (the middle mouse button also zooms). Mouse turning slows down while zoomed. Right click to unbind.")]
    public Binding ZoomKey
    {
        get => zoomKey;
        set => SetField(ref zoomKey, value);
    }

    [Separator("Keys")]
    [Keybind(description: "Opens and closes the Doom screen. Right click to unbind.")]
    public Binding OpenKey
    {
        get => openKey;
        set => SetField(ref openKey, value);
    }

    [Keybind(description: "Turns Doom's volume down while playing. Page Down by default: it is in the same place on every keyboard layout and Doom does not use it.")]
    public Binding VolumeDownKey
    {
        get => volumeDownKey;
        set => SetField(ref volumeDownKey, value);
    }

    [Keybind(description: "Turns Doom's volume up while playing. Page Up by default.")]
    public Binding VolumeUpKey
    {
        get => volumeUpKey;
        set => SetField(ref volumeUpKey, value);
    }

    #endregion

    /// <summary>How many times 320x200 Doom renders at, for a screen of the given height.</summary>
    public int ResolutionScale(int screenHeight) => resolution switch
    {
        DoomResolution.Low_640x400 => 2,
        DoomResolution.Medium_960x600 => 3,
        DoomResolution.For1080p_1280x800 => 4,
        DoomResolution.For1440p_1600x1000 => 5,
        DoomResolution.Sharp1440p_1920x1200 => 6,
        DoomResolution.For4K_2560x1600 => 8,
        _ => AutoScale(screenHeight),
    };

    /// <summary>The smallest scale whose picture is at least as tall as it appears on screen (74% of the height).</summary>
    public static int AutoScale(int screenHeight)
    {
        var shown = screenHeight * 0.74;
        var scale = (int)Math.Ceiling(shown / 200.0 - 0.05);
        return Math.Max(2, Math.Min(8, scale));
    }

    /// <summary>The volume as a gain from 0 to 1.</summary>
    public float VolumeGain => volume / (float)MaxVolume;

    public void ChangeVolume(int delta)
    {
        Volume = (int)Math.Round((volume + delta) / (double)VolumeStep) * VolumeStep;
        ConfigStorage.Save(this);
    }

    #region Property change notification boilerplate

    public static readonly Config Default = new Config();
    public static readonly Config Current = ConfigStorage.Load();

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}
