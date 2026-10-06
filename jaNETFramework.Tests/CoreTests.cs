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

using jaNET.Configuration;
using jaNET.Infrastructure;
using jaNET.Scripting;
using jaNET.Servers;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace jaNETFramework.Tests;

public class SplitArgumentsTests
{
    [Fact]
    public void SplitsOnWhitespace() {
        Assert.Equal(new[] { "judo", "inset", "ls" }, ArgumentSplitter.Split("judo inset ls"));
    }

    [Theory]
    [InlineData("judo mailheaders set `a@x.org` `Subj ect`", "Subj ect")]
    [InlineData("judo mailheaders set 'a@x.org' 'Subj ect'", "Subj ect")]
    [InlineData("judo mailheaders set \"a@x.org\" \"Subj ect\"", "Subj ect")]
    public void KeepsQuotedTextTogether(string input, string last) {
        var args = ArgumentSplitter.Split(input);

        Assert.Equal(5, args.Count);
        Assert.Equal("a@x.org", args[3]);
        Assert.Equal(last, args[4]);
    }

    [Fact]
    public void LockedActionIsOneArgumentWithoutTheLockTags() {
        var args = ArgumentSplitter.Split("judo inset add yes <lock>one; two  three</lock>");

        Assert.Equal(new[] { "judo", "inset", "add", "yes", "one; two  three" }, args);
    }
}

public class JsonCompatTests
{
    static string U(string hex) => "\\" + "u" + hex;

    [Fact]
    public void MatchesJavaScriptSerializerEscaping() {
        // Captured from System.Web.Script.Serialization.JavaScriptSerializer on .NET Framework 4.8
        string value = "a<b>c'd&e\"f\\g/h\t\n\r\b\f" + (char)1 + (char)0x7f + (char)0x85 + (char)0x2028 + (char)0x2029 + (char)0xe9 + (char)0x20ac + "+";
        string expected =
            "{\"k" + U("003c") + "\":{\"Key\":\"k" + U("003c") + "\",\"Value\":\"" +
            "a" + U("003c") + "b" + U("003e") + "c" + U("0027") + "d" + U("0026") + "e\\\"f\\\\g/h\\t\\n\\r\\b\\f" +
            U("0001") + (char)0x7f + U("0085") + U("2028") + U("2029") + (char)0xe9 + (char)0x20ac + "+\"}}";

        var results = new Dictionary<string, KeyValuePair<string, string>> {
            { "k<", new KeyValuePair<string, string>("k<", value) }
        };

        Assert.Equal(expected, JsonCompat.Serialize(results));
    }

    [Fact]
    public void EmptyResultIsAnEmptyObject() {
        Assert.Equal("{}", JsonCompat.Serialize(new Dictionary<string, KeyValuePair<string, string>>()));
    }

    [Fact]
    public void SerializesSeveralEntriesInOrder() {
        var results = new Dictionary<string, KeyValuePair<string, string>> {
            { "yes", new KeyValuePair<string, string>("yes", "Y") },
            { "no", new KeyValuePair<string, string>("no", "N") }
        };

        Assert.Equal("{\"yes\":{\"Key\":\"yes\",\"Value\":\"Y\"},\"no\":{\"Key\":\"no\",\"Value\":\"N\"}}", JsonCompat.Serialize(results));
    }

    [Theory]
    [InlineData("{\"a\":{\"b\":[10,20,{\"c\":\"deep\"}]}}", "a/b/1", "20")]
    [InlineData("{\"a\":{\"b\":[10,20,{\"c\":\"deep\"}]}}", "a/b/2/c", "deep")]
    [InlineData("{\"a\":true}", "a", "True")]
    [InlineData("{\"a\":null}", "a", "")]
    [InlineData("{\"a\":1.5}", "a", "1.5")]
    public void SelectsValuesByPath(string json, string path, string expected) {
        Assert.Equal(expected, JsonCompat.SelectValue(json, path));
    }

    [Fact]
    public void OutOfRangeIndexThrows() {
        Assert.Throws<ArgumentOutOfRangeException>(() => JsonCompat.SelectValue("{\"a\":[1]}", "a/5"));
    }
}

public class SettingsEncryptionTests
{
    // Values taken from the settings files written by the original .NET Framework build:
    // existing installations must keep being able to read their files.
    [Theory]
    [InlineData("admin", "u0VZL6/Tv0Yfoo3vGePFaQ==")]
    [InlineData("smtp.example.org", "KYjwSf+a6ocBx1GaeQvBCucLp/JG2l7yoy+2rWV6U4o=")]
    [InlineData("secret1", "6UwfbQX+ITfJs0/UqD7g0w==")]
    [InlineData("587", "jNDw5tQqe07TjhKAlmTeww==")]
    public void ProducesTheSameCipherTextAsTheOriginalImplementation(string plain, string cipher) {
        Assert.Equal(cipher, LegacySettingsCipher.Encrypt(plain));
        Assert.Equal(plain, LegacySettingsCipher.Decrypt(cipher));
    }

