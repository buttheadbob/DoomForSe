using VRage.Input;

namespace ClientPlugin.Settings.Tools;

public struct Binding
{
    public MyKeys Key;
    public bool Ctrl;
    public bool Alt;
    public bool Shift;

    public Binding(MyKeys key, bool ctrl = false, bool alt = false, bool shift = false)
    {
        Key = key;
        Ctrl = ctrl;
        Alt = alt;
        Shift = shift;
    }

    public override string ToString()
    {
        if (Key == MyKeys.None)
            return "None";

        var ctrl = Ctrl ? "Ctrl+" : "";
        var alt = Alt ? "Alt+" : "";
        var shift = Shift ? "Shift+" : "";
        return $"{ctrl}{alt}{shift}{Key}";
    }

    public bool IsPressed(IMyInput input) =>
        Key != MyKeys.None && AreModifiersMatch(input) && input.IsKeyPress(Key);

    public bool HasPressed(IMyInput input) =>
        Key != MyKeys.None && AreModifiersMatch(input) && input.IsNewKeyPressed(Key);

    /// <summary>Like <see cref="IsPressed"/>, but extra Ctrl, Alt or Shift held at the same time do not matter.</summary>
    public bool IsPressedAnyModifiers(IMyInput input) =>
        Key != MyKeys.None && AreRequiredModifiersDown(input) && input.IsKeyPress(Key);

    public bool HasPressedAnyModifiers(IMyInput input) =>
        Key != MyKeys.None && AreRequiredModifiersDown(input) && input.IsNewKeyPressed(Key);

    private bool AreRequiredModifiersDown(IMyInput input)
    {
        return (!Ctrl || input.IsAnyCtrlKeyPressed())
            && (!Alt || input.IsAnyAltKeyPressed())
            && (!Shift || input.IsAnyShiftKeyPressed());
    }

    private bool AreModifiersMatch(IMyInput input)
    {
        return input.IsAnyCtrlKeyPressed() == Ctrl
            && input.IsAnyAltKeyPressed() == Alt
            && input.IsAnyShiftKeyPressed() == Shift;
    }
}
