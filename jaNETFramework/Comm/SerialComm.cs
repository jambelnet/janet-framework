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

using jaNET.Diagnostics;
using jaNET.Environment.AppConfig;
using jaNET.Extensions;
using System;
using System.IO;
using System.IO.Ports;
using System.Threading;

namespace jaNET.IO.Ports
{
    static class SerialComm
    {
        public enum TypeOfSerialMessage
        {
            None,
            SendText,
            SendByteArray,
            Listen,
            Monitor
        }

        static readonly object _serial_locker = new object();
        static readonly object _write_locker = new object();
        internal static volatile string SerialData = string.Empty;
        internal static SerialPort port = new SerialPort();

        internal static void ActivateSerialPort(string portName) {
            try {
                bool isPOSIX = Environment.OperatingSystem.Version == Environment.OperatingSystem.Type.Unix
                                || Environment.OperatingSystem.Version == Environment.OperatingSystem.Type.MacOS;

                if (isPOSIX && !File.Exists(port.PortName))
                    return;

                // Port
                if (portName == string.Empty)
                    port.PortName = Helpers.Xml.AppConfigQuery(
                        AppStructure.ComPortPath)
                        .Item(0).InnerText;
                else
                    port.PortName = portName;
                // Baud
                port.BaudRate = Convert.ToInt32(
                    Helpers.Xml.AppConfigQuery(
                    AppStructure.ComBaudRatePath)
                    .Item(0).InnerText);

                port.DataReceived += new SerialDataReceivedEventHandler(port_DataReceived);
                port.ReadTimeout = 500;
                port.Open();

                var t = new Thread(SerialPortListener);
                t.IsBackground = true;
                t.Start();
            }
            catch {

            }
        }

        internal static void DeactivateSerialPort() {
            if (port.IsOpen)
                port.Close();
        }

        static void SerialPortListener() {
            lock (_serial_locker) {
                while (port.IsOpen) {
                    try {
                        SerialData = port.ReadLine().Replace("\r", string.Empty)
                                                    .Replace("SIGKILL", "\n");

                        if (SerialData != string.Empty &&
                            Helpers.Xml.AppConfigQuery(
                            AppStructure.SystemEventsRoot + "/event[@id='" + SerialData + "']").Count > 0) {
                            Process.CallWithTimeout(() => {
                                try {
                                    Helpers.Xml.AppConfigQuery(
                                    AppStructure.SystemEventsRoot +
                                    "/event[@id='" + SerialData + "']").Item(0).InnerText.Parse();
                                } catch {
                                    //null reference exception from Xml.AppConfigQuery
                                }
                            }, 10000);
                        }
                    } catch (Exception e) {
                        if (e is TimeoutException) {

                        } else {

                        }
                    }
                }
            }
        }

        internal static string WriteToSerialPort(object message, TypeOfSerialMessage typeOfSerialMessage, int timeout = 1000)
        {
            bool _done = false;
            if (port.IsOpen) {
                try {
                    lock (_write_locker) {
                        // Clear all buffers
                        port.DiscardInBuffer();
                        port.DiscardOutBuffer();
                        SerialData = string.Empty;
                        if (typeOfSerialMessage == TypeOfSerialMessage.SendText) {
                            // Send a new argument
                            port.WriteLine(message.ToString());
                        } else if (typeOfSerialMessage == TypeOfSerialMessage.SendByteArray) {
                            byte[] msg = message as byte[];
                            //port.Write("F9"); // 46 39 is F9
                            port.Write(msg, 0, msg.Length);
                        }
                        //Thread.Sleep(220);
                        Process.CallWithTimeout(() => {
                            while (!_done) {
                                _done |= !string.IsNullOrWhiteSpace(SerialData);
                                Thread.Sleep(50);
                            }
                        }, timeout);
                    }
                }
                catch {
                    _done = true;
                }
            }
            else
                return string.Format("Serial port state: {0}", port.IsOpen);
            return SerialData;
        }

        private static void port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            // Show all the incoming data in the port's buffer.
            SerialData = port.ReadExisting();
        }
    }
}