    [Fact]
    public void RoundTripsUnicode() {
        const string text = "pässwörd €";

        Assert.Equal(text, LegacySettingsCipher.Decrypt(LegacySettingsCipher.Encrypt(text)));
    }
}

public class EvaluatorTests
{
    [Theory]
    [InlineData("1 == 1", true)]
    [InlineData("2 < 1", false)]
    [InlineData("\"absent\" == \"absent\"", true)]
    [InlineData("DateTime.Now.Year > 2000", true)]
    public void EvaluatesConditions(string condition, bool expected) {
        Assert.Equal(expected, ConditionEvaluator.EvaluateBool(condition));
    }

    [Fact]
    public void InvalidConditionThrowsWithTheCompilerMessage() {
        var e = Assert.Throws<ConditionSyntaxException>(() => ConditionEvaluator.EvaluateBool("this is not C#"));

        Assert.StartsWith("Error Compiling Expression", e.Message);
    }
}

public class ScheduleParsingTests
{
    [Theory]
    [InlineData("25/12/2099", "25/12/2099")]
    [InlineData("05-01-2030", "5/1/2030")]
    [InlineData("05.01.2030", "5/1/2030")]
    [InlineData("daily", "daily")]
    public void NormalizesDates(string input, string expected) {
        Assert.Equal(expected, Schedule.NormalizeDate(input));
    }

    [Fact]
    public void ParsesAScheduleLine() {
        var s = Schedule.FromArguments(ArgumentSplitter.Split("s1 Monday 08:30 'yes' False"));

        Assert.Equal("s1", s.Name);
        Assert.Equal("monday", s.Date);
        Assert.Equal("08:30", s.Time);
        Assert.Equal("yes", s.Action);
        Assert.False(s.Enabled);
    }

    [Fact]
    public void SchedulesAreEnabledUnlessSaidOtherwise() {
        Assert.True(Schedule.FromArguments(ArgumentSplitter.Split("s1 daily 10:00 yes")).Enabled);
    }
}

public class WebServerTests
{
    [Fact]
    public void UriEncodingRoundTripsForUrls() {
        const string url = "http://localhost:8080/?cmd=yes";

        Assert.Equal(url, UriCodec.Decode(UriCodec.Encode(url)));
    }

    [Fact]
    public void UriEncodingOfSpacesIsLossyBecausePercentIsEncodedAfterIt() {
        // Existing behavior, kept as it is: stored instruction sets may depend on it.
        Assert.Equal("a%2520b", UriCodec.Encode("a b"));
    }

    [Fact]
    public void DecodesCommonEscapes() {
        Assert.Equal("?cmd=judo inset ls", UriCodec.Decode("%3Fcmd%3Djudo%20inset%20ls"));
    }

    static WebServer NewServer(TempApp app) =>
        new WebServer(app.NewConfig(), new SettingsStore(app.Paths, app.Log), app.Paths, () => throw new NotSupportedException(), app.Log);

    [Fact]
    public void FilesInsideTheApplicationDirectoryAreAllowed() {
        using var app = new TempApp();

        NewServer(app).EnsureServable(app.Paths.Root + "www" + Path.DirectorySeparatorChar + "index.html");
    }

    [Theory]
    [InlineData("..")]
    [InlineData("www/../../outside.txt")]
    public void PathsEscapingTheApplicationDirectoryAreRejected(string relative) {
        using var app = new TempApp();
        string path = app.Paths.Root + relative.Replace('/', Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "x";

        Assert.Throws<FileNotFoundException>(() => NewServer(app).EnsureServable(path));
    }
}

public class MimeTypeTests
{
    [Theory]
    [InlineData("C:\\app\\www\\js\\jubito.core.js", "text/javascript; charset=utf-8")]
    [InlineData("/app/www/css/app.css", "text/css; charset=utf-8")]
    [InlineData("/app/www/index.html", "text/html; charset=utf-8")]
    [InlineData("/app/www/manifest.webmanifest", "application/manifest+json; charset=utf-8")]
    [InlineData("/app/www/images/LOGO.PNG", "image/png")]
    public void KnownExtensions(string path, string expected) {
        Assert.Equal(expected, MimeTypes.ForFile(path));
    }

    [Fact]
    public void UnknownExtensionsStayUntyped() {
        Assert.Null(MimeTypes.ForFile("/app/www/data.bin"));
    }

    [Fact]
    public void ApiAnswersMatchTheirMode() {
        Assert.StartsWith("application/json", MimeTypes.ForCommand(ResponseFormat.Json));
        Assert.StartsWith("text/plain", MimeTypes.ForCommand(ResponseFormat.Text));
        Assert.StartsWith("text/html", MimeTypes.ForCommand(ResponseFormat.Html));
    }
}
