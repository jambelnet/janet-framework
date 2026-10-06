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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace jaNET.Configuration;

/// <summary>
/// AppConfig.xml on disk. Reads are cached until the file changes; every change is written through a temporary
/// file (so readers never see a half written or missing file) after a copy to AppConfig.xml.bak.
/// Ids and values are compared as data, never spliced into XPath expressions.
/// </summary>
internal sealed class AppConfigStore
{
    const string Added = "Element added.";
    const string Removed = "Element removed.";

    readonly AppPaths _paths;
    readonly ILog _log;
    readonly object _gate = new();

    volatile string? _problem;
    XDocument? _cached;
    DateTime _cachedStamp;
    long _cachedLength;

    public AppConfigStore(AppPaths paths, ILog log) {
        _paths = paths;
        _log = log;
    }

    /// <summary>Why AppConfig.xml cannot be used right now (missing, damaged, locked); null while it can.</summary>
    public string? Problem => _problem;

    /// <summary>Writes the default AppConfig.xml if there is none yet.</summary>
    public void EnsureExists() {
        lock (_gate) {
            if (File.Exists(_paths.ConfigFile)) return;

            using Stream? resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("DefaultAppConfig.xml");
            if (resource == null) throw new InvalidOperationException("The default configuration is missing from the assembly.");

            using var reader = new StreamReader(resource, Encoding.UTF8);
            File.WriteAllText(_paths.ConfigFile, reader.ReadToEnd(), new UTF8Encoding(false));
            _cached = null;
        }
    }

    /* ------------------------------------------------------------------ reading */

    public CommSettings Comm {
        get {
            XElement? comm = Section("System", "Comm");
            string Get(string name) => comm?.Element(name)?.Value ?? string.Empty;

            return new CommSettings(Get("Trusted"), Get("localHost"), Get("localPort"), Get("Hostname"), Get("httpPort"),
                                    Get("Authentication"), Get("ComPort"), Get("BaudRate"), Get("HttpsPort"), Get("Certificate"));
        }
    }

    public MqttSettings Mqtt {
        get {
            XElement? mqtt = Section("System", "Mqtt");
            string Get(string name) => mqtt?.Element(name)?.Value ?? string.Empty;

            return new MqttSettings(Get("Broker"), Get("Port"), Get("Tls"), Get("ClientId"), Get("Enabled"));
        }
    }

    /// <summary>The topics jaNET listens to and what to run for each, in file order.</summary>
    public IReadOnlyList<MqttSubscription> MqttSubscriptions =>
        Section("System", "Mqtt", "Subscriptions")?.Elements("Subscription")
            .Select(e => new MqttSubscription((string?)e.Attribute("topic") ?? string.Empty, e.Value))
            .Where(s => s.Topic.Length > 0)
            .ToList() ?? new List<MqttSubscription>();

    public MailHeaderSettings MailHeaders {
        get {
            XElement? headers = Section("System", "Alerts", "MailHeaders");
            string Get(string name) => headers?.Element(name)?.Value ?? string.Empty;

            return new MailHeaderSettings(Get("MailFrom"), Get("MailTo"), Get("MailSubject"));
        }
    }

    /// <summary>URL of the weather service.</summary>
    public string WeatherUrl => Section("System", "Others")?.Element("Weather")?.Value ?? string.Empty;

    /// <summary>Marker for commands received by mail, e.g. "jaNET" for &lt;jaNET&gt;command&lt;/jaNET&gt;.</summary>
    public string MailKeyword => Section("System", "Comm")?.Element("MailKeyword")?.Value ?? string.Empty;

    /// <summary>The texts of all instruction sets with this exact id (normally one), in file order.</summary>
    public IReadOnlyList<string> InstructionActions(string id) =>
        Items("Instructions", "InstructionSet", id).Select(e => e.Value).ToList();

    /// <summary>The texts of all events with this id, in file order.</summary>
    public IReadOnlyList<string> EventActions(string id) =>
        Items("Events", "event", id).Select(e => e.Value).ToList();

    /// <summary>Ids of all instruction sets in file order, including the "*launcher" entries.</summary>
    public IReadOnlyList<string> InstructionSetIds() =>
        Items("Instructions", "InstructionSet", null).Select(e => (string?)e.Attribute("id") ?? string.Empty).ToList();

    /// <summary>All instruction sets in file order, without their actions (the dashboard needs these).</summary>
    public IReadOnlyList<InstructionSetInfo> InstructionSets() =>
        Items("Instructions", "InstructionSet", null).Select(e => new InstructionSetInfo(
            (string?)e.Attribute("id") ?? string.Empty,
            (string?)e.Attribute("categ"), (string?)e.Attribute("header"), (string?)e.Attribute("shortdescr"),
            (string?)e.Attribute("descr"), (string?)e.Attribute("img"), (string?)e.Attribute("ref"))).ToList();

