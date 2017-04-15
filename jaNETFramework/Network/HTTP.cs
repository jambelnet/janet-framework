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
    along with jaNET Framework. If not, see <http://www.gnu.org/licenses/>.
   
    Resources: http://msdn.microsoft.com/en-us/library/system.net.sockets.tcplistener.aspx
               http://www.codehosting.net/blog/BlogEngine/post/Simple-C-Web-Server.aspx 
               http://www.albahari.com/nutshell/cs5ch16.aspx */

using jaNET.Diagnostics;
using jaNET.Environment;
using jaNET.Environment.AppConfig;
using jaNET.IO;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace jaNET.Net.Http
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
                var webLogin = new Settings().Load(".htaccess");

                if (webLogin != null) {
                    Username = webLogin[0];
                    Password = webLogin[1];
                }
            }

            internal bool Authenticate(string u, string p) {
                return (Username == u && Password == p);
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
                if (Helpers.Xml.AppConfigQuery(AppStructure.HttpAuthenticationPath).Count > 0)
                    Prefix = "http://" + Helpers.Xml.AppConfigQuery(
                                            AppStructure.HttpHostNamePath)
                                            .Item(0).InnerText + ":" +
                                            Helpers.Xml.AppConfigQuery(
                                            AppStructure.HttpPortPath)
                                            .Item(0).InnerText + "/";
                if (Helpers.Xml.AppConfigQuery(AppStructure.HttpAuthenticationPath).Count > 0)
                    AuthenticationType = Helpers.Xml.AppConfigQuery(
                                            AppStructure.HttpAuthenticationPath)
                                            .Item(0).InnerText;
            }
            catch (NullReferenceException e) {
                Logger.Instance.Append(string.Format("obj [ Server.Web.Start <Exception> ]: NullReferenceException [ {0} ]", e.Message));
                return;
            }

            try {
                httplistener.Prefixes.Clear();
                httplistener.Prefixes.Add(Prefix);
                httplistener.AuthenticationSchemes = AuthenticationType.ToLower() == "basic" ?
                    httplistener.AuthenticationSchemes = AuthenticationSchemes.Basic :
                    httplistener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
                httplistener.IgnoreWriteExceptions = true;
                httplistener.Start();
            }
            catch (Exception e) {
                Logger.Instance.Append(string.Format("obj [ Server.Web.Start <Exception> ]: Exception [ {0} ]", e.Message));
                return;
            }

            while (httplistener.IsListening) {
                try {
                    var ctx = await httplistener.GetContextAsync();
                    Task.Run(() => ProcessRequestAsync(ctx)); // Do not await
                }
                catch (HttpListenerException e) {
                    Logger.Instance.Append(string.Format("obj [ Server.Web.Start <Exception> ]: HttpListenerException [ {0} ]", e.Message));
                }
                catch (InvalidOperationException) {

                }
                catch (Exception e) {
                    Logger.Instance.Append(string.Format("obj [ Server.Web.Start <Exception> ]: Generic [ {0} ]", e.Message));
                }
            }
        }

        static async Task ProcessRequestAsync(HttpListenerContext ctx) {
            //string[] MIME_Image = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico" };
            //string[] MIME_Text = { ".html", ".htm", ".xml", ".css", ".js", ".txt" };
            string mapPath = Methods.Instance.GetApplicationPath + SimpleUriDecode(ctx.Request.RawUrl.Substring(1));
            byte[] buf = null;

            try {
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

                        buf = Encoding.UTF8.GetBytes(Parser.Instance.Parse(
                                                            Regex.Replace(mapPath.Substring(mapPath.LastIndexOf("?cmd=", StringComparison.Ordinal)),
                                                            @"\?cmd=|&mode=text|&mode=json|&mode=html", string.Empty)
                                                           , t, false));
                    }
                    else
                        buf = File.ReadAllBytes(mapPath);
                }
                else {
                    ctx.Response.StatusCode = 401;
                    if (Environment.OperatingSystem.Version == Environment.OperatingSystem.Type.Unix)
                        ctx.Response.AddHeader("WWW-Authenticate", "Basic Realm=\"Authentication Required\""); // Show login dialog
                    buf = Encoding.UTF8.GetBytes("<html><head><title>401 Authorization Required</title></head>" +
                    "<body><h1>Authorization Required</h1>This server could not verify that you are authorized to access the document requested. Either you supplied the wrong credentials (e.g., bad password), or your browser doesn't understand how to supply the credentials required.<hr>" +
                    "</body></html>");
                }

                ctx.Response.ContentLength64 = buf.Length;
                using (Stream s = ctx.Response.OutputStream)
                    await s.WriteAsync(buf, 0, buf.Length);
            }
            catch (InvalidOperationException) {

            }
            catch (Exception e) {
                if (!e.Message.Contains("favicon.ico") && !e.Message.Contains("The object was used after being disposed."))
                    Logger.Instance.Append(string.Format("obj [ Server.Web.ProcessRequestAsync <Exception> ]: {0}\r\n{1}", e.Message, mapPath));
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