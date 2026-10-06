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

using jaNET.Hosting;
using jaNET.Infrastructure;
using jaNET.Scripting;
using jaNET.Servers;
using jaNET.Services;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace jaNETFramework.Tests;

internal sealed class FakeServer : IServer
{
    public bool IsRunning { get; private set; }
    public int Starts { get; private set; }
    public int Stops { get; private set; }

    /// <summary>When set, Start fails with this problem instead of running.</summary>
    public ServiceProblem FailWith { get; set; }

    public ServiceProblem Problem { get; private set; }

    public void Start() {
        if (FailWith != null) { Problem = FailWith; return; }
        IsRunning = true;
        Problem = null;
        Starts++;
    }

    public void Stop() { IsRunning = false; Problem = null; Stops++; }
}

internal sealed class FakeSerial : ISerialService
{
    public bool IsOpen { get; private set; }
    public string OpenedWith { get; private set; }
    public List<string> Sent { get; } = new List<string>();
    public string Answer { get; set; } = "42";

    /// <summary>When set, Open fails with this problem instead of opening.</summary>
    public ServiceProblem FailWith { get; set; }

    public ServiceProblem Problem { get; private set; }

    public void Open(string portName) {
        if (FailWith != null) { Problem = FailWith; return; }
        IsOpen = true;
        Problem = null;
        OpenedWith = portName;
    }

    public void Close() { IsOpen = false; Problem = null; }

    public string Write(string message, SerialMessageKind kind, int timeoutMs = 1000) {
        Sent.Add(kind + ":" + message + ":" + timeoutMs);
        return Answer;
    }
}

/// <summary>Serves canned answers for URLs; any other URL fails like a dead connection.</summary>
internal sealed class FakeHttp : IHttpFetcher
{
    public Dictionary<string, string> Pages { get; } = new Dictionary<string, string>();
    public List<string> Requests { get; } = new List<string>();
    public bool Reachable { get; set; }

    public string Get(string url) {
        Requests.Add(url);
        return Pages.TryGetValue(url, out string body) ? body : throw new HttpRequestException("no route to " + url);
    }

    public string Get(string url, int timeoutMs) => Get(url);

    public Task<string> GetAsync(string url) => Task.FromResult(Get(url));

    public bool IsReachable(string url) => Reachable;
}

internal sealed class FakeWeather : IWeatherSource
{
    public WeatherReport Report { get; set; } = new WeatherReport {
        TodayDay = "Thursday", TodayConditions = "Clouds", TodayLow = "11", TodayHigh = "19", CurrentTemp = "17.5",
        CurrentHumidity = "60", CurrentPressure = "1012", CurrentCity = "Athens", WeatherIcon = "http://icons/a$1b.png"
    };

    public WeatherReport Current() => Report;
}

internal sealed class FakeConsole : IConsoleControl
{
    public int Cleared { get; private set; }

    public void Clear() => Cleared++;
}

/// <summary>Records what it is asked to run.</summary>
internal sealed class RecordingExecutor : IInstructionExecutor
{
    public List<string> Calls { get; } = new List<string>();
    public Func<string, string> Answer { get; set; } = input => "ran " + input;

    public string Run(string input, ResponseFormat format = ResponseFormat.Text, bool silent = false) {
        lock (Calls) Calls.Add(input);
        return Answer(input);
    }
}

/// <summary>
/// A complete jaNET in a scratch directory with everything that touches the outside world replaced:
/// no network, no sound, no sockets, no serial port, a clock that stands still.
/// </summary>
internal sealed class TestHost : IDisposable
{
    readonly TempApp _app = new TempApp();

    public FakeClock Clock { get; } = new FakeClock();
    public SilentSpeaker Speaker { get; } = new SilentSpeaker();
    public FakeHttp Http { get; } = new FakeHttp();
    public FakeWeather Weather { get; } = new FakeWeather();
    public FakeConsole Console { get; } = new FakeConsole();
    public FakeServer Web { get; } = new FakeServer();
    public FakeServer Socket { get; } = new FakeServer();
    public FakeSerial Serial { get; } = new FakeSerial();
    public FakeMqtt Mqtt { get; } = new FakeMqtt();
    public int Exits { get; private set; }
    public JanetHost Host { get; }

    public string Directory => _app.Directory;

    /// <param name="start">Start the services (the fakes) right away.</param>
    /// <param name="weatherKey">Give the weather service an API key, so that the "no API key" notice does not turn up in every test.</param>
    public TestHost(bool start = true, bool weatherKey = true) {
        Host = new JanetHost(
            new JanetHostOptions { RootDirectory = _app.Directory, ExitProcess = () => Exits++ },
            new HostParts {
                Clock = Clock, Speaker = Speaker, Http = Http, Weather = Weather, Console = Console,
                Web = Web, Socket = Socket, Serial = Serial, Mqtt = Mqtt, ExitDelay = TimeSpan.FromMilliseconds(20)
            });
        if (weatherKey) Host.Execute("judo weather key test-key");
        if (start) Host.Start();
    }

    /// <summary>Lets the speech that the start-up event started finish, then forgets what was said.</summary>
    public void Settle() {
        System.Threading.Thread.Sleep(300);
        lock (Speaker.Spoken) Speaker.Spoken.Clear();
    }

    public string Run(string input) => Host.Execute(input);

    public string Run(string input, ResponseFormat format) => Host.Executor.Run(input, format);

    public void Dispose() {
        Host.Dispose();
        _app.Dispose();
    }
}

internal static class TestParts
{
    /// <summary>Everything that reaches outside the process replaced by fakes (for tests that create their own directory).</summary>
    public static HostParts Offline() => new HostParts {
        Clock = new FakeClock(), Speaker = new SilentSpeaker(), Http = new FakeHttp(), Weather = new FakeWeather(), Console = new FakeConsole(),
        Web = new FakeServer(), Socket = new FakeServer(), Serial = new FakeSerial(), Mqtt = new FakeMqtt(), ExitDelay = TimeSpan.FromMilliseconds(20)
    };
}
