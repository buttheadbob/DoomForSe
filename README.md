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
  falloff but no left/right panning. Doom's own Options menu sets the volumes, the game's sound volume and mute
  apply on top, and the game's music is silenced while the Doom screen is open.
- Your progress is saved to a hidden slot every minute and when the screen closes, and resumes next time.
  Closing the game with Doom open keeps the last autosave (at most a minute old); close Doom with its key first to
  save everything.
- Controls follow Managed Doom: arrows or WASD to move, Ctrl to fire, Space to use, Shift to run, Esc for the menu.

Doom runs only while its screen has focus; the game world keeps running behind it.

## Licence

GPL-2.0-or-later, see [LICENSE](LICENSE). Code from before 2026-11-10 was MIT, see [LICENSE-MIT](LICENSE-MIT).

Parts of the repository come from other projects and keep their own notices:

| Path | Origin | Licence |
|---|---|---|
| `ClientPlugin/Doom` | [Managed Doom](https://github.com/sinshu/managed-doom), adapted | GPL-2.0-or-later, see `Docs/LICENSE_ManagedDoom.txt` |
| `ClientPlugin/MeltySynth` | [MeltySynth](https://github.com/sinshu/meltysynth), adapted | MIT, see `Docs/LICENSE_MeltySynth.txt` |
| `ClientPlugin/Assets/Freedoom/freedoom1.wad` | [Freedoom](https://freedoom.github.io/) 0.13.0 | BSD-3-Clause, see `Docs/LICENSE_Freedoom.txt` |
| `ClientPlugin/Assets/Audio/TimGM6mb.sf2` | TimGM6mb sound font by Tim Brechbill, from the Managed Doom v2.1a release | GPL-2.0, see `Docs/LICENSE_TimGM6mb.txt` |

The plugin as a whole is distributed under GPL-2.0-or-later.
