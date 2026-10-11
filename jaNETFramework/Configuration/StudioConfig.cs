// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later.
// This file is part of jaNET Framework.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace jaNET.Configuration;

internal sealed record StudioEvent(string Id, string Action);
internal sealed record StudioSnapshot(string Revision, IReadOnlyList<InstructionSetEntry> Instructions, IReadOnlyList<StudioEvent> Events, IReadOnlyList<string> Functions);
internal sealed record StudioSave(string Id, string Revision, string[] Actions, bool Create = false, string? Header = null, string? Category = null);

internal sealed partial class AppConfigStore
{
    internal StudioSnapshot Studio(IReadOnlyList<string> functions) {
        lock (_gate) {
            XDocument doc = XDocument.Load(_paths.ConfigFile);
            var entries = doc.Root?.Element("Instructions")?.Elements("InstructionSet").Select(e => new InstructionSetEntry(
                (string?)e.Attribute("id") ?? "", e.Value, (string?)e.Attribute("img"), (string?)e.Attribute("descr"),
                (string?)e.Attribute("shortdescr"), (string?)e.Attribute("header"), (string?)e.Attribute("categ"), (string?)e.Attribute("ref"))).ToArray() ?? Array.Empty<InstructionSetEntry>();
            var events = doc.Root?.Element("Events")?.Elements("event").Select(e => new StudioEvent((string?)e.Attribute("id") ?? "", e.Value)).ToArray() ?? Array.Empty<StudioEvent>();
            return new StudioSnapshot(StudioRevision(doc), entries, events, functions);
        }
    }

    internal string SaveStudio(StudioSave save) {
        if (string.IsNullOrEmpty(save.Id) || string.IsNullOrEmpty(save.Revision) || save.Actions == null || save.Actions.Length is < 1 or > 128 ||
            save.Actions.Any(action => action == null || action.Length > 65536) || save.Actions.Sum(action => action.Length) > 262144)
            throw new ArgumentException("Enter an identifier and 1 to 128 actions (maximum 256 KB total).");
        if (save.Create && !Regex.IsMatch(save.Id, "^[A-Za-z0-9_-]+$")) throw new ArgumentException("Use letters, numbers, underscores or hyphens for a new identifier.");
        if ((save.Header?.Length ?? 0) > 1024 || (save.Category?.Length ?? 0) > 1024) throw new ArgumentException("Header and category must be 1024 characters or fewer.");
        return Modify("Instruction saved.", doc => {
            if (StudioRevision(doc) != save.Revision) throw new InvalidOperationException("Studio conflict: configuration changed. Reload before saving.");
            XElement parent = Ensure(doc, "Instructions");
            var originals = parent.Elements("InstructionSet").Where(e => (string?)e.Attribute("id") == save.Id).ToArray();
            if (save.Create) {
                if (parent.Elements("InstructionSet").Any(e => (string?)e.Attribute("id") == save.Id || (string?)e.Attribute("id") == "*" + save.Id) ||
                    doc.Root?.Element("Events")?.Elements("event").Any(e => (string?)e.Attribute("id") == save.Id) == true)
                    throw new InvalidOperationException("Studio conflict: that name already exists.");
                foreach (string action in save.Actions) parent.Add(ToElement(new InstructionSetEntry(save.Id, action, Header: save.Header, Category: save.Category)));
            }
            else {
                if (originals.Length == 0) throw new InvalidOperationException("Studio conflict: instruction no longer exists.");
                // Only resize an unambiguous legacy self-pointer pair; arbitrary scripts remain untouched.
                var callable = save.Id.StartsWith('*') ? parent.Elements("InstructionSet").Where(e => (string?)e.Attribute("id") == save.Id.Substring(1)).ToArray() : Array.Empty<XElement>();
                bool paired = callable.Length == originals.Length && callable.Length > 0 && callable.All(e => e.Value == save.Id);
                ReplaceActions(originals, save.Actions);
                if (paired) ReplaceActions(callable, Enumerable.Repeat(save.Id, save.Actions.Length).ToArray());
            }
        });
    }

    static void ReplaceActions(XElement[] originals, string[] actions) {
        XElement anchor = originals[originals.Length - 1];
        for (int index = 0; index < actions.Length; index++) {
            if (index < originals.Length) {
                if (originals[index].Value != actions[index]) originals[index].Value = actions[index];
            }
            else {
                var extra = new XElement(originals[originals.Length - 1]) { Value = actions[index] };
                anchor.AddAfterSelf(extra);
                anchor = extra;
            }
        }
        foreach (XElement surplus in originals.Skip(actions.Length)) surplus.Remove();
    }

    static string StudioRevision(XDocument doc) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting))));
}
