/* *****************************************************************************************************************************
 * (c) J@mBeL.net 2010-2017
 * Author: John Ambeliotis
 * Created: 24 Apr. 2010
 *
 * License:
 *  This file is part of jaNET Framework.

    jaNET Framework is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    jaNET Framework is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with jaNET Framework. If not, see <http://www.gnu.org/licenses/>. */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace jaNETFramework
{
    class Settings
    {
        internal List<String> LoadSettings(string fileName) {
            string fullPath = Methods.Instance.GetApplicationPath + fileName;

            if (File.Exists(fullPath)) {
                string input;
                var args = new List<String>();

                using (var tr = new StreamReader(fullPath)) {
                    while ((input = tr.ReadLine()) != null) {
                        if (input != null)
                            args.Add(RijndaelSimple.Decrypt(input));
                    }
                }
                return args;
            }
            return null;
        }

        internal string SaveSettings(string fileName, string settings) {
            try {
                string[] args = settings.Split('\n');

                string fullPath = Methods.Instance.GetApplicationPath + fileName;

                if (File.Exists(fullPath))
                    File.Delete(fullPath);

                using (var tw = new StreamWriter(fullPath)) {
                    args.ToList().ForEach(s => tw.WriteLine(RijndaelSimple.Encrypt(s.Trim())));
                    //for (int i = 0; i < args.Length; i++)
                    //tw.WriteLine(RijndaelSimple.Encrypt(args[i].Trim()));
                }
                return "Settings saved.";
            }
            catch {
                return "Unable to save settings.";
            }
        }
    }
}
