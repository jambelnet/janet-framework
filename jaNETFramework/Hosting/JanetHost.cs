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
using jaNET.Configuration;
using jaNET.Infrastructure;
using jaNET.Scripting;
using jaNET.Servers;
using jaNET.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace jaNET.Hosting;

/// <summary>Settings of a <see cref="JanetHost"/>. All of them are optional.</summary>
public sealed class JanetHostOptions
{
    /// <summary>
    /// The data folder: AppConfig.xml, the settings files, the key of the settings files and log.txt. It is created if it does not exist.
    /// Default: the environment variable <c>JANET_HOME</c>; else the folder that already has an AppConfig.xml where older versions kept it
    /// (the current directory on Windows, the program directory elsewhere); else a folder of its own: a <c>jaNET</c> folder in <c>%LOCALAPPDATA%</c>,
    /// <c>/var/lib/janet</c> (root) or <c>~/.local/share/janet</c>, <c>~/Library/Application Support/jaNET</c> on a Mac.
    /// <c>AddJanet</c> uses the application folder instead. The web UI is the <c>www</c> folder in here if there is one,
    /// otherwise the one next to the program.
    /// </summary>
    public string? RootDirectory { get; set; }

    /// <summary>What to do when "%exit%" is received. Default: end the process.</summary>
    public Action? ExitProcess { get; set; }

    /// <summary>Speaks the answers of instructions. Default: the speech of the system (jspeech.exe on Windows, festival or say elsewhere).</summary>
    public ISpeaker? Speaker { get; set; }

    /// <summary>
    /// Your own "judo" commands. Their names must be new: a name that a built-in command or another entry uses makes
    /// <see cref="JanetHost"/> throw an <see cref="ArgumentException"/>.
    /// </summary>
    public IList<JudoCommand> Commands { get; } = new List<JudoCommand>();

    /// <summary>
    /// Your own built-in functions: the name without percent signs (letters, digits, underscore) and what it returns each time it is used,
    /// e.g. <c>Functions["garage"] = () => door.IsOpen ? "open" : "closed"</c> makes "%garage%" available in every instruction.
    /// A name that already exists makes <see cref="JanetHost"/> throw an <see cref="ArgumentException"/>.
    /// </summary>
    public IDictionary<string, Func<string>> Functions { get; } = new Dictionary<string, Func<string>>(StringComparer.Ordinal);
}

/// <summary>
/// jaNET: the instruction runner and the services around it (web server, socket server, serial port, scheduler).
/// This is the one place where the parts are put together; everything else receives what it needs.
/// </summary>
public sealed class JanetHost : IDisposable
{
    readonly HostLifetime _lifetime;
    readonly AppPaths _paths;
    readonly AppConfigStore _config;
    readonly ISettingsStore _settings;
    readonly AppInfo _info;
    readonly FunctionExpander _functions;
    readonly InstructionRunner _runner;
    readonly JudoDispatcher _judo;
    readonly IServer _web;
    readonly IServer _socket;
    readonly ISerialService _serial;
    readonly IMqttService _mqtt;
    readonly SchedulerService _scheduler;
    readonly UserPresence _presence;
    bool _started;

    /// <summary>Creates the host; nothing runs until <see cref="Start"/>. Throws <see cref="ArgumentException"/> when a custom command or function clashes with an existing name.</summary>
    /// <param name="options">Folder, extensions and exit behaviour; <c>null</c> uses the defaults.</param>
    public JanetHost(JanetHostOptions? options = null) : this(options, null) { }

