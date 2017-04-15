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
using jaNET.Net.Http;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace jaNET.Net.Sockets
{
    internal static class TCP
    {
        static TcpListener server;
        internal static volatile bool ServerState;

        internal static void Start() {
            if (!ServerState) {
                var t = new Thread(ListenForClients);
                t.IsBackground = true;
                t.Start();
            }
        }

        internal static void Stop() {
            if (ServerState) {
                ServerState = false;
                server.Stop();
            }
        }

        static void ListenForClients() {
            try {
                var localAddr = IPAddress.Parse("127.0.0.1");
                int port = 5744;
                string trusted = "127.0.0.1";

                if (Helpers.Xml.AppConfigQuery(AppStructure.LocalHostPath).Count > 0)
                    localAddr = IPAddress.Parse(
                        Helpers.Xml.AppConfigQuery(
                        AppStructure.LocalHostPath)
                        .Item(0).InnerText.Replace("localhost", "127.0.0.1"));
                if (Helpers.Xml.AppConfigQuery(AppStructure.LocalPortPath).Count > 0)
                    port = Convert.ToInt32(
                        Helpers.Xml.AppConfigQuery(
                        AppStructure.LocalPortPath)
                        .Item(0).InnerText);
                if (Helpers.Xml.AppConfigQuery(AppStructure.TrustedPath).Count > 0)
                    trusted = Helpers.Xml.AppConfigQuery(AppStructure.TrustedPath)
                        .Item(0).InnerText.Replace("localhost", "127.0.0.1");

                server = new TcpListener(localAddr, port);
                // Start listening for client requests.
                server.Start();

                ServerState = true;

                // Buffer for reading data
                var bytes = new Byte[1024];
                string data = null;

                // Enter the listening loop.
                while (ServerState) {
                    // Perform a blocking call to accept requests.
                    // You could also user server.AcceptSocket() here.
                    TcpClient client = server.AcceptTcpClient();
                    // Get a stream object for reading and writing
                    NetworkStream stream = client.GetStream();
                    data = null;
                    int i;

                    // Loop to receive all the data sent by the client.
                    while ((i = stream.Read(bytes, 0, bytes.Length)) != 0) {
                        byte[] response = null;
                        // Translate data bytes to a ASCII string.
                        data += Web.SimpleUriDecode(Encoding.ASCII.GetString(bytes, 0, i));

                        var mItem = Regex.Match(data, "GET.*HTTP");

                        if (mItem.Success) {
                            data = string.Format("{0}\r\n", mItem.Value.Replace("GET /", string.Empty).Replace("HTTP", string.Empty).Trim());
                            if (data.ToLower().Contains("favicon.ico"))
                                break;
                        }

                        try {
                            if (!trusted.Contains(
                                  IPAddress.Parse(((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString()).ToString()
                              )) {
                                throw new UnauthorizedAccessException();
                            }
                            if (data.IndexOf("\r\n", StringComparison.Ordinal) >= 0) {
                                response = Encoding.ASCII.GetBytes(string.Format("{0}\r\n", data.Replace("\r\n", string.Empty).Parse()));
                                // Send back a response.
                                stream.Write(response, 0, response.Length);
                                data = string.Empty;
                                if (mItem.Success)
                                    break; // HTTP post, need to break when finished
                            }
                        }
                        catch (UnauthorizedAccessException e) {
                            response = Encoding.ASCII.GetBytes(string.Format("{0}\r\n", e.Message));
                            stream.Write(response, 0, response.Length);
                            data = string.Empty;
                        }

                        if (!ServerState)
                            throw new SocketException();
                    }
                    // Shutdown and end connection
                    client.Close();
                }
            }
            catch (SocketException e) {
                ServerState = false;
                server.Stop();
                Logger.Instance.Append(string.Format("obj [ Server.TCP.ListenforClients <SocketException> ]: {0}", e.Message));
                //Debug.Print("SocketException: {0}", e);
            }
            catch {
            }
            /*finally
            {
                // Stop listening for new clients.
                ServerState = false;
                server.Stop();
            }*/
        }
    }
}