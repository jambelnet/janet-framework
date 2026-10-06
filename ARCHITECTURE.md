# Architecture

jaNET runs **instructions** (names of instruction sets, `%functions%`, `judo ...` commands) and offers four ways to send them:
the console, the web server (`?cmd=...`), the socket server and the serial port. Instructions can also be started by the
scheduler, by events (`oncheckin`, serial lines) and by mail. Everything goes through one place, the `InstructionRunner`.

```
 console   web server   socket server   serial port   scheduler   mail (POP3)
    \          |             |              |             |          /
     +---------+------ IInstructionExecutor (InstructionRunner) ----+
                                   |
        +--------------------------+--------------------------+
        |                          |                          |
  InstructionResolver        FunctionExpander              JudoDispatcher
  *pointers, evalBool,       %user% %time% ...          one JudoCommand per command group
  "./programs", "judo ..."                              (serial, inset, schedule, smtp, ...)
        |                          |                          |
        +------------------ services and configuration -------+
          AppConfigStore (AppConfig.xml)   SettingsStore (encrypted files)
          SystemSpeaker, UserPresence, MailService, WeatherSource, SmsClient, ...
```

`JanetHost` (`jaNETFramework/Hosting`) is the only place where the parts are put together. Nothing else creates its own
dependencies or reaches for a static: classes receive what they need through their constructor, and the few circular
relations (an event runs instructions, instructions start events) go through `Func<IInstructionExecutor>`.

## Folders of `jaNETFramework` (namespace = folder)

| Folder | What lives there |
| --- | --- |
| `Hosting` | `JanetHost` (composition root, public), `JudoSyntax` (data for completion, public) |
| `Scripting` | `InstructionRunner`, `InstructionResolver`, `FunctionExpander`, `ConditionEvaluator`, `ArgumentSplitter`, `UriCodec` |
| `Commands` | `JudoDispatcher`, `JudoCommand` and one class per command group, `HelpText` |
| `Configuration` | `AppConfigStore` (AppConfig.xml), `SettingsStore` (encrypted files), typed settings, the embedded default configuration |
| `Services` | speech, user presence, mail, SMS, dynamic DNS, ping, weather, update check, web service readers, time texts |
| `Servers` | `WebServer` (Kestrel: serves `www/` and `/api/instructions`, Basic auth, https), `MqttService` (MQTTnet client: keeps the connection, subscriptions run actions through a bounded queue), `MqttTopic` (filters, and `Safe`: the only way text from a message gets into an instruction), `CertificateProvider` (own .pfx or a self-signed one that is kept and renewed), `ServiceProblems`, `TcpSocketServer`, `SerialPortService`, `SchedulerService`, `ScheduleRules`, `MimeTypes` |
| `Infrastructure` | application paths, clock, log, process runner, host lifetime, JSON compatibility writer, `SettingsCipher` / `SettingsKey` (AES-GCM, key per installation) |

Only `JanetHost`, `JanetHostOptions` and `JudoSyntax` are public; the rest is `internal` (the tests see it through `InternalsVisibleTo`).

## Rules the code follows

* **An interface only where tests need a second implementation.** The seams are the places where jaNET touches the outside world:
  `IClock`, `ILog`, `IHttpFetcher`, `ISpeaker`, `IWeatherSource`, `IConsoleControl`, `IServer`, `ISerialService`, `ISettingsStore`, plus
  `IInstructionExecutor` (which also breaks the circle between events and instructions). Tests replace them through `HostParts`, so the whole
  stack runs without network, sound, sockets or hardware. Everything else (`AppConfigStore`, `ProcessRunner`, `SchedulerService`, the commands)
  has one implementation and is used directly: no interface, no indirection.
* **Names follow the product:** instruction set, launcher (`*name`), event, evaluator, judo command and built-in *function* (`%user%`) are the
  terms of the original documentation and the web UI, and the code uses the same words.
* **Data is data:** ids and values are never spliced into XPath; AppConfig.xml is read and written through `AppConfigStore` only.
* **Text compatibility is part of the contract:** judo command output, the JSON of `mode=json`, the encrypted settings files and the AppConfig.xml
  layout are what existing installations and the web UI depend on. `tools/compare.ps1` checks them against a recording of the original program.
* **Nullable reference types are on** everywhere; warnings are errors in Release.

## Adding a judo command

1. Derive from `JudoCommand`, give it `Names`, register sub commands in the constructor (`On(handler, "add", "new")`, optionally `Otherwise(handler)`).
2. Add it to the list in `JanetHost` (your own application passes it in `JanetHostOptions.Commands` instead). Help and completion pick it up (`JudoSyntax` is built from the registered handlers).
3. Add the text to `HelpText` and a test to `CommandTests`.

## Notices

`JanetHost.Notices` is computed on every call from the facts the parts already have: `IServer.Problem` and `ISerialService.Problem` (why the last
start failed, written by `ServiceProblems` in plain words), `AppConfigStore.Problem`, `ISettingsStore.Problems` and the security checks in
`JanetHost.SecurityNotices`. Nothing is stored, so a notice disappears when its cause does. The console (`NoticeTracker`) shows each one once,
`Reason.For` adds it to the answer of the start commands, and `JanetHostedService` writes them to `ILogger`. A new service that can fail to start
gets a `Problem` and a message in `ServiceProblems`.

## Version

One version for everything: `<Version>` in `Directory.Build.props`. `AppInfo.Version` reads it from the assembly at run time, so the banner, `%copyright%`, the
packages and `jaNETProgram --version` cannot disagree. `AppInfo.IsNewer` compares the version that the project site publishes with it as versions (not as digits).

## Tests

* `dotnet test` - unit tests per class, host level tests with fakes (`TestHost`), and integration tests with real loopback sockets for the web and socket server.
* `tools/compare.ps1` - replays about 120 console commands, web requests and socket requests against the built program and diffs the transcript with
  `tools/expected-transcript.txt`. Differences from the original program are listed in `tools/approved-differences.md`.