    internal JanetHost(JanetHostOptions? options, HostParts? parts) {
        options ??= new JanetHostOptions();
        parts ??= new HostParts();

        _paths = options.RootDirectory == null ? new AppPaths() : new AppPaths(options.RootDirectory);
        IClock clock = parts.Clock ?? new SystemClock();
        ILog log = new FileLog(_paths, clock);
        ProcessRunner processes = new ProcessRunner(log);
        _config = new AppConfigStore(_paths, log);
        _settings = parts.Settings ?? new SettingsStore(_paths, log);
        _lifetime = new HostLifetime(options.ExitProcess ?? (() => Environment.Exit(0)), parts.ExitDelay);

        IHttpFetcher http = parts.Http ?? new HttpFetcher();
        var internet = new InternetConnection(http);
        _info = new AppInfo(http, clock);
        ISpeaker speaker = parts.Speaker ?? options.Speaker ?? new SystemSpeaker(_paths, processes);

        // The pieces refer to each other (an event runs instructions, instructions start events, ...):
        // they get the executor, the dispatcher and so on lazily, once everything exists.
        InstructionRunner? runner = null;
        JudoDispatcher? judo = null;
        FunctionExpander? functions = null;
        InstructionResolver? resolver = null;
        ConditionEvaluator? evaluator = null;
        Func<IInstructionExecutor> executor = () => runner!;

        _presence = new UserPresence(_config, executor);
        var mail = new MailService(_settings, _config, internet, executor);
        var notifier = new MailNotifier(mail, _config, _settings, _presence, internet, log);
        IWeatherSource weather = parts.Weather ?? new OpenWeatherSource(_config, http, clock, _settings);
        var dynDns = new DynDnsClient(http, log);
        var time = new TimeText(clock);

        functions = new FunctionExpander(_config, executor, _lifetime, parts.Console ?? new ConsoleControl(), speaker, _presence, time,
                                   new Uptime(clock), _info, _paths, internet, mail, weather, dynDns, Environment.UserName,
                                   WithMqttFunctions(options.Functions));
        resolver = new InstructionResolver(functions, _config, () => evaluator!, () => judo!, processes);
        evaluator = new ConditionEvaluator(() => resolver, executor);
        runner = new InstructionRunner(_config, resolver, () => judo!, speaker, notifier, log);

        _web = parts.Web ?? new WebServer(_config, _settings, _paths, executor, log);
        _socket = parts.Socket ?? new TcpSocketServer(_config, executor, log);
        _serial = parts.Serial ?? new SerialPortService(_config, executor, log);
        _mqtt = parts.Mqtt ?? new MqttService(_config, _settings, executor, log);
        _scheduler = new SchedulerService(_settings, clock, _config, speaker, executor, log);

        judo = new JudoDispatcher(new JudoCommand[] {
            new SleepCommand(),
            new SerialCommand(_serial, _config),
            new InstructionSetCommand(_config),
            new EventCommand(_config),
            new TrustedCommand(_config),
            new SocketCommand(_socket, _config),
            new ServerCommand(_web, _config, _settings, () => (_web as WebServer)?.Certificate),
            new ScheduleCommand(_scheduler),
            new SmtpCommand(_settings),
            new Pop3Command(_settings),
            new GmailCommand(_settings),
            new MailCommand(mail),
            new MailHeadersCommand(_config),
            new SmsCommand(_settings, new SmsClient(_settings)),
            new JsonCommand(_config, new RemoteData(http)),
            new XmlCommand(_config, new RemoteData(http)),
            new HttpCommand(http),
            new DynDnsCommand(_settings, dynDns),
            new WeatherCommand(_config, _settings),
            new MqttCommand(_mqtt, _config, _settings),
            new PingCommand(new Pinger(log)),
            new HelpCommand()
        }.Concat(options.Commands), () => functions);

        _functions = functions;
        _runner = runner;
        _judo = judo;

        _lifetime.StopRequested += StopServices;
        Syntax = new JudoSyntax(() => _judo.Roots, _judo.SubCommands, () => _functions.Names, _config.InstructionSetIds);
    }

    internal IInstructionExecutor Executor => _runner;

    internal AppConfigStore Config => _config;

    /// <summary>Product version, e.g. 1.0.0 or 1.0.0-rc.1.</summary>
    public string Version => AppInfo.Version;

    /// <summary>The folder with AppConfig.xml, the settings files, the key and log.txt (see <see cref="JanetHostOptions.RootDirectory"/>).</summary>
    public string DataDirectory => _paths.Root;

    /// <summary>The user jaNET greets (the login name).</summary>
    public string UserName => Environment.UserName;

    /// <summary>False after "%exit%" or <see cref="Stop"/>.</summary>
    public bool IsRunning => _lifetime.IsRunning;

    /// <summary>What a front end can offer for completion: commands, functions and instruction sets.</summary>
    public JudoSyntax Syntax { get; }

