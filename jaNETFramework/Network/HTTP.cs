/* *****************************************************************************************************************************
 * (c) J@mBeL.net 2010-2017
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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace jaNETFramework
{
    static partial class Server
    {
        internal static class Web
        {
            internal struct Request
            {
                internal enum DataType
                {
                    html,
                    json,
                    text
                }
            }

            class Login
            {
                string Username { get; set; }
                string Password { get; set; }

                internal Login() {
                    var webLogin = new Settings().LoadSettings(".htaccess");

                    if (webLogin != null) {
                        Username = webLogin[0];
                        Password = webLogin[1];
                    }
                }

                internal bool Authenticate(string u, string p) {
                    if (Username == u && Password == p)
                        return true;
                    return false;
                }
            }

            internal static readonly HttpListener httplistener = new HttpListener();

            internal static async void Start() {
                string Prefix = "http://localhost:8080/";
                string AuthenticationType = "none";

                if (!HttpListener.IsSupported)
                    throw new NotSupportedException(
                        "Server is not supported.");

                if (httplistener.IsListening)
                    return;

                try {
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
                catch (NullReferenceException e) {
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

                while (httplistener.IsListening) {
                    try {
                        var ctx = await httplistener.GetContextAsync();
                        Task.Run(() => ProcessRequestAsync(ctx));
                    }
                    catch (HttpListenerException e) {
                        Logger.Instance.Append("obj [ Server.Web.Start <Exception> ]: HttpListenerException [ " + e.Message + " ]");
                        //Restart();
                    }
                    catch (InvalidOperationException) {
                        //FileSystem.Log.Append("obj [ Server.Web.Start <Exception> ]: InvalidOperationException [ " + e.Message + " ]");
                        //Restart();
                    }
                    catch (Exception e) {
                        Logger.Instance.Append("obj [ Server.Web.Start <Exception> ]: Generic [ " + e.Message + " ]");
                        //Restart();
                    }
                }
            }

            static async Task ProcessRequestAsync(HttpListenerContext ctx) {
                //string[] MIME_Image = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico" };
                //string[] MIME_Text = { ".html", ".htm", ".xml", ".css", ".js", ".txt" };
                try {
                    string mapPath = Methods.Instance.GetApplicationPath() + SimpleUriDecode(ctx.Request.RawUrl.Substring(1));
                    byte[] buf = null;

                    if (isAuthenticated(ctx)) {
                        ctx.Response.StatusCode = (int)HttpStatusCode.OK;

                        if (mapPath.EndsWith("/", StringComparison.Ordinal))
                            mapPath += "index.html";

                        // Filtering file types
                        //if (Array.Find(MIME_Image, s => s.Contains(mapPath.Substring(mapPath.LastIndexOf('.')))) != null)
                        //buf = File.ReadAllBytes(mapPath);
                        //else if (Array.Find(MIME_Text, s => s.Contains(mapPath.Substring(mapPath.LastIndexOf('.')))) != null || mapPath.Contains("?cmd="))
                        //buf = Encoding.UTF8.GetBytes(SendResponse(mapPath));
                        if (mapPath.Contains("?cmd=")) {
                            var t = Request.DataType.html;

                            if (mapPath.Contains("&mode=json"))
                                t = Request.DataType.json;
                            if (mapPath.Contains("&mode=text"))
                                t = Request.DataType.text;

                            buf = Encoding.UTF8.GetBytes(Parser.Instance.Parse(mapPath.Substring(mapPath.LastIndexOf("?cmd=", StringComparison.Ordinal))
                                                               .Replace("?cmd=", string.Empty)
                                                               .Replace("&mode=text", string.Empty)
                                                               .Replace("&mode=json", string.Empty)
                                                               .Replace("&mode=html", string.Empty)
                                                               .Replace("&", ";")
                                                               , t, false));
                        }
                        else
                            buf = File.ReadAllBytes(mapPath);
                    }
                    else {
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
                catch (InvalidOperationException) {
                    //FileSystem.Log.Append("obj [ Server.Web.ProcessRequestAsync <InvalidOperationException> ]: " + e.Message);
                }
                catch (Exception e) {
                    if (!e.Message.Contains("favicon.ico") && !e.Message.Contains("The object was used after being disposed."))
                        Logger.Instance.Append("obj [ Server.Web.ProcessRequestAsync <Exception> ]: " + e.Message);
                }
            }

            static bool isAuthenticated(HttpListenerContext ctx) {
                if (httplistener.AuthenticationSchemes == AuthenticationSchemes.Basic) {
                    var identity = (HttpListenerBasicIdentity)ctx.User.Identity;
                    return new Login().Authenticate(ctx.User.Identity.Name, identity.Password);
                }
                return true;
            }

            internal static void Stop() {
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

            internal static string SimpleUriDecode(string uri) {
                for (int i = 0; i < CharSet.GetUpperBound(0); i++)
                    uri = uri.Replace(CharSet[i, 1], CharSet[i, 0]);

                return uri;
            }

            internal static string SimpleUriEncode(string uri) {
                for (int i = 0; i < CharSet.GetUpperBound(0); i++)
                    uri = uri.Replace(CharSet[i, 0], CharSet[i, 1]);

                return uri;
            }
        }
    }
}