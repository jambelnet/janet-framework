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

using jaNET.Environment;
using System;
using System.IO;

namespace jaNET.Diagnostics
{
    class Logger
    {
        public static Logger Instance => Singleton<Logger>.Instance;

        // Read Log
        /*using (StreamReader r = File.OpenText("log.txt"))
        {
            DumpLog(r);
        }*/
        static readonly object _log_locker = new Object();

        internal virtual void Append(string logMessage) {
            lock (_log_locker)
                using (StreamWriter w = File.AppendText(Methods.Instance.GetApplicationPath + "log.txt"))
                    Append(logMessage, w);
        }

        void Append(string logMessage, TextWriter w) {
            w.Write("\r\nLog Entry @ ");
            w.WriteLine("{0} {1}", DateTime.Now.ToLongTimeString(),
                DateTime.Now.ToLongDateString());
            //w.WriteLine("  :");
            w.WriteLine("  :{0}", logMessage);
            w.WriteLine("--------------------------------------------------------------------------------------------------");
        }

        void DumpLog(TextReader r) {
            string line;

            while ((line = r.ReadLine()) != null)
                Console.WriteLine(line);
        }
    }
}