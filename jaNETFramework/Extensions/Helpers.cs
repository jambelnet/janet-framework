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
using jaNET.Environment;
using System;
using System.Collections.Generic;
using System.Net;
using System.Web.Script.Serialization;
using System.Xml;

namespace jaNET
{
    static class Helpers
    {
        static String GetRawData(string uri) {
            string rawData;

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
                    if (!string.IsNullOrEmpty(attribute))
                        e.Add(n.Attributes[attribute].InnerText);
                    else
                        e.Add(n.InnerText);

                return e;
            }

            internal static XmlNodeList SelectNodeList(string endpoint, string namespacePrefix, string namespaceUri, string node) {
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(GetRawData(endpoint));

                var ns = new XmlNamespaceManager(xmlDoc.NameTable);
                ns.AddNamespace(namespacePrefix, namespaceUri);

                return xmlDoc.SelectNodes(node, ns);
            }

            internal static String SelectSingleNode(string endpoint, string node, int nodeIndex = 0) {
                try {
                    var xmlDoc = new XmlDocument();
                    xmlDoc.LoadXml(GetRawData(endpoint));

                    return nodeIndex <= 0 ?
                        xmlDoc.SelectNodes(node).Item(0).InnerText :
                        xmlDoc.SelectNodes(node).Item(nodeIndex).InnerText;
                }
                catch {
                    return null;
                }
            }

            internal static XmlNodeList AppConfigQuery(string xPathNode) {
                try {
                    var xmlDoc = new XmlDocument();

                    xmlDoc.Load(Methods.Instance.GetApplicationPath + "AppConfig.xml");

                    return xmlDoc.SelectNodes(xPathNode);
                }
                catch (Exception e) {
                    Logger.Instance.Append(string.Format("obj [ Helpers.Xml.AppConfigQuery <Exception> ] Arguments: [ {0} ] Exception Message: [ {1} ]", xPathNode + ". Malformed AppConfig.xml or not found.", e.Message));
                    return null;
                }
            }
        }

        internal class Json
        {
            // Json on line editor: http://codebeautify.org/online-json-editor
            internal String SelectSingleNode(string endpoint, string node) {
                string json = GetRawData(endpoint);

                dynamic item = new JavaScriptSerializer().Deserialize<object>(json); //// ToDo: JavaScriptSerializer is no longer used for Xamarin compatibility.

                var steps = node.Split('/');
                for (var i = 0; i < steps.Length; i++) {
                    int n;
                    //item = int.TryParse(steps[i], out n) ? item[n] : item[steps[i]];
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
                return GetRawData(requestURI);
            }
        }

        // https://github.com/sami1971/SimplyMobile/blob/master/Core/Plugins/SimplyMobile.Text.RuntimeSerializer/JsonSerializer.cs
        //public class JsonSerializer
        //{
        //    private Lazy<List<Type>> types = new Lazy<List<Type>>();

        //    //public Format Format {
        //    //    get { return Format.Json; }
        //    //}

        //    public void AddKnownType<T>() {
        //        this.types.Value.Add(typeof(T));
        //    }

        //    public string Serialize<T>(T obj) {
        //        using (var memoryStream = new MemoryStream())
        //        using (var reader = new StreamReader(memoryStream)) {
        //            var serializer = new DataContractJsonSerializer(obj.GetType(), new DataContractJsonSerializerSettings {
        //                UseSimpleDictionaryFormat = true
        //            }); //); //, this.types.Value);
        //            serializer.WriteObject(memoryStream, obj);
        //            memoryStream.Position = 0;
        //            return reader.ReadToEnd();
        //        }
        //    }

        //    /// <summary>
        //    /// Serializes object to a stream
        //    /// </summary>
        //    /// <param name="obj">Object to serialize</param>
        //    /// <param name="stream">Stream to serialize to</param>
        //    public void Serialize<T>(T obj, Stream stream) {
        //        var serializer = new DataContractJsonSerializer(obj.GetType(), new DataContractJsonSerializerSettings {
        //            UseSimpleDictionaryFormat = true
        //        });
        //        serializer.WriteObject(stream, obj);
        //    }

        //    public T Deserialize<T>(string data) {
        //        return (T)this.Deserialize(data, typeof(T));
        //    }

        //    /// <summary>
        //    /// Deserializes stream into an object
        //    /// </summary>
        //    /// <typeparam name="T">Type of object to serialize to</typeparam>
        //    /// <param name="stream">Stream to deserialize from</param>
        //    /// <returns>Object of type T</returns>
        //    public T Deserialize<T>(Stream stream) where T : class {
        //        var serializer = new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings {
        //            UseSimpleDictionaryFormat = true
        //        });
        //        return serializer.ReadObject(stream) as T;
        //    }

        //    public object Deserialize(string data, Type type) {
        //        using (var reader = new MemoryStream(Encoding.UTF8.GetBytes(data))) {
        //            var serializer = new DataContractJsonSerializer(type, new DataContractJsonSerializerSettings {
        //                UseSimpleDictionaryFormat = true
        //            });
        //            return serializer.ReadObject(reader);
        //        }
        //    }
        //}
    }
}