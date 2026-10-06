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

using jaNET.Commands;
using jaNET.Hosting;
using jaNET.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace jaNETFramework.Tests;

/// <summary>The public extension points: own commands, own functions, own speaker, and AddJanet.</summary>
public class ExtensionTests
{
    sealed class BlindsCommand : JudoCommand
    {
        public string Position { get; private set; } = "down";

        public BlindsCommand(params string[] names) {
            Names = names.Length > 0 ? names : new[] { "blinds" };
            On(i => { Position = "up"; return "Blinds are going up."; }, "up");
            On(i => { Position = "down"; return "Blinds are going down."; }, "down");
            On(i => i.Count > 3 ? "level " + i.Args[3] : "no level", "level");
            Otherwise(i => "unknown " + i.Sub);
        }

        public override IReadOnlyList<string> Names { get; }
    }

    sealed class RecordingSpeaker : ISpeaker
    {
        public bool Muted { get; set; }
        public List<string> Spoken { get; } = new List<string>();

        public void Say(string text) { lock (Spoken) Spoken.Add(text); }
    }

    static JanetHost Create(TempApp app, Action<JanetHostOptions> configure) {
        var options = new JanetHostOptions { RootDirectory = app.Directory, ExitProcess = () => { } };
        configure(options);
        return new JanetHost(options, TestParts.Offline());
    }

    /// <summary>Like <see cref="Create"/> but the speaker comes from the options, as for a real application.</summary>
    static JanetHost CreateWithOptionsSpeaker(TempApp app, ISpeaker speaker) {
        var parts = TestParts.Offline();
        var offline = new HostParts {
            Clock = parts.Clock, Http = parts.Http, Weather = parts.Weather, Console = parts.Console,
            Web = parts.Web, Socket = parts.Socket, Serial = parts.Serial, ExitDelay = parts.ExitDelay
        };
        var host = new JanetHost(new JanetHostOptions { RootDirectory = app.Directory, ExitProcess = () => { }, Speaker = speaker }, offline);
        host.Start();
        return host;
    }

    static JanetHost CreateStarted(TempApp app, Action<JanetHostOptions> configure) {
        JanetHost host = Create(app, configure);
        host.Start();
        return host;
    }

    [Fact]
    public void CustomCommandIsRunByJudo() {
        using var app = new TempApp();
        var blinds = new BlindsCommand();
        using JanetHost host = Create(app, o => o.Commands.Add(blinds));

        Assert.Equal("Blinds are going up.", host.Execute("judo blinds up"));
        Assert.Equal("up", blinds.Position);
        Assert.Equal("level 40", host.Execute("judo blinds level 40"));
        Assert.Equal("no level", host.Execute("judo blinds level"));
        Assert.Equal("unknown sideways", host.Execute("judo blinds sideways"));
    }

    [Fact]
    public void CustomCommandAppearsInTheSyntax() {
        using var app = new TempApp();
        using JanetHost host = Create(app, o => o.Commands.Add(new BlindsCommand("blinds", "shutters")));

        Assert.Contains("blinds", host.Syntax.Roots);
        Assert.Contains("shutters", host.Syntax.Roots);
        Assert.Equal(3, host.Syntax.SubCommands("blinds").Count);
        Assert.Contains("level", host.Syntax.SubCommands("shutters"));
    }

    [Fact]
    public void StartCreatesTheRootDirectoryWhenItDoesNotExist() {
        using var app = new TempApp();
        string root = System.IO.Path.Combine(app.Directory, "not", "there", "yet");
        var options = new JanetHostOptions { RootDirectory = root, ExitProcess = () => { } };
        using var host = new JanetHost(options, TestParts.Offline());

        host.Start();

        Assert.True(System.IO.File.Exists(System.IO.Path.Combine(root, "AppConfig.xml")));
    }

    [Fact]
    public void CommandNameOfABuiltInCommandIsRefused() {
        using var app = new TempApp();

        var ex = Assert.Throws<ArgumentException>(() => Create(app, o => o.Commands.Add(new BlindsCommand("schedule"))));
        Assert.Contains("schedule", ex.Message);
    }

    [Fact]
    public void TwoCustomCommandsWithTheSameNameAreRefused() {
        using var app = new TempApp();

        Assert.Throws<ArgumentException>(() => Create(app, o => {
            o.Commands.Add(new BlindsCommand());
            o.Commands.Add(new BlindsCommand());
        }));
    }

