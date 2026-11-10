using System;
using System.Collections.Generic;
using ManagedDoom;
using ManagedDoom.UserInput;
using VRage.Input;

namespace ClientPlugin.DoomHost;

/// <summary>
/// IUserInput fed by the screen: it polls the game's keyboard and mouse on the main thread and passes them in through
/// <see cref="SetKey"/> and <see cref="AddMouse"/>, while Doom reads them on its own thread. One lock guards both sides.
/// </summary>
public sealed class DoomInput : IUserInput
{
    /// <summary>Every key the screen polls, paired with the Doom key it produces.</summary>
    public static readonly (MyKeys Key, DoomKey Doom)[] Map = BuildMap();

    private readonly ManagedDoom.Config config;
    private readonly object sync = new();
    private readonly HashSet<MyKeys> down = new();
    private readonly bool[] weaponKeys = new bool[7];
    private int turnHeld;
    private float mouseDeltaX, mouseDeltaY;
    private bool mouseLeft, mouseRight;

    /// <summary>True while Doom wants the mouse for looking around (in a level, with no menu open).</summary>
    public bool MouseGrabbed => mouseGrabbed;
    private volatile bool mouseGrabbed;

    public DoomInput(ManagedDoom.Config config)
    {
        this.config = config;
    }

    public bool IsDown(MyKeys key)
    {
        lock (sync) return down.Contains(key);
    }

    public void SetKey(MyKeys key, bool isDown)
    {
        lock (sync)
        {
            if (isDown) down.Add(key); else down.Remove(key);
        }
    }

    public void ReleaseAll()
    {
        lock (sync)
        {
            down.Clear();
            mouseLeft = mouseRight = false;
            mouseDeltaX = mouseDeltaY = 0;
        }
    }

    /// <summary>Called every frame by the screen. Movement adds up until the next tic reads it.</summary>
    public void AddMouse(float dx, float dy, bool left, bool right)
    {
        lock (sync)
        {
            mouseDeltaX += dx;
            mouseDeltaY += dy;
            mouseLeft = left;
            mouseRight = right;
        }
    }

