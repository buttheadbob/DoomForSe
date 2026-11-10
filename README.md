# Doom for Space Engineers

A client-side Pulsar plugin that runs Doom in an in-game GUI screen.

- **Ctrl+Shift+D** opens the Doom screen; press it again to close it.
- The engine is [Managed Doom](https://github.com/sinshu/managed-doom) (a C# port of the original Doom source,
  GPL-2.0-or-later), vendored in `ClientPlugin/Doom`. Its framebuffer is uploaded each frame into a generated game
  texture and drawn with `MyGuiManager.DrawSpriteBatch`, so no extra window or WinForms is needed.
- Game data: the free [Freedoom](https://freedoom.github.io/) Phase 1 WAD (BSD-3-Clause, v0.13.0) comes with the
  plugin in `ClientPlugin/Assets/Freedoom`, so it works straight away. To play the original Doom, copy your own `DOOM.WAD` or `DOOM2.WAD`
  into `%AppData%\SpaceEngineers\Storage\DoomForSe`; it is used instead of Freedoom. Saves go to the same folder.
- Sound effects and music play through the game's own audio engine (Windows and Linux). Music uses the TimGM6mb
  General MIDI sound font, which comes with the plugin. The game's streaming voice is mono, so sounds have distance
  falloff but no left/right panning. Doom's own Options menu sets the volumes.
- Your progress is saved to a hidden slot every minute and when the screen closes, and resumes next time.
- Controls follow Managed Doom: arrows or WASD to move, Ctrl to fire, Space to use, Shift to run, Esc for the menu.

Doom runs only while its screen has focus; the game world keeps running behind it.

Licences: see `LICENSE` and the files in `Docs/`: Managed Doom (GPL-2.0-or-later), MeltySynth (MIT),
Freedoom (BSD-3-Clause) and TimGM6mb (GPL-2.0).