    [Fact]
    public void CustomFunctionIsExpandedInInstructions() {
        using var app = new TempApp();
        string garage = "closed";
        using JanetHost host = CreateStarted(app, o => o.Functions["garage"] = () => garage);

        host.Execute("judo inset add status <lock>The garage is %garage%</lock>");

        Assert.Equal("The garage is closed", host.Execute("status").Trim());
        garage = "open";
        Assert.Equal("The garage is open", host.Execute("status").Trim());
        Assert.Contains("%garage%", host.Syntax.Functions);
    }

    [Theory]
    [InlineData("two words")]
    [InlineData("with%percent")]
    [InlineData("")]
    public void CustomFunctionWithAnInvalidNameIsRefused(string name) {
        using var app = new TempApp();

        Assert.Throws<ArgumentException>(() => Create(app, o => o.Functions[name] = () => "x"));
    }

    [Fact]
    public void CustomFunctionCannotReplaceABuiltInFunction() {
        using var app = new TempApp();

        Assert.Throws<ArgumentException>(() => Create(app, o => o.Functions["time"] = () => "never"));
    }

    [Fact]
    public void CustomSpeakerIsUsedForSpeech() {
        using var app = new TempApp();
        var speaker = new RecordingSpeaker();
        using JanetHost host = CreateWithOptionsSpeaker(app, speaker);

        host.Execute("judo inset add hello <lock>Hello there</lock>");
        host.Execute("hello");

        Assert.True(SpinWait.SpinUntil(() => { lock (speaker.Spoken) return speaker.Spoken.Exists(s => s.Contains("Hello there")); }, 2000));
    }

    // ---- AddJanet ----

    sealed class FakeLifetime : IHostApplicationLifetime
    {
        public int Stops;
        readonly CancellationTokenSource _stopping = new();
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() { Interlocked.Increment(ref Stops); _stopping.Cancel(); }
    }

    static ServiceProvider BuildProvider(TempApp app, FakeLifetime lifetime, Action<JanetHostOptions> extra = null) {
        var services = new ServiceCollection();
        services.AddSingleton<IHostApplicationLifetime>(lifetime);
        services.AddJanet(o => {
            o.RootDirectory = app.Directory;
            extra?.Invoke(o);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddJanetRegistersOneHostAndAHostedService() {
        using var app = new TempApp();
        using ServiceProvider provider = BuildProvider(app, new FakeLifetime());

        JanetHost first = provider.GetRequiredService<JanetHost>();
        Assert.Same(first, provider.GetRequiredService<JanetHost>());
        Assert.Single(provider.GetServices<IHostedService>());
    }

    [Fact]
    public void AddJanetAppliesTheConfiguration() {
        using var app = new TempApp();
        using ServiceProvider provider = BuildProvider(app, new FakeLifetime(), o => {
            o.Commands.Add(new BlindsCommand());
            o.Functions["garage"] = () => "open";
        });
        JanetHost host = provider.GetRequiredService<JanetHost>();

        host.Config.EnsureExists();            // Start() would open the real web and socket ports
        Assert.Equal("Blinds are going up.", host.Execute("judo blinds up"));
        host.Execute("judo inset add status <lock>garage %garage%</lock>");
        Assert.Equal("garage open", host.Execute("status").Trim());
    }

    [Fact]
    public async Task ExitStopsTheApplicationInsteadOfTheProcess() {
        using var app = new TempApp();
        var lifetime = new FakeLifetime();
        using ServiceProvider provider = BuildProvider(app, lifetime);
        JanetHost host = provider.GetRequiredService<JanetHost>();

        host.Execute("%exit%");

        for (int i = 0; i < 100 && lifetime.Stops == 0; i++) await Task.Delay(100);
        Assert.Equal(1, lifetime.Stops);
    }

    [Fact]
    public async Task HostedServiceStopsTheServicesWithoutThrowing() {
        using var app = new TempApp();
        using ServiceProvider provider = BuildProvider(app, new FakeLifetime());
        IHostedService service = Assert.Single(provider.GetServices<IHostedService>());

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void AddJanetKeepsAnExitActionThatWasSetExplicitly() {
        using var app = new TempApp();
        int own = 0;
        var lifetime = new FakeLifetime();
        using ServiceProvider provider = BuildProvider(app, lifetime, o => o.ExitProcess = () => own++);
        JanetHost host = provider.GetRequiredService<JanetHost>();

        host.Execute("%exit%");
        for (int i = 0; i < 100 && own == 0; i++) Thread.Sleep(100);

        Assert.Equal(1, own);
        Assert.Equal(0, lifetime.Stops);
    }
}
