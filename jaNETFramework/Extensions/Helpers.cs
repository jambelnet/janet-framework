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
using System.Net;
using System.Web.Script.Serialization;
using System.Xml;

namespace jaNETFramework
{
    static class Helpers
    {
        internal static String getRawData(string uri) {
            String rawData;

            using (var wc = new WebClient())
                rawData = wc.DownloadString(uri);

            return rawData;
        }

        internal static class Xml
        {
            internal static List<String> SelectNodes(string endpoint, string node, string attribute) {
                string[] nsprefixuri = node.Split('=');
                string ns = nsprefixuri[0].Substring(nsprefixuri[0].LastIndexOf(':') + 1);
                string uri = nsprefixuri[1].Trim();

                string n = attribute.Substring(0, attribute.LastIndexOf('/'));
                string attr = attribute.Substring(attribute.LastIndexOf('/') + 1);

                return SelectNodes(endpoint, ns, uri, n, attr);
            }

            static List<String> SelectNodes(string endpoint, string namespacePrefix, string namespaceUri, string node, string attribute) {
                XmlNodeList nodes = SelectNodeList(endpoint, namespacePrefix, namespaceUri, node);

                var e = new List<String>();

                foreach (XmlNode n in nodes)
                    if (!String.IsNullOrEmpty(attribute))
                        e.Add(n.Attributes[attribute].InnerText);
                    else
                        e.Add(n.InnerText);

                return e;
            }

            internal static XmlNodeList SelectNodeList(string endpoint, string namespacePrefix, string namespaceUri, string node) {
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(getRawData(endpoint));

                var ns = new XmlNamespaceManager(xmlDoc.NameTable);
                ns.AddNamespace(namespacePrefix, namespaceUri);

                return xmlDoc.SelectNodes(node, ns);
            }

            internal static String SelectSingleNode(string endpoint, string node, int nodeIndex) {
                try {
                    var xmlDoc = new XmlDocument();
                    xmlDoc.LoadXml(getRawData(endpoint));

                    return nodeIndex <= 0 ?
                        xmlDoc.SelectNodes(node).Item(0).InnerText
                    :
                        xmlDoc.SelectNodes(node).Item(nodeIndex).InnerText;
                }
                catch {
                    return null;
                }
            }

            internal static String SelectSingleNode(string endpoint, string node) {
                return SelectSingleNode(endpoint, node, 0);
            }

            internal static XmlNodeList AppConfigQuery(string xPathNode) {
                try {
                    var xmlDoc = new XmlDocument();

                    xmlDoc.Load(Methods.Instance.GetApplicationPath() + "AppConfig.xml");

                    return xmlDoc.SelectNodes(xPathNode);
                }
                catch //(Exception e)
                {
                    return null;
                    //Logger.Instance.Append("obj [ Helpers.AppConfigQuery <Exception> ]: Exception: [ " + e.Message + " ] Message: [ Your AppConfig.xml is not well formed according to the XML specification ]");
                    //throw new ArgumentNullException();
                }
            }
        }

        internal class Json
        {
            // Json on line editor: http://codebeautify.org/online-json-editor
            internal String SelectSingleNode(string endpoint, string node) {
                string json = getRawData(endpoint);

                var serializer = new JavaScriptSerializer();
                dynamic item = serializer.Deserialize<object>(json);

                var steps = node.Split('/');
                for (var i = 0; i < steps.Length; i++) {
                    int n;
                    // item = int.TryParse(steps[i], out n) ? item[n] : item[steps[i]];
                    if (int.TryParse(steps[i], out n))
                        item = item[n];
                    else
                        item = item[steps[i]];
                    if (item == null) return string.Empty;
                }

                return item.ToString();
            }
        }

        internal static class Http
        {
            internal static String Get(string requestURI) {
                return getRawData(requestURI);
            }
        }
    }
}
