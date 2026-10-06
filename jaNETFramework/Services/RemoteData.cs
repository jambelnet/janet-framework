/* *****************************************************************************************************************************
 * (c) J@mBeL.net 2010-2026
 * Author: John Ambeliotis
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

using jaNET.Infrastructure;
using System;
using System.Collections.Generic;
using System.Xml;

namespace jaNET.Services;

/// <summary>Picks single values out of JSON and XML web services ("judo json get", "judo xml get").</summary>
internal sealed class RemoteData
{
    readonly IHttpFetcher _http;

    public RemoteData(IHttpFetcher http) {
        _http = http;
    }

    /// <summary>The value at a '/' separated path of property names and array indexes, e.g. "main/temp" or "list/0/name".</summary>
    public string JsonValue(string endpoint, string path) => JsonCompat.SelectValue(_http.Get(endpoint), path);

    /// <summary>The text of one node, or null if the document or the node cannot be read. A <paramref name="nodeIndex"/> of 0 or less selects the first match.</summary>
    public string? XmlNode(string endpoint, string xpath, int nodeIndex = 0) {
        try {
            XmlNodeList nodes = Load(endpoint).SelectNodes(xpath)!;
            return nodes.Item(nodeIndex <= 0 ? 0 : nodeIndex)!.InnerText;
        }
        catch (Exception) {
            return null;
        }
    }

    /// <summary>
    /// The text of all nodes in a namespace ("prefix:name=uri" for <paramref name="namespaceDeclaration"/>),
    /// or of one attribute of those nodes when <paramref name="nodeAndAttribute"/> is "node/attribute".
    /// </summary>
    public List<string> XmlNodes(string endpoint, string namespaceDeclaration, string nodeAndAttribute) {
        string[] declaration = namespaceDeclaration.Split('=');
        string prefix = declaration[0].Substring(declaration[0].LastIndexOf(':') + 1);
        string uri = declaration[1].Trim();

        string node = nodeAndAttribute.Substring(0, nodeAndAttribute.LastIndexOf('/'));
        string attribute = nodeAndAttribute.Substring(nodeAndAttribute.LastIndexOf('/') + 1);

        XmlDocument document = Load(endpoint);
        var namespaces = new XmlNamespaceManager(document.NameTable);
        namespaces.AddNamespace(prefix, uri);

        var values = new List<string>();
        foreach (XmlNode n in document.SelectNodes(node, namespaces)!)
            values.Add(!string.IsNullOrEmpty(attribute) ? n.Attributes![attribute]!.InnerText : n.InnerText);
        return values;
    }

    XmlDocument Load(string endpoint) {
        var document = new XmlDocument();
        document.LoadXml(_http.Get(endpoint));
        return document;
    }
}
