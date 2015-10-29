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
    along with Project jaNET. If not, see <http://www.gnu.org/licenses/>.
   
    Resources: http://msdn.microsoft.com/en-us/library/system.net.sockets.tcplistener.aspx
               http://www.codehosting.net/blog/BlogEngine/post/Simple-C-Web-Server.aspx 
               http://www.albahari.com/nutshell/cs5ch16.aspx */

using System;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace jaNETFramework
{
    static class Server
    {
        internal static class TCP
        {
            static TcpListener server;
            internal static volatile bool ServerState;

            internal static void Start()
            {
                if (!ServerState)
                {
                    var t = new Thread(ListenForClients);
                    t.IsBackground = true;
                    t.Start();
                }
            }

            internal static void Stop()
            {
                if (ServerState)
                {
                    ServerState = false;
                    server.Stop();
                }
            }

            static void ListenForClients()
            {
                try
                {
                    IPAddress localAddr = IPAddress.Parse("127.0.0.1");
                    Int32 port = 5744;
                    String trusted = "127.0.0.1";

                    if (Helpers.Xml.AppConfigQuery(ApplicationSettings.ApplicationStructure.LocalHostPath).Count > 0)
                        localAddr = IPAddress.Parse(
                            Helpers.Xml.AppConfigQuery(
                            ApplicationSettings.ApplicationStructure.LocalHostPath)
                            .Item(0).InnerText.Replace("localhost", "127.0.0.1"));
                    if (Helpers.Xml.AppConfigQuery(ApplicationSettings.ApplicationStructure.LocalPortPath).Count > 0)
                        port = Convert.ToInt32(
                            Helpers.Xml.AppConfigQuery(
                            ApplicationSettings.ApplicationStructure.LocalPortPath)
                            .Item(0).InnerText);
                    if (Helpers.Xml.AppConfigQuery(ApplicationSettings.ApplicationStructure.TrustedPath).Count > 0)
                        trusted = Helpers.Xml.AppConfigQuery(ApplicationSettings.ApplicationStructure.TrustedPath)
                            .Item(0).InnerText.Replace("localhost", "127.0.0.1");

                    server = new TcpListener(localAddr, port);
                    // Start listening for client requests.
                    server.Start();

                    ServerState = true;

                    // Buffer for reading data
                    var bytes = new Byte[1024];
                    String data = null;

                    // Enter the listening loop.
                    while (ServerState)
                    {
                        // Perform a blocking call to accept requests.
                        // You could also user server.AcceptSocket() here.
                        TcpClient client = server.AcceptTcpClient();
                        // Get a stream object for reading and writing
                        NetworkStream stream = client.GetStream();
                        data = null;
                        int i;

                        // Loop to receive all the data sent by the client.
                        while ((i = stream.Read(bytes, 0, bytes.Length)) != 0)
                        {
                            byte[] response = null;
                            // Translate data bytes to a ASCII string.
                            data += Web.SimpleUriDecode(Encoding.ASCII.GetString(bytes, 0, i));

                            Match mItem = Regex.Match(data, "GET.*HTTP");

                            if (mItem.Success)
                            {
                                data = string.Format("{0}\r\n", mItem.ToString().Replace("GET /", string.Empty).Replace("HTTP", string.Empty).Trim());
                                if (data.ToLower().Contains("favicon.ico"))
                                    break;
                            }

                            try
                            {
                                if (!trusted.Contains(
                                      IPAddress.Parse(((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString()).ToString()
                                  ))
                                {
                                    throw new UnauthorizedAccessException();
                                }
                                if (data.IndexOf("\r\n", StringComparison.Ordinal) >= 0)
                                {
                                    response = Encoding.ASCII.GetBytes(string.Format("{0}\r\n", data.Replace("\r\n", string.Empty).Parse()));
                                    // Send back a response.
                                    stream.Write(response, 0, response.Length);
                                    data = string.Empty;
                                    if (mItem.Success)
                                        break; // HTTP post, need to break when finished
                                }
                            }
                            catch (UnauthorizedAccessException e)
                            {
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
                catch (SocketException e)
                {
                    ServerState = false;
                    server.Stop();
                    Logger.Instance.Append("obj [ Server.TCP.ListenforClients <SocketException> ]: " + e.Message);
                    //Debug.Print("SocketException: {0}", e);
                }
                /*catch (Exception e) {
                    FileSystem.Log.Append("obj [ Server.TCP.ListenforClients <Exception> ]: " + e.Message);
                }*/
                /*finally
                {
                    // Stop listening for new clients.
                    ServerState = false;
                    server.Stop();
                }*/
            }
        }

        internal static class Web
        {
            class Login
            {
                string Username { get; set; }
                string Password { get; set; }

                internal Login()
                {
                    IList<String> webLogin = new Settings().LoadSettings(".htaccess");

                    if (webLogin != null)
                    {
                        Username = webLogin[0];
                        Password = webLogin[1];
                    }
                }

                internal bool Authenticate(string u, string p)
                {
                    if (Username == u && Password == p)
                        return true;
                    return false;
                }
            }

            internal static readonly HttpListener httplistener = new HttpListener();

            internal static async void Start()
            {
                string Prefix = "http://localhost:8080/";
                string AuthenticationType = "none";

                if (!HttpListener.IsSupported)
                    throw new NotSupportedException(
                        "Server is not supported.");

                if (httplistener.IsListening)
                    return;

                try
                {
                    if (Helpers.Xml.AppConfigQuery(ApplicationSettings.ApplicationStructure.HttpAuthenticationPath).Count > 0)
                        Prefix = "http://" + Helpers.Xml.AppConfigQuery(
                                                ApplicationSettings.ApplicationStructure.HttpHostNamePath)
                                                .Item(0).InnerText + ":" +
                                                Helpers.Xml.AppConfigQuery(
                                                ApplicationSettings.ApplicationStructure.HttpPortPath)
                                                .Item(0).InnerText + "/";
                    if (Helpers.Xml.AppConfigQuery(ApplicationSettings.ApplicationStructure.HttpAuthenticationPath).Count > 0)
                        AuthenticationType = Helpers.Xml.AppConfigQuery(
                                                ApplicationSettings.ApplicationStructure.HttpAuthenticationPath)
                                                .Item(0).InnerText;
                }
                catch (NullReferenceException e)
                {
                    Logger.Instance.Append("obj [ Server.Web.Start <Exception> ]: NullReferenceException [ " + e.Message + " ]");
                    return;
                }

                httplistener.Prefixes.Clear();
                httplistener.Prefixes.Add(Prefix);
                httplistener.AuthenticationSchemes = AuthenticationType.ToLower() == "basic" ?
                    httplistener.AuthenticationSchemes = AuthenticationSchemes.Basic :
                    httplistener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
                httplistener.IgnoreWriteExceptions = true;
                httplistener.Start();

                while (httplistener.IsListening)
                {
                    try
                    {
                        var ctx = await httplistener.GetContextAsync();
                        Task.Run(() => ProcessRequestAsync(ctx));
                    }
                    catch (HttpListenerException e)
                    {
                        Logger.Instance.Append("obj [ Server.Web.Start <Exception> ]: HttpListenerException [ " + e.Message + " ]");
                        //Restart();
                    }
                    catch (InvalidOperationException)
                    {
                        //FileSystem.Log.Append("obj [ Server.Web.Start <Exception> ]: InvalidOperationException [ " + e.Message + " ]");
                        //Restart();
                    }
                    catch (Exception e)
                    {
                        Logger.Instance.Append("obj [ Server.Web.Start <Exception> ]: Generic [ " + e.Message + " ]");
                        //Restart();
                    }
                }
            }

            static async Task ProcessRequestAsync(HttpListenerContext ctx)
            {
                //string[] MIME_Image = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico" };
                //string[] MIME_Text = { ".html", ".htm", ".xml", ".css", ".js", ".txt" };
                try
                {
                    string mapPath = Methods.Instance.GetApplicationPath() + SimpleUriDecode(ctx.Request.RawUrl.Substring(1));
                    byte[] buf = null;

                    if (isAuthenticated(ctx))
                    {
                        ctx.Response.StatusCode = (int)HttpStatusCode.OK;

                        if (mapPath.EndsWith("/", StringComparison.Ordinal))
                            mapPath += "index.html";

                        // Filtering file types
                        //if (Array.Find(MIME_Image, s => s.Contains(mapPath.Substring(mapPath.LastIndexOf('.')))) != null)
                        //buf = File.ReadAllBytes(mapPath);
                        //else if (Array.Find(MIME_Text, s => s.Contains(mapPath.Substring(mapPath.LastIndexOf('.')))) != null || mapPath.Contains("?cmd="))
                        //buf = Encoding.UTF8.GetBytes(SendResponse(mapPath));
                        if (mapPath.Contains("?cmd="))
                            buf = Encoding.UTF8.GetBytes(Parser.Instance.Parse(mapPath.Substring(mapPath.LastIndexOf("?cmd=", StringComparison.Ordinal))
                                                               .Replace("?cmd=", string.Empty), true, false));
                        else
                            buf = File.ReadAllBytes(mapPath);
                    }
                    else
                    {
                        ctx.Response.StatusCode = 401;
                        if (OperatingSystem.Version == OperatingSystem.Type.Unix)
                            ctx.Response.AddHeader("WWW-Authenticate", "Basic Realm=\"Authentication Required\""); // show login dialog
                        buf = Encoding.UTF8.GetBytes("<html><head><title>401 Authorization Required</title></head>" +
                        "<body><h1>Authorization Required</h1>This server could not verify that you are authorized to access the document requested. Either you supplied the wrong credentials (e.g., bad password), or your browser doesn't understand how to supply the credentials required.<hr>" +
                        "</body></html>");
                    }

                    ctx.Response.ContentLength64 = buf.Length;
                    using (Stream s = ctx.Response.OutputStream)
                        await s.WriteAsync(buf, 0, buf.Length);
                }
                catch (InvalidOperationException)
                {
                    //FileSystem.Log.Append("obj [ Server.Web.ProcessRequestAsync <InvalidOperationException> ]: " + e.Message);
                }
                catch (Exception e)
                {
                    if (!e.Message.Contains("favicon.ico") && !e.Message.Contains("The object was used after being disposed."))
                        Logger.Instance.Append("obj [ Server.Web.ProcessRequestAsync <Exception> ]: " + e.Message);
                }
            }

            static bool isAuthenticated(HttpListenerContext ctx)
            {
                if (httplistener.AuthenticationSchemes == AuthenticationSchemes.Basic)
                {
                    var identity = (HttpListenerBasicIdentity)ctx.User.Identity;
                    return new Login().Authenticate(ctx.User.Identity.Name, identity.Password);
                }
                return true;
            }

            internal static void Stop()
            {
                httplistener.Stop();
            }

            /*internal static void Restart()
            {
                httplistener.Stop();
                Thread.Sleep(500);
                Start();
            }*/

            /*static string SendResponse(string request)
            {
                var client = new WebClient();

                if (request.Contains("?cmd="))
                    return Parser.Parse(request.Substring(request.LastIndexOf("?cmd="))
                                                                 .Replace("?cmd=", string.Empty), true, false);

                return client.DownloadString(request);
            }*/

            static readonly string[,] CharSet = {
                {" ", "%20"},
                {"!", "%21"},
                {"\"", "%22"},
                {"#", "%23"},
                {"$", "%24"},
                {"%", "%25"},
                {"&", "%26"},
                {"'", "%27"},
                {"{", "%28"},
                {"}", "%29"},
                {"*", "%2A"},
                {"+", "%2B"},
                {",", "%2C"},
                {"-", "%2D"},
                {".", "%2E"},
                {"/", "%2F"},
                {":", "%3A"},
                {";", "%3B"},
                {"<", "%3C"},
                {"=", "%3D"},
                {">", "%3E"},
                {"?", "%3F"},
                {"@", "%40"},
                {"[", "%5B"},
                {@"\", "%5C"},
                {"]", "%5D"},
                {"^", "%5E"},
                {"_", "%5F"},
                {"`", "%60"},
                {"{", "%7B"},
                {"|", "%7C"},
                {"}", "%7D"},
                {"~", "%7E"}
            };

            internal static string SimpleUriDecode(string uri)
            {
                for (int i = 0; i < CharSet.GetUpperBound(0); i++)
                    uri = uri.Replace(CharSet[i, 1], CharSet[i, 0]);

                return uri;
            }

            internal static string SimpleUriEncode(string uri)
            {
                for (int i = 0; i < CharSet.GetUpperBound(0); i++)
                    uri = uri.Replace(CharSet[i, 0], CharSet[i, 1]);

                return uri;
            }
        }
    }
}