    /// <summary>Every instruction set as an XML element, one string each.</summary>
    public IReadOnlyList<string> InstructionSetXml() =>
        Items("Instructions", "InstructionSet", null).Select(e => e.ToString(SaveOptions.DisableFormatting)).ToList();

    public IReadOnlyList<string> EventXml() =>
        Items("Events", "event", null).Select(e => e.ToString(SaveOptions.DisableFormatting)).ToList();

    IEnumerable<XElement> Items(string section, string element, string? id) {
        XElement? parent = Section(section);
        if (parent == null) return Enumerable.Empty<XElement>();

        return parent.Elements(element).Where(e => id == null || (string?)e.Attribute("id") == id);
    }

    XElement? Section(params string[] path) {
        XElement? current = Load()?.Root;
        foreach (string name in path) current = current?.Element(name);
        return current;
    }

    XDocument? Load() {
        lock (_gate) {
            try {
                var info = new FileInfo(_paths.ConfigFile);
                if (!info.Exists) {
                    _problem = $"AppConfig.xml was not found in {_paths.Root}. jaNET runs with empty settings; restart it to get the defaults.";
                    _log.Write($"obj [ AppConfig <Exception> ] Arguments: [ {_paths.ConfigFile} ] Exception Message: [ AppConfig.xml was not found. ]");
                    return null;
                }

                if (_cached != null && info.LastWriteTimeUtc == _cachedStamp && info.Length == _cachedLength)
                    return _cached;

                _cached = XDocument.Load(info.FullName);
                _cachedStamp = info.LastWriteTimeUtc;
                _cachedLength = info.Length;
                _problem = null;
                return _cached;
            }
            catch (Exception e) when (e is IOException || e is System.Xml.XmlException || e is UnauthorizedAccessException) {
                _cached = null;
                _problem = e is System.Xml.XmlException
                    ? $"AppConfig.xml is damaged ({e.Message}) and jaNET runs with empty settings until it is fixed. Repair the file, restore AppConfig.xml.bak, or delete it to get the defaults."
                    : $"AppConfig.xml cannot be read ({e.Message}). jaNET runs with empty settings until it can.";
                _log.Write($"obj [ AppConfig <Exception> ] Arguments: [ {_paths.ConfigFile} ] Malformed AppConfig.xml or not found. Exception Message: [ {e.Message} ]");
                return null;
            }
        }
    }

    /* ------------------------------------------------------------------ changing */

    public string AddInstructionSets(IEnumerable<InstructionSetEntry> entries) {
        return Modify(Added, doc => {
            XElement instructions = Ensure(doc, "Instructions");
            foreach (InstructionSetEntry entry in entries)
                instructions.Add(ToElement(entry));
        });
    }

    /// <summary>Adds the event and the "%~>id%" instruction set that triggers it.</summary>
    public string AddEvent(string id, string action) {
        return Modify(Added, doc => {
            Ensure(doc, "Events").Add(new XElement("event", new XAttribute("id", id), new XText(action.Trim())));
            Ensure(doc, "Instructions").Add(ToElement(new InstructionSetEntry(id, "%~>" + id + "%")));
        });
    }

    /// <summary>Removes the instruction set and its "*launcher".</summary>
    public string RemoveInstructionSet(string id) {
        return Modify(Removed, doc => RemoveItems(doc, "Instructions", "InstructionSet", id));
    }

    public string RemoveEvent(string id) {
        return Modify(Removed, doc => RemoveItems(doc, "Events", "event", id));
    }

    public string Update(CommUpdate update) {
        return Modify(Added, doc => {
            Set(doc, "Trusted", update.Trusted);
            Set(doc, "localHost", update.LocalHost);
            Set(doc, "localPort", update.LocalPort);
            Set(doc, "Hostname", update.Hostname);
            Set(doc, "httpPort", update.HttpPort);
            Set(doc, "Authentication", update.Authentication);
            Set(doc, "ComPort", update.ComPort);
            Set(doc, "BaudRate", update.BaudRate);
            SetOrClear(doc, "HttpsPort", update.HttpsPort);
            SetOrClear(doc, "Certificate", update.Certificate);

            static void SetOrClear(XDocument d, string name, string? value) {
                if (value == CommUpdate.Clear) Ensure(d, "System", "Comm", name).Value = string.Empty;
                else if (!string.IsNullOrWhiteSpace(value)) Ensure(d, "System", "Comm", name).Value = value;
            }

            static void Set(XDocument d, string name, string? value) {
                if (!string.IsNullOrWhiteSpace(value)) Ensure(d, "System", "Comm", name).Value = value;
            }
        });
    }