    /// <summary>
    /// What a person running jaNET should see right now: a service that could not start and why, an AppConfig.xml or settings file that
    /// cannot be used, and unsafe settings. Computed on every call, so a notice is gone as soon as its cause is. Read it after
    /// <see cref="Start"/> and again after commands; <see cref="HostNotice"/> explains how to show it.
    /// </summary>
    public IReadOnlyList<HostNotice> Notices {
        get {
            var notices = new List<HostNotice>();

            _ = _config.Comm;        // reads AppConfig.xml again if it changed, so that a damaged file shows up
            if (_config.Problem != null) notices.Add(new HostNotice("Configuration", NoticeLevel.Error, _config.Problem));
            foreach (string problem in _settings.Problems) notices.Add(new HostNotice("Settings", NoticeLevel.Error, problem));
            AddProblem(notices, "Web server", _web.Problem);
            AddProblem(notices, "Socket server", _socket.Problem);
            AddProblem(notices, "Serial port", _serial.Problem);
            AddProblem(notices, "MQTT", _mqtt.Problem);
            notices.AddRange(SecurityNotices());

            if (WeatherKey.IsPlaceholder(_config.WeatherUrl) && WeatherKey.Stored(_settings) == null)
                notices.Add(new HostNotice("Weather", NoticeLevel.Info,
                    "No API key: the weather functions stay empty. Get a free key at openweathermap.org/api and set it with: judo weather key <key>"));

            return notices;
        }
    }

    static void AddProblem(List<HostNotice> notices, string source, ServiceProblem? problem) {
        if (problem != null) notices.Add(new HostNotice(source, problem.Level, problem.Message));
    }

    // a web server that anybody on the network can reach must ask for a password, and the password must not be the one everybody knows
    IEnumerable<HostNotice> SecurityNotices() {
        if (!_web.IsRunning) yield break;

        CommSettings comm = _config.Comm;
        bool password = comm.Authentication.Equals("basic", StringComparison.OrdinalIgnoreCase);
        string host = comm.Hostname.Length > 0 ? comm.Hostname : "localhost";
        string port = comm.HttpPort.Length > 0 ? comm.HttpPort : "8080";

        bool https = comm.HttpsPort.Length > 0 && (_web as WebServer)?.Certificate != null;

        if (!password && !IsThisComputerOnly(host))
            yield return new HostNotice("Security", NoticeLevel.Warning,
                $"The web server ({host}:{port}) can be reached from the network and has no password: anyone who can reach it can run any command. " +
                $"Turn the password on: judo server set {host} {port} basic" + (https ? string.Empty : ", and https: judo server https on"));

        if (password && !https && !IsThisComputerOnly(host))
            yield return new HostNotice("Security", NoticeLevel.Warning,
                "The web login travels over the network in clear text (http). Turn https on: judo server https on");

        MqttSettings mqtt = _config.Mqtt;
        if (mqtt.IsEnabled && mqtt.Broker.Length > 0 && !IsThisComputerOnly(mqtt.Broker)) {
            if (!mqtt.UsesTls && _settings.LoadMqttLogin() != null)
                yield return new HostNotice("Security", NoticeLevel.Warning,
                    "The MQTT login travels over the network in clear text. Use tls if the broker has it: judo mqtt set " + mqtt.Broker + " 8883 tls");
            if (mqtt.IsInsecure)
                yield return new HostNotice("Security", NoticeLevel.Warning,
                    "The MQTT broker's certificate is not checked (insecure): anyone in between can listen. Use its real certificate: judo mqtt set " + mqtt.Broker + " 8883 tls");
        }

        if ((_web as WebServer)?.Certificate is { SelfSigned: true } own && comm.HttpsPort.Length > 0)
            yield return new HostNotice("Security", NoticeLevel.Info,
                $"https uses a certificate that jaNET made itself ({own.Fingerprint}): browsers warn once, compare the fingerprint. " +
                "Use your own with: judo server https cert <file.pfx> <password>");

        if (password && _settings.LoadWebLogin()?.Matches("admin", "admin") == true)
            yield return new HostNotice("Security", NoticeLevel.Warning,
                "The web login is still the default admin / admin. Change it: judo server login <user> <password>");
    }