    private bool IsPressed(KeyBinding binding)
    {
        foreach (var doomKey in binding.Keys)
        {
            foreach (var (key, doom) in Map)
            {
                if (doom == doomKey && down.Contains(key))
                    return true;
            }
        }

        if (mouseGrabbed)
        {
            foreach (var button in binding.MouseButtons)
            {
                if ((button == DoomMouseButton.Mouse1 && mouseLeft) || (button == DoomMouseButton.Mouse2 && mouseRight))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Called by Doom on its own thread, once per tic.</summary>
    public void BuildTicCmd(TicCmd cmd)
    {
        lock (sync) BuildTicCmdLocked(cmd);
    }

    private void BuildTicCmdLocked(TicCmd cmd)
    {
        var keyForward = IsPressed(config.key_forward);
        var keyBackward = IsPressed(config.key_backward);
        var keyStrafeLeft = IsPressed(config.key_strafeleft);
        var keyStrafeRight = IsPressed(config.key_straferight);
        var keyTurnLeft = IsPressed(config.key_turnleft);
        var keyTurnRight = IsPressed(config.key_turnright);
        var keyFire = IsPressed(config.key_fire);
        var keyUse = IsPressed(config.key_use);
        var keyRun = IsPressed(config.key_run);
        var strafe = IsPressed(config.key_strafe);

        for (var i = 0; i < weaponKeys.Length; i++)
            weaponKeys[i] = down.Contains((MyKeys)((int)MyKeys.D1 + i)) || down.Contains((MyKeys)((int)MyKeys.NumPad1 + i));

        cmd.Clear();

        var speed = keyRun ? 1 : 0;
        var forward = 0;
        var side = 0;

        if (config.game_alwaysrun)
            speed = 1 - speed;

        if (keyTurnLeft || keyTurnRight) turnHeld++; else turnHeld = 0;

        var turnSpeed = turnHeld < PlayerBehavior.SlowTurnTics ? 2 : speed;

        if (strafe)
        {
            if (keyTurnRight) side += PlayerBehavior.SideMove[speed];
            if (keyTurnLeft) side -= PlayerBehavior.SideMove[speed];
        }
        else
        {
            if (keyTurnRight) cmd.AngleTurn -= (short)PlayerBehavior.AngleTurn[turnSpeed];
            if (keyTurnLeft) cmd.AngleTurn += (short)PlayerBehavior.AngleTurn[turnSpeed];
        }

        if (keyForward) forward += PlayerBehavior.ForwardMove[speed];
        if (keyBackward) forward -= PlayerBehavior.ForwardMove[speed];
        if (keyStrafeLeft) side -= PlayerBehavior.SideMove[speed];
        if (keyStrafeRight) side += PlayerBehavior.SideMove[speed];

        if (keyFire) cmd.Buttons |= TicCmdButtons.Attack;
        if (keyUse) cmd.Buttons |= TicCmdButtons.Use;

        for (var i = 0; i < weaponKeys.Length; i++)
        {
            if (weaponKeys[i])
            {
                cmd.Buttons |= TicCmdButtons.Change;
                cmd.Buttons |= (byte)(i << TicCmdButtons.WeaponShift);
                break;
            }
        }

        // Mouse: left and right turn, forward and back walk (unless the y axis is disabled).
        var ms = 0.5f * config.mouse_sensitivity;
        var mx = mouseGrabbed ? (int)Math.Round(ms * mouseDeltaX) : 0;
        var my = mouseGrabbed && !config.mouse_disableyaxis ? (int)Math.Round(ms * -mouseDeltaY) : 0;
        mouseDeltaX = mouseDeltaY = 0;
        forward += my;
        if (strafe)
            side += mx * 2;
        else
            cmd.AngleTurn -= (short)(mx * 8);

        forward = Math.Max(-PlayerBehavior.MaxMove, Math.Min(PlayerBehavior.MaxMove, forward));
        side = Math.Max(-PlayerBehavior.MaxMove, Math.Min(PlayerBehavior.MaxMove, side));

        cmd.ForwardMove += (sbyte)forward;
        cmd.SideMove += (sbyte)side;
    }

    public void Reset() { }
    public void GrabMouse()
    {
        lock (sync)
        {
            mouseGrabbed = true;
            mouseDeltaX = mouseDeltaY = 0;
        }
    }

    public void ReleaseMouse()
    {
        lock (sync)
        {
            mouseGrabbed = false;
            mouseDeltaX = mouseDeltaY = 0;
        }
    }

    public int MaxMouseSensitivity => 15;

    public int MouseSensitivity
    {
        get => config.mouse_sensitivity;
        set => config.mouse_sensitivity = value;
    }

    private static (MyKeys, DoomKey)[] BuildMap()
    {
        var list = new List<(MyKeys, DoomKey)>();
        for (var i = 0; i < 26; i++) list.Add(((MyKeys)((int)MyKeys.A + i), DoomKey.A + i));
        for (var i = 0; i < 10; i++) list.Add(((MyKeys)((int)MyKeys.D0 + i), DoomKey.Num0 + i));
        for (var i = 0; i < 10; i++) list.Add(((MyKeys)((int)MyKeys.NumPad0 + i), DoomKey.Numpad0 + i));
        for (var i = 0; i < 15; i++) list.Add(((MyKeys)((int)MyKeys.F1 + i), DoomKey.F1 + i));
        list.AddRange(new (MyKeys, DoomKey)[]
        {
            (MyKeys.Space, DoomKey.Space),
            (MyKeys.OemComma, DoomKey.Comma),
            (MyKeys.OemMinus, DoomKey.Subtract),
            (MyKeys.OemPeriod, DoomKey.Period),
            (MyKeys.OemQuestion, DoomKey.Slash),
            (MyKeys.OemSemicolon, DoomKey.Semicolon),
            (MyKeys.OemPlus, DoomKey.Equal),
            (MyKeys.OemOpenBrackets, DoomKey.LBracket),
            (MyKeys.OemCloseBrackets, DoomKey.RBracket),
            (MyKeys.OemPipe, DoomKey.Backslash),
            (MyKeys.Escape, DoomKey.Escape),
            (MyKeys.Enter, DoomKey.Enter),
            (MyKeys.Tab, DoomKey.Tab),
            (MyKeys.Back, DoomKey.Backspace),
            (MyKeys.Insert, DoomKey.Insert),
            (MyKeys.Delete, DoomKey.Delete),
            (MyKeys.Right, DoomKey.Right),
            (MyKeys.Left, DoomKey.Left),
            (MyKeys.Down, DoomKey.Down),
            (MyKeys.Up, DoomKey.Up),
            (MyKeys.PageUp, DoomKey.PageUp),
            (MyKeys.PageDown, DoomKey.PageDown),
            (MyKeys.Home, DoomKey.Home),
            (MyKeys.End, DoomKey.End),
            (MyKeys.Pause, DoomKey.Pause),
            (MyKeys.Divide, DoomKey.Divide),
            (MyKeys.Multiply, DoomKey.Multiply),
            (MyKeys.Subtract, DoomKey.Subtract),
            (MyKeys.Add, DoomKey.Add),
            (MyKeys.LeftShift, DoomKey.LShift),
            (MyKeys.RightShift, DoomKey.RShift),
            (MyKeys.LeftControl, DoomKey.LControl),
            (MyKeys.RightControl, DoomKey.RControl),
            (MyKeys.LeftAlt, DoomKey.LAlt),
            (MyKeys.RightAlt, DoomKey.RAlt),
        });
        return list.ToArray();
    }
}
