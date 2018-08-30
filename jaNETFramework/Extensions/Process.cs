﻿/* *****************************************************************************************************************************
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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace jaNET.Diagnostics
{
    class Process
    {
        public static Process Instance => Singleton<Process>.Instance;

        internal string Start(string sFilePath) {
            if (sFilePath.Trim().Contains(" ")) {
                string fileName = sFilePath.Substring(0, sFilePath.IndexOf(' '));
                string arguments = sFilePath.Substring(fileName.Length);
                return Start(fileName, arguments);
            }
            return Start(sFilePath, string.Empty);
        }

        internal string Start(string sFilePath, string sArguments) {
            try {
                if (sFilePath != string.Empty) {
                    var process = new System.Diagnostics.Process {
                        EnableRaisingEvents = false
                    };

                    if (sArguments != string.Empty) {
                        process.StartInfo.FileName = sFilePath;
                        process.StartInfo.Arguments = sArguments;
                    }
                    else
                        process.StartInfo.FileName = sFilePath;

                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    process.Close();

                    return output;
                }
                return string.Empty;
            }
            catch (Exception e) {
                Logger.Instance.Append(string.Format("obj [ Process.Start <Exception> ] Arguments: [ {0} {1} ] Exception Message: [ {2} ]", sFilePath, sArguments, e.Message));
                return e.Message;
            }
        }

        // True => worked, False => timeout
        /// <summary>
        /// Always observe with try/catch
        /// </summary>
        /// <param name="method"></param>
        /// <param name="timeout"></param>
        internal static void CallWithTimeout(Action method, int timeout = 1000) {
            //Exception e;
            var cts = new CancellationTokenSource();
            cts.CancelAfter(timeout);

            Task.Run(method, cts.Token).Wait(timeout, cts.Token);
                //.ContinueWith(t => {
                //    // Ensure any exception is observed, is no-op if no exception.
                //    // Using closure to help avoid this being optimised out.
                //    e = t.Exception;
                //});
            //worker.Wait(timeout, cts.Token);
        }
    }
}