//
// Copyright (C) 1993-1996 Id Software, Inc.
// Copyright (C) 2019-2020 Nobuaki Tanaka
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//

// Adapted from Managed Doom (GPL-2.0-or-later) for hosting inside Space Engineers.
// Files (WADs, save games) live in a plugin folder under the game's user data
// directory instead of next to the executable.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VRage.FileSystem;

namespace ManagedDoom
{
    public static class ConfigUtilities
    {
        private static readonly string[] iwadNames = new string[]
        {
            "DOOM2.WAD",
            "PLUTONIA.WAD",
            "TNT.WAD",
            "DOOM.WAD",
            "DOOM1.WAD",
            "FREEDOOM2.WAD",
            "FREEDOOM1.WAD"
        };

        /// <summary>Folder that holds the IWAD files and the save games.</summary>
        public static string GetExeDirectory()
        {
            var path = Path.Combine(MyFileSystem.UserDataPath, "Storage", "DoomForSe");
            Directory.CreateDirectory(path);
            return path;
        }

        public static string GetDefaultIwadPath()
        {
            var directory = GetExeDirectory();
            var own = FindIwad(directory);
            if (own != null)
            {
                return own;
            }

            // No WAD of the player's own: use the Freedoom WAD that comes with the plugin.
            var bundled = ClientPlugin.Plugin.FindBundledWad();
            if (bundled != null)
            {
                return bundled;
            }

            throw new FileNotFoundException(
                "No Doom IWAD was found, and the bundled Freedoom could not be loaded. "
                + "Copy DOOM1.WAD, DOOM.WAD or DOOM2.WAD into: " + directory);
        }

        /// <summary>
        /// The player's own IWAD in the folder, in the order of <see cref="iwadNames"/>, or null. Names are matched
        /// ignoring case, so doom2.wad, DOOM2.WAD and Doom2.Wad are all found on case-sensitive file systems too.
        /// </summary>
        public static string FindIwad(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return null;
            }

            var files = Directory.GetFiles(directory);
            foreach (var name in iwadNames)
            {
                var match = files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        public static bool IsIwad(string path)
        {
            var name = Path.GetFileName(path).ToUpperInvariant();
            return iwadNames.Contains(name);
        }

        public static string[] GetWadPaths(CommandLineArgs args)
        {
            var wadPaths = new List<string>();

            if (args.iwad.Present)
            {
                wadPaths.Add(args.iwad.Value);
            }
            else
            {
                wadPaths.Add(GetDefaultIwadPath());
            }

            if (args.file.Present)
            {
                foreach (var path in args.file.Value)
                {
                    wadPaths.Add(path);
                }
            }

            return wadPaths.ToArray();
        }
    }
}