    public string UpdateMqtt(MqttUpdate update) {
        return Modify(Added, doc => {
            Set(doc, "Broker", update.Broker);
            Set(doc, "Port", update.Port);
            Set(doc, "Tls", update.Tls);
            Set(doc, "ClientId", update.ClientId);
            Set(doc, "Enabled", update.Enabled);

            static void Set(XDocument d, string name, string? value) {
                if (!string.IsNullOrWhiteSpace(value)) Ensure(d, "System", "Mqtt", name).Value = value;
            }
        });
    }

    /// <summary>Adds the subscription, or changes the action if the topic is already there.</summary>
    public string AddMqttSubscription(string topic, string action) {
        return Modify(Added, doc => {
            XElement subscriptions = Ensure(doc, "System", "Mqtt", "Subscriptions");
            XElement? existing = subscriptions.Elements("Subscription").FirstOrDefault(e => (string?)e.Attribute("topic") == topic);

            if (existing != null) existing.Value = action.Trim();
            else subscriptions.Add(new XElement("Subscription", new XAttribute("topic", topic), new XText(action.Trim())));
        });
    }

    public string RemoveMqttSubscription(string topic) {
        return Modify(Removed, doc => {
            doc.Root?.Element("System")?.Element("Mqtt")?.Element("Subscriptions")?.Elements("Subscription")
                .Where(e => (string?)e.Attribute("topic") == topic).ToList().ForEach(e => e.Remove());
        });
    }

    public string UpdateMailHeaders(string? from, string? to, string? subject) {
        return Modify(Added, doc => {
            Set(doc, "MailFrom", from);
            Set(doc, "MailTo", to);
            Set(doc, "MailSubject", subject);

            static void Set(XDocument d, string name, string? value) {
                if (!string.IsNullOrWhiteSpace(value)) Ensure(d, "System", "Alerts", "MailHeaders", name).Value = value;
            }
        });
    }

    public string UpdateWeatherUrl(string url) {
        return Modify(Added, doc => {
            if (!string.IsNullOrWhiteSpace(url)) Ensure(doc, "System", "Others", "Weather").Value = url;
        });
    }

    static XElement ToElement(InstructionSetEntry e) {
        // attribute order is part of the file format: id, img, descr, shortdescr, header, categ, ref
        var element = new XElement("InstructionSet", new XAttribute("id", e.Id));
        AddIfSet(element, "img", e.Thumbnail);
        AddIfSet(element, "descr", e.Description);
        AddIfSet(element, "shortdescr", e.ShortDescription);
        AddIfSet(element, "header", e.Header);
        AddIfSet(element, "categ", e.Category);
        AddIfSet(element, "ref", e.Reference);
        element.Add(new XText(e.Action.Trim()));
        return element;

        static void AddIfSet(XElement target, string name, string? value) {
            if (!string.IsNullOrWhiteSpace(value)) target.Add(new XAttribute(name, value));
        }
    }

    // The item itself and the "*launcher" it points to belong together.
    static void RemoveItems(XDocument doc, string section, string element, string id) {
        XElement? parent = doc.Root?.Element(section);
        if (parent == null) return;

        parent.Elements(element)
              .Where(e => (string?)e.Attribute("id") == id || (string?)e.Attribute("id") == "*" + id)
              .ToList()
              .ForEach(e => e.Remove());
    }

    // Finds or creates the element at the given path below the root.
    static XElement Ensure(XDocument doc, params string[] path) {
        XElement current = doc.Root ?? throw new InvalidOperationException("AppConfig.xml has no root element.");
        foreach (string name in path) {
            XElement? next = current.Element(name);
            if (next == null) {
                next = new XElement(name);
                current.Add(next);
            }
            current = next;
        }
        return current;
    }

    string Modify(string successMessage, Action<XDocument> change) {
        lock (_gate) {
            string path = _paths.ConfigFile;
            string backup = path + ".bak";
            string temp = path + ".tmp";

            try {
                XDocument doc = XDocument.Load(path);
                File.Copy(path, backup, true);

                change(doc);

                doc.Save(temp);
                File.Move(temp, path, true);
                _cached = null;
                return successMessage;
            }
            catch (Exception e) {
                // leave the previous configuration in place
                try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
                try { if (File.Exists(backup) && !File.Exists(path)) File.Copy(backup, path); } catch (IOException) { }
                _cached = null;
                return e.Message + "\r\nPlease try again.";
            }
        }
    }
}
