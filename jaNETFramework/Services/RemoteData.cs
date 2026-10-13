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
using System.Xml.XPath;

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

    /// <summary>The text of one node. A <paramref name="nodeIndex"/> of 0 or less selects the first match; errors are reported to the command runner.</summary>
    public string XmlNode(string endpoint, string xpath, int nodeIndex = 0) {
        XmlNodeList nodes = Select(Load(endpoint), xpath);
        int index = Math.Max(0, nodeIndex);
        if (index >= nodes.Count) throw new ArgumentOutOfRangeException(nameof(nodeIndex), "XML web service: the selected node index is out of range.");
        return nodes[index]!.InnerText;
    }

    /// <summary>
    /// The text of all nodes in a namespace ("prefix:name=uri" for <paramref name="namespaceDeclaration"/>),
    /// or of one attribute of those nodes when <paramref name="nodeAndAttribute"/> is "node/attribute".
    /// </summary>
    public List<string> XmlNodes(string endpoint, string namespaceDeclaration, string nodeAndAttribute) {
        int equals = namespaceDeclaration.IndexOf('=');
        if (equals <= 0 || equals == namespaceDeclaration.Length - 1) throw new ArgumentException("XML web service: enter a namespace such as s=urn:sensors.");
        string prefix = namespaceDeclaration.Substring(0, equals);
        prefix = prefix.Substring(prefix.LastIndexOf(':') + 1);
        string uri = namespaceDeclaration.Substring(equals + 1).Trim();
        XmlDocument document = Load(endpoint);
        var namespaces = new XmlNamespaceManager(document.NameTable);
        namespaces.AddNamespace(prefix, uri);
        string xpath = nodeAndAttribute.EndsWith('/') ? nodeAndAttribute.Substring(0, nodeAndAttribute.Length - 1) : nodeAndAttribute;
        // Keep the legacy "node/attribute" form, while also accepting ordinary namespace-qualified XPath.
        int slash = xpath.LastIndexOf('/');
        if (!nodeAndAttribute.EndsWith('/') && slash > 1 && IsXmlName(xpath.Substring(slash + 1))) {
            string attribute = xpath.Substring(slash + 1);
            XmlNodeList parents = Select(document, xpath.Substring(0, slash), namespaces, allowEmpty: true);
            if (parents.Count > 0 && parents[0]!.Attributes?[attribute] != null) {
                var attributes = new List<string>();
                foreach (XmlNode parent in parents) attributes.Add(parent.Attributes?[attribute]?.InnerText ?? throw new InvalidOperationException("XML web service: the selected attribute was not found on every node."));
                return attributes;
            }
        }
        var values = new List<string>();
        foreach (XmlNode n in Select(document, xpath, namespaces)) values.Add(n.InnerText);
        return values;
    }

    static bool IsXmlName(string value) {
        try { XmlConvert.VerifyName(value); return true; }
        catch (XmlException) { return false; }
    }

    static XmlNodeList Select(XmlDocument document, string xpath, XmlNamespaceManager? namespaces = null, bool allowEmpty = false) {
        XmlNodeList nodes;
        try { nodes = (namespaces == null ? document.SelectNodes(xpath) : document.SelectNodes(xpath, namespaces))!; }
        catch (XPathException error) { throw new ArgumentException("XML web service: invalid XPath or an undeclared namespace prefix.", error); }
        if (!allowEmpty && nodes.Count == 0) throw new InvalidOperationException("XML web service: no nodes match the selected path.");
        return nodes;
    }

    XmlDocument Load(string endpoint) {
        var document = new XmlDocument { XmlResolver = null };
        string response = _http.Get(endpoint);
        try { document.LoadXml(response); }
        catch (XmlException error) { throw new InvalidOperationException("XML web service: the response is not valid XML.", error); }
        return document;
    }
}
