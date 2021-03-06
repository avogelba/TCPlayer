﻿/*
TC Plyer
Total Commander Audio Player plugin & standalone player written in C#, based on bass.dll components
Copyright (C) 2016 Webmaster442 aka. Ruzsinszki Gábor

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.IO;
using System.Linq;

namespace TCPluginInstaller.Logic
{
    internal static class TCPluginInstaller
    {
        private const string WlxFile = "TCPlayerLister.wlx";
        private const string WcxFile = "TCPlayerPacker.wcx";

        public static void Install(string file, bool lister, bool packer)
        {
            string iniFile = file;
            string currentdir = AppDomain.CurrentDomain.BaseDirectory;
            string section = "";
            string fullpath = "";

            if (lister)
            {
                section = ToString(PluginType.Lister);
                fullpath = Path.Combine(currentdir, WlxFile);
                var keys = IniFile.GetKeyValuePairs(iniFile, section);
                int index = 0;
                if (keys != null)
                {
                    index = keys.Keys.Select(k => Convert.ToInt32(k)).Max();
                    index += 1;
                }
                IniFile.WriteValue(iniFile, section, index.ToString(), fullpath);
            }

            if (packer)
            {
                section = ToString(PluginType.Packer);
                fullpath = Path.Combine(currentdir, WcxFile);
                IniFile.WriteValue(iniFile, section, "tcplayer", $"277,{fullpath}");
            }
        }

        private static string ToString(PluginType pluginType)
        {
            switch (pluginType)
            {
                case PluginType.Packer:
                    return "PackerPlugins";
                case PluginType.Lister:
                    return "ListerPlugins";
                default:
                    return "";
            }
        }
    }
}
