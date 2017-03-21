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
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace jaNETFramework
{
    static class SerialComm
    {
        static readonly object _serial_locker = new object();
        internal static volatile string SerialData = string.Empty;
        internal static SerialPort port = new SerialPort();

        internal static void ActivateSerialPort(string portName) {
            try {
                if (OperatingSystem.Version == OperatingSystem.Type.Unix
                    || OperatingSystem.Version == OperatingSystem.Type.MacOS)
                    if (!File.Exists(port.PortName))
                        return;

                port.BaudRate = Convert.ToInt32(
                    Helpers.Xml.AppConfigQuery(
                    ApplicationSettings.ApplicationStructure.ComBaudRatePath)
                    .Item(0).InnerText);

                if (portName == string.Empty)
                    port.PortName = Helpers.Xml.AppConfigQuery(
                        ApplicationSettings.ApplicationStructure.ComPortPath)
                        .Item(0).InnerText;
                else
                    port.PortName = portName;

                port.Open();

                Task.Run(() => {
                    SerialPortListener();
                });
                //var t = new Thread(SerialPortListener);
                //t.IsBackground = true;
                //t.Start();
            }
            catch {
            }
        }

        internal static void DeactivateSerialPort() {
            try {
                if (port.IsOpen)
                    port.Close();
            }
            catch {
            }
        }

        static void SerialPortListener() {
            lock (_serial_locker) {
                while (port.IsOpen) {
                    try {
                        SerialData = port.ReadLine().Replace("\r", string.Empty)
                                                    .Replace("SIGKILL", "\n");

                        if (SerialData != string.Empty &&
                            Helpers.Xml.AppConfigQuery(
                            ApplicationSettings.ApplicationStructure.SystemEventsRoot +
                            "/event[@id='" + SerialData + "']").Count > 0) {
                            Action ParseSerialData = () => {
                                try {
                                    Helpers.Xml.AppConfigQuery(
                                    ApplicationSettings.ApplicationStructure.SystemEventsRoot +
                                    "/event[@id='" + SerialData + "']").Item(0).InnerText.Parse();
                                }
                                catch {
                                    //null reference exception from Xml.AppConfigQuery
                                }
                            };
                            Process.CallWithTimeout(ParseSerialData, 30000);
                        }
                    }
                    catch (Exception e) {
                        if (e is TimeoutException) {
                            //Logger.Instance.Append(string.Format("Serial Exception <SerialPortListener, Timeout>: {0}", e.Message));
                        }
                        else {
                            //Logger.Instance.Append(string.Format("Serial Exception <SerialPortListener>: {0}", e.Message));
                        }
                    }
                }
            }
        }

        static readonly object _write_locker = new object();

        internal static string WriteToSerialPort(string message, string typeOfSerialMessage = "send", int timeout = 10000) {
            try {
                lock (_write_locker) {
                    if (port.IsOpen) {
                        if (message.Trim() != string.Empty && typeOfSerialMessage == "send") {
                            // Clear all buffers
                            port.DiscardInBuffer();
                            port.DiscardOutBuffer();
                            SerialData = string.Empty;
                            // Send a new argument
                            port.WriteLine(message);
                            Thread.Sleep(220);
                        }
                        Process.CallWithTimeout(() => {
                            while (SerialData == string.Empty)
                                Thread.Sleep(50);
                        }, timeout);
                    }
                    else
                        return "Serial port state: " + port.IsOpen;
                }
                return SerialData;
            }
            catch {
                return SerialData;
                //Suppress
                //Logger.Instance.Append(string.Format("Serial Exception <JudoParser>: {0}", e.Message));
            }
        }
    }
}