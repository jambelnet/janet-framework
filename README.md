# jaNET Framework

## Introduction

A free and open source IoT framework that provides a set of built-in [functions](https://github.com/jambelnet/janet-framework/wiki/Built-in-functions), a native API ([judo API](https://github.com/jambelnet/janet-framework/wiki/judo-API)), and multiple providers, such as scheduler, evaluator, notification manager and others, to allow a 3rd party software (e.g. [Jubito](http://www.jubito.org), see details below) to exploit, in order to interact with multiple services, software applications and vendor hardware (especially open hardware, such as Arduino, Raspberry Pi, Banana Pi, etc). It is designed for interoperability, therefore, to be absolutely vendor-neutral as well as hardware/protocol-agnostic. It can operate on any device that is capable of running .NET (Linux, Windows, Mac, including single-board computers, such Raspberry Pi and Banana Pi).

## Usage

1. Clone the repository and build it with the [.NET SDK](https://dotnet.microsoft.com/download) (`dotnet build jaNETFramework.sln`).
   The '*www*' directory is copied next to the program automatically.
2. Run the application (`dotnet run --project jaNETProgram`, or *jaNETProgram.exe* from *jaNETProgram/bin/Debug/net10.0*) and access Jubito UI (*http://localhost:8080/www/*) [1] in your browser.
   To deploy, use `dotnet publish jaNETProgram -c Release`.

[*1*] Default built-in web server provided by the framework is listening to localhost on port 8080.

You can change defaults by corresponding UI menu (*Menu->Settings->Web Server*) or via judo API.

> judo server setup [host] [port] [authentication]\
i.e.
> judo server setup localhost 8080 none | basic

[judo API doc](https://github.com/jambelnet/janet-framework/wiki/judo-API)

## Console

<img width="995" height="525" alt="image" src="https://github.com/user-attachments/assets/5af842e6-0677-4aa5-b6f4-a34857c85b9a" />

At a terminal the console shows a banner, an overview of the running services, a coloured prompt with command
history (Up/Down), Tab completion for `judo` commands, `%functions%` and instruction sets, a grey hint taken from the history,
tables for `judo schedule`/`inset`/`event` listings and a spinner for slow commands. Ctrl+L clears the screen, Ctrl+D quits.

It is the same console on Windows, Linux and macOS. When input or output is redirected (pipes, scripts), `TERM=dumb`, or
`--plain` / `JANET_PLAIN=1` is given, the classic plain-text console is used, byte for byte as before.

Without a terminal (systemd, Docker, `nohup`) start it with `--headless`; it keeps serving until SIGTERM/Ctrl+C or `%exit%`.
`deploy/janet.service` is an example systemd unit. `jaNETProgram --help` lists the options.

## Structure

There's basically two components in the core system:

* Instruction Sets
* Events

How the code is organized (folders, the instruction runner, how to add a judo command, tests) is described in [ARCHITECTURE.md](ARCHITECTURE.md).

## Using jaNET in your own application

jaNET is also a library. Two NuGet packages (build them with `dotnet pack`, or add the projects to your solution):

| Package | For |
|---|---|
| `jaNETFramework` | `JanetHost`, the extension points (`JudoCommand`, custom `%functions%`, `ISpeaker`) and the Jubito web UI, which is copied next to your program (`<JanetCopyWebUi>false</JanetCopyWebUi>` turns that off) |
| `jaNETFramework.Hosting` | `services.AddJanet(...)` for the .NET generic host (Worker Service, ASP.NET Core) |

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddJanet(o => {
    o.Functions["garage"] = () => door.IsOpen ? "open" : "closed";     // %garage% in every instruction
    o.Commands.Add(new BlindsCommand());                               // judo blinds up|down
});
builder.Build().Run();
```

Without the generic host: `using var janet = new JanetHost(options); janet.Start(); janet.Execute("judo schedule ls");`.
Both package readmes ([core](jaNETFramework/PackageReadme.md), [hosting](jaNETFramework.Hosting/README.md)) show the details.
jaNET is GPL licensed, so applications that link it must be GPL compatible.

## When something does not work

The console says so, in words, instead of leaving you to read `log.txt`: a web, socket or serial service that could not start (port in use, no
rights, an address that makes no sense), a damaged `AppConfig.xml` or settings file, a web server that anybody on the network can use without a
password, and the default `admin` / `admin` login. At start-up they appear in a box (red for errors, yellow for warnings); with `--plain` and
`--headless` they are printed as `Error: ...` and `Warning: ...` lines. After a command that changes things the new ones appear too, and
`judo server start` (and `judo socket start`, `judo serial open`) answer with a `Reason:` line when the service stays off. The technical message
still goes to `log.txt`. A program that embeds jaNET reads `JanetHost.Notices`; with `AddJanet` they are written to the application's log.

Tab completion works like a shell: one match completes the word, several complete as far as they agree, and if they still differ the line is left
as you typed it and the choices are listed; Tab again steps through them (Shift+Tab backwards). A `judo` command that does not exist turns red
while you type it.

## Help

In the fancy console `judo help` is laid out as one table per chapter (what it is for, the command, the other verbs that do the same);
`--plain` and `--headless` print the text as it always was.

A forum wil be started at some point.\
Submit bugs or feature requests [here](https://github.com/jambelnet/janet-framework/issues) and turn yourself into a valuable project participant.

## Running it on Linux, macOS and Raspberry Pi

The easy way is a build that carries its own runtime, so nothing has to be installed. Take `janet-<version>-<system>.tar.gz` from the
[releases](https://github.com/jambelnet/janet-framework/releases), or make it yourself on any computer with the .NET SDK (`python tools/publish.py linux-arm64`, the files
end up in `artifacts/`). Which system is yours? `uname -m` says `x86_64` (linux-x64), `aarch64` (linux-arm64, a Raspberry Pi with the 64-bit system) or `armv7l` (linux-arm, the 32-bit system).

```
tar xzf janet-1.0.0-linux-arm64.tar.gz
cd janet-1.0.0-linux-arm64
./jaNETProgram                 (--headless for a service; the console is fancy when you are at a terminal)
```

Use a `.tar.gz`, not a zip: a zip made on Windows has lost the "executable" flag of `jaNETProgram` (then `chmod +x jaNETProgram` is needed). If it still does not start:

| What you see | What it is | What to do |
| --- | --- | --- |
| `You must install or update .NET to run this application` ... `Microsoft.AspNetCore.App` | You have a build that is not self-contained (`dotnet publish`, `dotnet build`) and only the .NET runtime | Install the ASP.NET Core runtime too (it contains the .NET runtime): `sudo apt install aspnetcore-runtime-10.0`, or use the self-contained build |
| `cannot execute binary file: Exec format error` | The build is for another processor | Take the build that matches `uname -m` (see above) |
| `Permission denied` | The executable flag is missing | `chmod +x jaNETProgram` |
| `Couldn't find a valid ICU package installed on the system` | A minimal system (container, Alpine) without ICU | `export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` or install `libicu` |
| `jaNET cannot start: ...` | jaNET cannot write its data folder | See *Where jaNET keeps its files*; start it as the user that owns the folder, or give it one with `--root DIR` |

`dotnet jaNETProgram.dll` works with a framework-dependent build everywhere, with the same two runtimes. For a service see `deploy/janet.service`.

## Requirements

* [.NET SDK 10.0](https://dotnet.microsoft.com/download) or later (Windows, Linux or macOS; ARM boards such as Raspberry Pi are supported by .NET).
  To only run a published build you need the **.NET runtime and the ASP.NET Core runtime** (the web server is Kestrel); the SDK has both.
* Optional: Visual Studio 2022+, VS Code or Rider

Run the tests with `dotnet test jaNETFramework.sln` (489 tests) and check the behavior against the original program with `tools\compare.ps1`.

## MQTT (ESP32, Tasmota, ESPHome, Zigbee2MQTT, Home Assistant, ...)

jaNET is an MQTT client: it listens to topics on a broker (Mosquitto, the one in Home Assistant, ...) and runs an instruction when a message arrives, and
instructions, events and schedules can publish messages.

```
judo mqtt set 192.168.1.5                     (port 1883; add "tls" for 8883, or a port, or a client id)
judo mqtt login jaNET my-password             (kept encrypted in .mqttsettings)
judo mqtt subscribe home/livingroom/button <lock>{ evalBool("%mqttpayload%" == "pressed"); lighton; lightoff; }</lock>
judo mqtt subscribe home/garage/door <lock>door_%mqttpayload%</lock>     (runs the instruction set door_open or door_closed)
judo mqtt start                               (and again after every restart; "judo mqtt stop" turns it off)
judo mqtt publish home/livingroom/light/set ON            (also `judo mqtt publish topic message retain`)
```

* In an action `%mqtttopic%` is the topic of the message, `%mqttpayload%` its text and `%mqttnumber%` the first number in it (`21.5` out of
  `temperature 21.5 C`). **These values are made safe:** only letters, digits and `. , : _ / + @ # = -` are kept, everything else (spaces, quotes,
  braces, `;`, `%`, ...) becomes `_`, and they are cut at 200 characters. Otherwise anybody who can publish to the broker could run commands through jaNET.
  For a JSON payload use a sensor that publishes plain values, or a subscription on its sub-topics.
* `+` in a topic stands for one level, `#` for all the rest (`home/#`); several subscriptions can match one message and all run, one after the other.
* jaNET says `online` (retained) on `<client id>/status` when it connects and the broker says `offline` when it disappears; it reconnects by itself.
* The console tells why it cannot connect (nothing listens, wrong login, certificate not trusted) and warns about a login that travels in clear text.
  `insecure` (`judo mqtt set broker 8883 insecure`) uses tls without checking the broker's certificate, for a broker with its own certificate.
* An ESP32 (or anything else) can also still talk to jaNET directly: the socket server (`judo socket`) and the web API (`/?cmd=...`) need no broker.

## HTTPS

The web server speaks plain http on its port (default 8080). To add https:

```
judo server https on            (port 8443, or: judo server https on 9443)
```

jaNET makes a self-signed certificate once and keeps it in the data folder (`.janet.cert.pfx`); browsers warn about it the first time (the console
shows its fingerprint to compare). With https on, the plain port only serves this computer (so scripts on the same machine keep working) and sends
everybody else to https. To use your own certificate (e.g. from Let's Encrypt, exported as .pfx):

```
judo server https cert /path/to/my.pfx <password>      (the password is kept encrypted in .tlssettings)
judo server https cert default                         (back to the self-signed one)
judo server https off
```

HTTPS is off by default. Turning it off in the web UI from an HTTPS page returns you to the configured HTTP port. HTTP-to-HTTPS redirects are temporary and are not cached. If an older build's permanent redirect was cached, open `http://localhost:8080/www/?transport=http` on the server computer (replace 8080 if you changed the HTTP port), or use your server's address from another device. The server must be running. HTTP on the LAN is not encrypted; use trusted HTTPS for remote microphone access and protect any network-accessible server with authentication.

The console warns when the web login would travel in clear text over a network and when the web server has no password at all.
Listening on a network address (`judo server set 0.0.0.0 8080 basic`) needs no administrator rights.

## Backups

Keep `.janet.key` together with `AppConfig.xml` and the dotfiles (`.htaccess`, `.smtpsettings`, ...) when you back up or move an installation: the key is
what makes the settings files readable (details in [MODERNIZATION.md](MODERNIZATION.md)). Installations made by older versions are converted automatically on first start.

## Where jaNET keeps its files

The data folder holds `AppConfig.xml`, the encrypted settings files (`.htaccess`, `.smtpsettings`, `.weathersettings`, ...), the key
`.janet.key` and `log.txt`. It is, in this order:

1. the folder in the environment variable `JANET_HOME`, or given with `--root DIR`;
2. the folder that already has an `AppConfig.xml` where older versions kept it (the current directory on Windows, the program folder elsewhere), so
   an existing installation does not move;
3. otherwise a folder of its own: `%LOCALAPPDATA%\jaNET` on Windows, `/var/lib/janet` for root and `~/.local/share/janet` on Linux,
   `~/Library/Application Support/jaNET` on a Mac.

The console shows the folder at start-up (`Data folder: ...`). Back it up as a whole, together with `.janet.key`. The web UI is the `www` folder
next to the program; put a `www` folder in the data folder to use your own version of it. For a service see `deploy/janet.service`.

## Configuration

All system configuration are described in *System* tag within *AppConfig.xml*.\
They can manipulated by judo API, but I suggest you doing it, either by editing the XML or by the web UI (*Menu->Settings*).

**Weather:** select Open-Meteo (no API key) or keep your existing OpenWeatherMap endpoint in Settings > Weather > Setup.
For Luxembourg, Open-Meteo uses latitude `49.6116`, longitude `6.1319`, and location `Luxembourg, LU`. The command-line equivalent is:

```text
judo weather openmeteo 49.6116 6.1319 <lock>Luxembourg, LU</lock>
```

The existing weather functions and instruction sets work with both providers. Open-Meteo also supplies tomorrow's forecast.
Switching providers preserves the saved OpenWeatherMap key. OpenWeatherMap's existing `/data/2.5/weather` URL remains supported. No API key is shipped:
create a free key at <https://openweathermap.org/api> and save it with `judo weather key <your key>`. The key is kept encrypted in
`.weathersettings` and put into the request when it is made; the URL in `AppConfig.xml` keeps `APPID=YOUR_OPENWEATHERMAP_API_KEY`. A key that an
older version left in the URL is moved there when jaNET starts. OpenWeatherMap requires that key; Open-Meteo does not.

## Web terminal

The Terminal page keeps a scrolling transcript with a bottom command prompt. Enter runs a command; Up/Down recall this tab's command history and restore the unfinished draft. Ctrl+L or Clear terminal clears the transcript without deleting command history. The transcript and history are limited to 100 entries and are not saved between reloads. This is the jaNET command interface, not an operating-system shell.

Successful submissions clear the Terminal or Ask Jubito field. Failed requests keep the submitted text for retry, and text edited while a request runs is preserved. Generic acknowledgments include the command that ran. Command help remains searchable in the terminal transcript.

## Local speech

HTTPS is required for browser **microphone capture**, not for speech synthesis. Browsers restrict microphone access to secure contexts to protect recordings and permissions from network tampering. `http://localhost` is an exception because it stays on the same device; on a phone, localhost means the phone, not your jaNET PC. A LAN IP over HTTP is not a secure context, even on a Windows Private network or over Tailscale. Use HTTPS with a certificate trusted by each device for remote recording. Browser/server voice playback can work over HTTP, subject to microphone-independent playback permissions, mute settings and installed voices. See [microphone security requirements](https://developer.mozilla.org/en-US/docs/Web/API/MediaDevices/getUserMedia).

Open Home > Ask Jubito > Voice settings (the gear), or Settings > Voice > Setup.

* **Playback on this device:** uses installed local OS voices through the browser, with voice selection, playback speed and optional spoken replies. No extra server engine is needed. Chrome may need a moment to populate the voice list. Remote/cloud browser voices are excluded.
* **Recognition:** click Download for the small English (40 MB) Vosk model. It is stored in the jaNET data folder and loaded on first use, not at startup. Small models typically need around 300 MB RAM; this is suitable for a home-assistant computer, not an ESP32. See the [official model sizes and licenses](https://alphacephei.com/vosk/models).
* **Microphone:** open jaNET on `localhost`, or use HTTPS with a certificate trusted by that device. A self-signed certificate warning is not equivalent to trusted HTTPS. Allow the browser microphone prompt. The microphone inside the command field starts recording; it becomes a square to finish recording and transcribe (maximum 20 seconds). The cross beside the recording/transcription status cancels and discards the capture. Review the transcript before pressing Send. Audio is processed by your jaNET server, not a cloud recognition service. Recognition only produces text; it never executes a command automatically.
* **Read aloud:** the speaker appears beside a completed answer, not beside an empty command. Click it to read that answer; during playback it changes to a square with the tooltip "Stop reading". This only stops audio, never a running command. Voice settings control the voice, speed and automatic spoken replies.
* **Speech on the box:** install [eSpeak NG](https://github.com/espeak-ng/espeak-ng) for small, fast synthesis, or [Piper](https://github.com/OHF-Voice/piper1-gpl) for a more natural local voice. Select the executable path in Voice settings. Piper additionally needs a `.onnx` voice model with its matching `.onnx.json` beside it. Use Test voice to check the configuration.
* **Mute/unmute:** the Mute and Unmute instructions (or `%mute%` and `%unmute%`) control both server speech and browser playback. Mute stops active browser audio and disables Read aloud and Test voice until unmuted. Other open devices pick up changes within five seconds. Unmute restores playback permission; automatic spoken replies still follow the per-device Voice settings checkbox. The `{mute}` command prefix silences only that server command, not the global speech switch.

When the server is bound to an explicit LAN IP, it also listens on localhost using the same ports. This makes `http://localhost:<httpPort>/www/` available on the server PC without changing LAN access or certificate trust.

Speech engines and models are optional. Browser playback remains available when the server engine is off. Vosk native libraries are included for Windows x64, Linux x64 and macOS; Linux ARM/ARM64 deployments need a matching `libvosk.so` installed separately. The old `jspeech.exe`, Festival and `say` synthesis calls are no longer used. Voice settings are encrypted in `.speechsettings`; browser voice preferences stay on each device.

### Install Your Own Language

1. Open the [official Vosk model list](https://alphacephei.com/vosk/models), choose your language, check its license and download a compatible model ZIP. Prefer a small model when available; not every language has one.
2. Extract the ZIP on the **computer running jaNET**, not just the phone/browser. Keep the entire extracted model together. For example, `C:\Models\vosk-model-small-de-0.15\am\final.mdl` must exist.
3. In Voice settings, set **Vosk model folder** to the full extracted folder path, such as `C:\Models\vosk-model-small-de-0.15` or `/opt/models/vosk-model-small-de-0.15`. Select the folder containing `am`, `conf` and the other model files, not the ZIP or its parent folder. Click Save.
4. Record a short phrase in that language. Only one recognition model is active at a time; there is no automatic language detection. Changing this setting affects every device using that jaNET server.

Recognition languages and spoken voices are independent. To add a browser playback language, install its speech voice through your device's OS language/speech settings, then reload Jubito and choose the local voice in Voice settings. If the browser exposes no local voice for it, configure an eSpeak NG voice or a Piper model instead. The built-in recognition download list now contains English only; user-installed Vosk languages remain supported.

## Home And Commands

**Customize Home:** click the plus icon beside the greeting. Search for an instruction and check it to pin its card to Home; uncheck it to remove the pin without deleting the instruction. Cards run the same action and display the same reference value as Dashboard cards. Pins are saved in this browser's local storage, separately on each device; clearing site data clears them.

**Create a new Home item:** in Customize Home, select New instruction, enter a unique Name and its Action, then Save. The newly created instruction is automatically pinned. The optional Add to Dashboard section accepts Category, Header, descriptions, thumbnail URL and a live Reference. Category and Header are both required to also display it on the Dashboard tab. Existing Settings > Instructions > Add New Instruction Set still works; pin that instruction afterward through Customize Home. Arbitrary HTML widgets still require editing the web UI rather than the instruction picker.

**Natural command matching:** Ask Jubito accepts instruction IDs and friendly Headers, ignoring spaces, underscores, hyphens, case and punctuation. Thus `who am I?` matches an existing `whoami` instruction. Give custom instructions clear Headers such as `Kitchen light on`; the Header becomes an exact spoken/typed name. Built-in phrases include `who am I`, `what is my name`, `where am I`, `what is my status`, `what time is it`, `weather`, `check in` and `check out`. Existing `judo ...` commands and `%function%` expressions remain available.

Partial names produce suggested commands instead of running a guess. Click the intended suggestion to execute it. Duplicate normalized names also require a choice. This is lightweight local matching, not a general-purpose chatbot or a language-model agent; it does not infer arbitrary actions, translate phrases, or automatically execute microphone transcripts. For other recognition languages, use instruction IDs/Headers in that language or existing jaNET syntax.

Home uses a green status dot for present and red for absent. The larger calendar icon is centered beside the clock, with a matching sky background: daytime from 06:00 to 19:59 and night otherwise, using the jaNET server's clock rather than calculated sunrise/sunset. Temperature, humidity and pressure share a single group without separator lines. The weather tile selects an illustrated sky from the provider's current condition code (clear, clouds, overcast, rain, storm, snow, fog or clear night); night conditions are dimmed. This is condition-based artwork, not a live camera image. Unavailable weather does not display a misleading sky. Both OpenWeatherMap and Open-Meteo feed the same UI; switch providers in Settings > Weather without deleting your saved OpenWeatherMap key.

Assistant replies and response dialogs use readable message blocks with distinct completion, pending and error states. Terminal output remains plain text, and command help keeps its searchable layout. The blue speaker button is aligned on the right of the assistant reply, matching Send; it changes to Stop reading during playback. Styling does not change the command output or execution behavior.

## Modernization

See [MODERNIZATION.md](MODERNIZATION.md) for what changed in the move from .NET Framework/Mono to .NET 10, and how the unchanged behavior was verified.

## Hardware & Software Compatibility

It is fully tested and runnable on devices listed below:

* Any computer with Windows or Linux desktops
* Raspberry Pi 3 Model B
* Banana Pi
* Banana Pro
* BPi-R1

**Attached microcontrollers**:

* Arduino
* ESP32 and other Wi-Fi boards (through MQTT, the socket server or the web API)
* [RaZberry](http://razberry.z-wave.me/)

**Examples**:

* [Arduino](http://jubitoblog.blogspot.com/search/label/arduino)
* [RazBerry](http://jubitoblog.blogspot.com/search/label/razberry)
* [IP Camera](http://jubitoblog.blogspot.com/2013/02/dvr-system-using-ip-camera.html)

## Versions and releasing

Version numbers follow [Semantic Versioning](https://semver.org/); what changed is in [CHANGELOG.md](CHANGELOG.md). The current version is the one in
`Directory.Build.props`: it is the version of both NuGet packages, of the program and of what the banner and `%copyright%` show (`jaNETProgram --version`).

To release: change `<Version>` in `Directory.Build.props`, add the entry for it to `CHANGELOG.md` (a test fails until you do), commit, then

```
git tag v1.0.0
git push origin v1.0.0
```

The *Release* workflow checks that the tag is the version of the code, builds, tests, packs, and publishes a GitHub release with the notes from the changelog,
the packages and the source archive (a version with a dash, such as `1.0.0-rc.1`, is a pre-release). Add the repository secret `NUGET_API_KEY` to have the
packages pushed to nuget.org as well. The *CI* workflow builds and tests on Windows, Linux and macOS and replays the original program's behavior on every push.

## Contributing

Any kind of contribution is always very welcome and appreciated.\
Once you're familiar with the way jaNET works then you might want to contribute to the core system.

## Contact

You may reach out to me via [email](mailto:jambel@jubito.org) or [contact form](http://www.jubito.org/contact.html).

## License

jaNET Framework is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License, version 3 or (at your
option) any later version. See [LICENSE](LICENSE) for the full text and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the components it uses.
There is no warranty. Every source file carries a notice with the copyright of J@mBeL.net and the author, John Ambeliotis.

## Wiki
https://github.com/jambelnet/janet-framework/wiki

# About Jubito
[Jubito](http://www.jubito.org) is a complete DIY automation solution. An awarded IoT hub ([Technical Enabler: Application Enablement](http://www.postscapes.com/internet-of-things-award/2014/iot-application-enabler/) - [Honors & Awards](http://jubitoblog.blogspot.com/search/label/awards)) based on jaNET Framework.
To get a deeper understanding on how the web application layer sits on top, and implements the framework, [download](http://www.jubito.org/download.html) Jubito, open index.html and js/jubito.core.js files and read through the code. They are located on the /www/ root directory. A copy of it, can be found on this git as well.
Afterwards you'll be able to create [custom widgets and more](http://jubitoblog.blogspot.com/2016/08/consuming-restful-data.html).

Tech blog: http://jubitoblog.blogspot.com \
FAQ: http://jubito.org/faq.html

## Jubito Screenshot
<img width="345" height="730" alt="image" src="https://github.com/user-attachments/assets/ae8a0787-704e-44f7-80ce-6a8a16619b2a" />