    static bool IsThisComputerOnly(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.StartsWith("127.", StringComparison.Ordinal) ||
        host == "::1" || host == "[::1]";

    /// <summary>
    /// Creates the default AppConfig.xml and web login on the first run, then starts the schedules, the web server,
    /// the socket server and the serial port as configured and checks the user in.
    /// </summary>
    public void Start() {
        if (_started) return;
        _started = true;

        Directory.CreateDirectory(_paths.Root);  // an embedding application may name a folder that does not exist yet
        _config.EnsureExists();

        _settings.MigrateLegacyFiles();          // settings of older versions get the encryption with the key of this installation

        if (!_settings.Exists(SettingsFiles.WebLogin))
            _settings.Save(SettingsFiles.WebLogin, "admin\r\nadmin");

        MoveWeatherKeyOutOfTheUrl();

        _scheduler.Load();

        CommSettings comm = _config.Comm;
        if (!string.IsNullOrWhiteSpace(comm.Hostname)) _web.Start();
        if (!string.IsNullOrWhiteSpace(comm.LocalHost)) _socket.Start();
        if (!string.IsNullOrWhiteSpace(comm.ComPort)) _serial.Open(string.Empty);
        if (_config.Mqtt.IsEnabled) _mqtt.Start();

        _functions.Expand("%checkin%");
    }

    // the built-in functions of the mqtt client next to the ones the host's owner added; a name that is taken twice is refused like any other
    static Dictionary<string, Func<string>> WithMqttFunctions(IDictionary<string, Func<string>> custom) {
        var all = new Dictionary<string, Func<string>>(custom);
        var builtIn = new Dictionary<string, Func<string>> {
            ["mqtttopic"] = () => MqttMessage.Topic,
            ["mqttpayload"] = () => MqttMessage.Payload,
            ["mqttnumber"] = () => MqttMessage.Number
        };

        foreach ((string name, Func<string> get) in builtIn) {
            if (!all.TryAdd(name, get)) throw new ArgumentException($"A function named '{name}' already exists.");
        }
        return all;
    }

    // older versions kept the OpenWeatherMap key inside the URL in AppConfig.xml; it belongs with the other secrets
    void MoveWeatherKeyOutOfTheUrl() {
        if (!WeatherKey.TryTake(_config.WeatherUrl, out string key, out string cleaned)) return;

        if (WeatherKey.Stored(_settings) == null) _settings.Save(SettingsFiles.Weather, key);
        _config.UpdateWeatherUrl(cleaned);
    }

    /// <summary>Runs instructions or "judo" commands and returns the answer as text (what you would type at the console).</summary>
    public string Execute(string input) => _runner.Run(input);

    /// <summary>The same as "%exit%": stops the services and ends the process.</summary>
    public void Stop() => _lifetime.RequestStop();

    /// <summary>Stops the web server, socket server, serial port and scheduler (the process keeps running).</summary>
    public void Dispose() => StopServices();

    void StopServices() {
        _scheduler.Dispose();
        _serial.Close();
        _mqtt.Stop();
        _web.Stop();
        _socket.Stop();
    }

    /// <summary>Messages of the copyright banner, including a notice when a newer version exists (this may take a few seconds offline).</summary>
    public string Copyright => _info.Copyright;
}

/// <summary>Parts that tests replace to run without network, sound, serial port or sockets.</summary>
internal sealed class HostParts
{
    public IClock? Clock { get; init; }
    public ISettingsStore? Settings { get; init; }
    public IHttpFetcher? Http { get; init; }
    public ISpeaker? Speaker { get; init; }
    public IWeatherSource? Weather { get; init; }
    public IConsoleControl? Console { get; init; }
    public IServer? Web { get; init; }
    public IServer? Socket { get; init; }
    public ISerialService? Serial { get; init; }
    public IMqttService? Mqtt { get; init; }
    public TimeSpan? ExitDelay { get; init; }
}

/// <summary>What jaNET understands, for front ends that offer completion.</summary>
public sealed class JudoSyntax
{
    readonly Func<IReadOnlyList<string>> _roots;
    readonly Func<string, IReadOnlyList<string>> _subCommands;
    readonly Func<IReadOnlyList<string>> _functions;
    readonly Func<IReadOnlyList<string>> _instructionSets;

    internal JudoSyntax(
        Func<IReadOnlyList<string>> roots, Func<string, IReadOnlyList<string>> subCommands,
        Func<IReadOnlyList<string>> functions, Func<IReadOnlyList<string>> instructionSets) {
        _roots = roots;
        _subCommands = subCommands;
        _functions = functions;
        _instructionSets = instructionSets;
    }

    /// <summary>The commands after "judo": serial, inset, schedule, ...</summary>
    public IReadOnlyList<string> Roots => _roots();

    /// <summary>The sub commands of a root command, e.g. "schedule" gives add, ls, rm, ...</summary>
    public IReadOnlyList<string> SubCommands(string root) => _subCommands(root);

    /// <summary>The built-in %functions% including the percent signs.</summary>
    public IReadOnlyList<string> Functions => _functions();

    /// <summary>Instruction sets that can be called by name (without the "*launchers").</summary>
    public IReadOnlyList<string> InstructionSets =>
        _instructionSets().Where(id => id.Length > 0 && !id.StartsWith('*')).ToList();
}
