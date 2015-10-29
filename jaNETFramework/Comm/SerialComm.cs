/* *****************************************************************************************************************************
 * (c) J@mBeL.net 2010-2015
 * Author: John Ambeliotis
 * Created: 24 Apr. 2010
 *
 * License:
 *  This file is part of Project jaNET.

    Project jaNET is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    Project jaNET is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with Project jaNET. If not, see <http://www.gnu.org/licenses/>. */

using System;
using System.IO;
using System.IO.Ports;
using System.Threading;

namespace jaNETFramework
{
    static class SerialComm
    {
        static readonly object _serial_locker = new object();
        internal static volatile string SerialData = string.Empty;
        internal static SerialPort port = new SerialPort();
        
        internal static void ActivateSerialPort(string portName)
        {
            try
            {
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

                if (OperatingSystem.Version == OperatingSystem.Type.Unix
                    || OperatingSystem.Version == OperatingSystem.Type.MacOS)
                    if (!File.Exists(port.PortName))
                        return;
                if (!port.IsOpen)
                    port.Open();

                var t = new Thread(SerialPortListener);
                t.IsBackground = true;
                t.Start();
            }
            catch {
                try {
                    throw new InvalidOperationException("Serial port state: " + port.IsOpen);
                }
                catch {
                    // Suppress
                }
            }
        }

        internal static void DeactivateSerialPort()
        {
            try {
                if (port.IsOpen)
                    port.Close();
            }
            catch {
                throw new InvalidOperationException("Serial port state: " + port.IsOpen);
            }
        }

        static void SerialPortListener()
        {
            lock (_serial_locker)
            {
                while (port.IsOpen)
                {
                    try
                    {
                        SerialData = port.ReadLine().Replace("\r", string.Empty)
                                                    .Replace("SIGKILL", "\n");

                        if (SerialData != string.Empty &&
                            Helpers.Xml.AppConfigQuery(
                            ApplicationSettings.ApplicationStructure.SystemEventsRoot + 
                            "/event[@id='" + SerialData + "']").Count > 0)
                        {
                            Action ParseSerialData = () =>
                            {
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
                    catch (Exception e)
                    {
                        if (e is TimeoutException)
                        {
                            //
                        }
                        else
                        {
                            //Console.WriteLine("Serial Exception: {0}", ex.Message);
                        }
                    }
                }
            }
        }
    }
}
