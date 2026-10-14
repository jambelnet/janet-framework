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

## Build Scripts

Install the [.NET SDK 10 or later](https://dotnet.microsoft.com/download) and clone this repository. Both scripts restore dependencies and build **Release** by default, including the web UI. Run them from the repository folder; they also work when called by absolute path from another folder. They do not start the server or change saved settings.

**Windows (PowerShell):**

```powershell
.\build.ps1
.\build.ps1 -Test
.\build.ps1 -Test -Publish
.\build.ps1 -Publish -Runtime linux-arm64
.\build.ps1 -Configuration Debug
```

If PowerShell blocks this downloaded script, review it, then run `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` with any desired options. This applies only to that process; do not change the machine-wide execution policy.

**Linux (Bash; also supports macOS):**

```bash
bash ./build.sh
bash ./build.sh --test
bash ./build.sh --test --publish
bash ./build.sh --publish --runtime linux-arm64
bash ./build.sh --configuration Debug
```

`-Test` / `--test` runs the .NET suite and the web JavaScript tests and requires **Node.js 18 or later**. `-Publish` / `--publish` uses **dotnet publish directly**, without Python or another build dependency, and writes a deployable folder into `artifacts/publish/<runtime>/`. The default publish target matches your platform/processor; use the runtime option to cross-publish for another device. Publishing always uses Release; do not combine it with Debug.

Published folders include the .NET and ASP.NET Core runtimes by default, along with the web UI, README and license notices. Copy the whole folder to the target device, then run `jaNETProgram.exe` on Windows or `./jaNETProgram` on Linux/macOS. If you built a Linux executable on Windows, run `chmod +x jaNETProgram` on Linux after copying it. Add `-FrameworkDependent` / `--framework-dependent` when publishing for a smaller folder that requires both runtimes installed on the target; its name ends in `-framework-dependent`. Supported targets are `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `linux-arm`, `osx-x64` and `osx-arm64`. Linux ARM voice recognition still needs a matching native Vosk library as described under Local speech.

To run the local Release build on either system:

```text
dotnet run --project jaNETProgram -c Release --no-build
```

Open `http://localhost:8080/www/` (or your previously configured address/port). Local builds are in `jaNETProgram/bin/Release/net10.0/`; run `dotnet jaNETProgram.dll` from that folder on a machine with the runtimes installed. Use `-Help` / `--help` to list build-script options. For Linux services, see `deploy/janet.service`; building does not install or enable a service.

To check changes to the build scripts themselves, run `node --test tools/test-build-scripts.mjs`. It uses isolated mock tools to verify command ordering, path quoting, architecture selection and failure propagation; each shell is checked when available on the test machine. A failed test command stops the build script and prevents publishing.

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
[releases](https://github.com/jambelnet/janet-framework/releases), or publish a folder yourself with the .NET SDK (`bash ./build.sh --publish --runtime linux-arm64`; on Windows, `.\build.ps1 -Publish -Runtime linux-arm64`). The files end up in `artifacts/publish/linux-arm64/`; copy the whole folder to your device. No Python is required. Which system is yours? `uname -m` says `x86_64` (linux-x64), `aarch64` (linux-arm64, a Raspberry Pi with the 64-bit system) or `armv7l` (linux-arm, the 32-bit system).

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

Run the .NET regression tests with `dotnet test jaNETFramework.sln` and check the behavior against the original program with `tools\compare.ps1`. The replay uses an isolated data folder and private HTTP/TCP ports. After building, `powershell -NoProfile -ExecutionPolicy Bypass -File tools\compare.ps1 -Configuration Release -NoBuild -Out TestResults\replay.txt` reuses that build and preserves the transcript. Deliberate behavior changes are reviewed in `tools\approved-differences.md`; do not overwrite the expected transcript with unreviewed output.

## Automatic Email Alerts

Automatic instruction-output alerts require SMTP settings, an Internet connection,
and an **absent** user (`%checkout%`). Normal startup checks the user in; no automatic
alerts are sent while present (`%checkin%`), including check-in/check-out greetings.
Studio editing and dependency previews do not execute instructions. Studio Run,
widget refreshes and `{mute}` / `{widget}` commands suppress automatic alerts,
including those from nested events. Explicit event speech retains its own mute/unmute behavior.
An ordinary instruction or event run while absent can still produce an alert.

The connection check and notification wait are limited to ten seconds. If you check
in or the connection check times out, no new SMTP send is started. An SMTP send
already in progress may still finish. Explicit `judo mail send` commands are not
presence-gated and remain available whether you are present or absent.

## Serial (Arduino and ESP32 over USB)

The board must run firmware that understands your commands and sends newline-terminated
text replies. Connecting a board alone does not install firmware or discover its commands.
Choose its actual COM/device port and match the baud rate used by `Serial.begin(...)`:

```text
judo serial close
judo serial set COM3 9600             (Windows; replace COM3 with your board's port)
judo serial open
judo serial send ping 2000            (requires firmware that replies to ping)
```

On Linux, use the board's device path, such as `/dev/ttyACM0`, `/dev/ttyUSB0`, or a
stable `/dev/serial/by-id/...` path. Your user needs access to that device (often the
`dialout` group). Close the Arduino Serial Monitor or other software using the port.
Some ESP32 boards need **USB CDC On Boot** enabled for their native USB connector;
check the board's [Espressif USB instructions](https://docs.espressif.com/projects/arduino-esp32/en/latest/tutorials/cdc_dfu_flash.html).
Changes to serial settings take effect after closing and reopening the connection.

To trigger a jaNET event, register its name and have the firmware send that name:

```text
judo event add button_pressed <lock>whoami</lock>
```

```cpp
Serial.println("button_pressed");
```

Event names are case-sensitive; surrounding whitespace and CR/LF are ignored. An
unknown line never becomes a command to execute. Registered event lines are queued
separately from command replies, so an event handler can itself send a serial command
and wait for its reply. Firmware should send a separate reply if a command also emits
an event. Avoid using the same text as both an event name and a reply value.

`judo serial listen` and `judo serial monitor` both wait for one incoming line, including
an event line; they are not continuous consoles. Their optional timeout defaults to
1000 ms. `send` waits for the first non-event, nonblank line received during its request.
Requests are serialized, but the text protocol has no request IDs: unrelated non-event
telemetry or late replies can still be mistaken for a reply. Use named events for
unsolicited notifications, and keep firmware command replies ordered.

Incoming data is never flushed by a transfer. Events run in order on a separate worker;
the 256-entry queue logs overflow instead of blocking serial reads. A handler exceeding
10 seconds produces a warning; arbitrary instruction code cannot be forcibly cancelled,
so the next event waits for it to finish. Closing clears queued events and releases any
pending request. An already-running instruction may finish after closing.

If the board is unplugged or a serial read/write fails, the connection closes and reports
a reason. Plug it back in, update the port if it changed, and run `judo serial open` again.
jaNET does not automatically select or reconnect to an arbitrary device.

Before relying on a physical setup, verify a command/reply, a board-generated event,
an event handler that requests another reply, and unplug/reopen on the target OS.
Automated tests simulate these serial paths; physical Arduino/ESP32 behavior still needs
checking with your board, driver and firmware.

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

## IP Cameras

Open **Settings > IP Cameras > Cameras & Home widgets**, or **Customize Home > IP cameras**.
Add a name and the camera's HTTP/HTTPS JPEG/PNG snapshot or MJPEG URL, leave Format **Automatic**, and enter its username/password separately if required.
Select **Show on Home**, save, then use **Test connection**. The image endpoint is normally documented in the camera's manual or its local web interface;
the camera's ordinary login page is not a feed. A snapshot address might look like `http://192.168.1.50/snapshot.jpg`, but the path is model-specific.
There is no network scanning or automatic discovery, and a guessed URL is not installed for you.

To try a public camera without owning one, the National Park Service's
[Yellowstone North Entrance - Electric Peak webcam](https://www.nps.gov/media/webcam/view.htm?id=81B468AB-1DD8-B71B-0BE84D8E8E0F1112)
provides `https://www.nps.gov/webcams-yell/mammoth_electric.jpg`.
Use **Automatic**, a **60-second** snapshot interval, empty username/password fields, and **Show on Home**.
Save, then test the connection. This public JPEG feed was verified through the jaNET proxy and Home widget on 2026-10-14;
it is a periodically updated snapshot, not continuous live video. Its availability depends on NPS, and it is not installed automatically for every user.

For continuous live video, Heidelberg University publishes its
[Foucault pendulum webcam](https://kip.uni-heidelberg.de/oeffwiss/pendelcam/) at
`https://kip.uni-heidelberg.de/mjpg/video.mjpg`.
Choose **Automatic** or **MJPEG**, leave credentials empty, and enable **Show on Home**.
This public stream was verified through jaNET on 2026-10-14: multiple changing JPEG frames arrived over a single HTTP connection,
and the Home widget displayed **Live** without periodic snapshot requests. The snapshot interval does not control MJPEG playback.
The university controls its availability; this example is not an automatic default for every installation.

Up to eight cameras can be saved. Home widgets show uncropped frames, refresh snapshots every 2-60 seconds (default five),
and display MJPEG through the browser's native image decoder. Each widget has refresh, pause/resume, fullscreen and camera-settings controls.
Leaving Home or hiding the tab stops feeds and pending probes; returning resumes them except cameras paused explicitly in this browser session.
Editing, removing or hiding cameras refreshes the widgets. Stream connections are renewed periodically, and failures retry while the widget is visible.
This first integration is view-only: no recording, audio, PTZ/ONVIF control or native RTSP/H.264 transcoding.
RTSP-only cameras need a gateway on another network device exposing an HTTP MJPEG or snapshot endpoint; an RTSP URL cannot be entered as a browser feed.

Camera profiles and credentials are encrypted in `.ipcameras` with the installation's settings key. Blank password fields retain saved passwords;
changing host or username requires re-entering the password or explicitly clearing it. Home receives only camera IDs and display metadata, never the source URL or credentials.
Feeds go through jaNET endpoints using the installation's configured authentication, so an HTTP LAN camera can appear on an HTTPS Home page without mixed-content requests.
Normal HTTPS camera certificate validation remains enabled; use a trusted certificate or the camera's HTTP endpoint on a trusted LAN.
Basic/Digest HTTP authentication is supported; vendor login forms and proprietary cloud sessions are not.
Protect jaNET with HTTPS and authentication, use a camera read-only account, and do not expose an unauthenticated installation to the internet.
An HTTP camera connection is still unencrypted on the LAN. "Show on Home" controls display, not access permissions for trusted jaNET users.

The proxy accepts only stored camera IDs, blocks redirects and loopback/link-local/special-purpose destinations,
checks resolved addresses against the jaNET machine before connecting, and limits snapshots to 5 MB and concurrent connections to eight.
Camera errors do not execute instructions, speak responses or send email. See [.NET ConnectCallback](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler.connectcallback?view=net-10.0)
and [HTMLImageElement](https://developer.mozilla.org/en-US/docs/Web/API/HTMLImageElement) for the underlying transport and browser image APIs.
Adding camera support requires rebuilding and restarting jaNET, not just refreshing the web files.

## Google Calendar (read-only)

Jubito can read upcoming events from your primary calendar or another calendar shared with your Google account.
It does not create, change or delete Google events, and connecting does not install schedules or send mail.

### Connect Your Account

1. In [Google Cloud Console](https://console.cloud.google.com/), create or choose a project and enable the **Google Calendar API**.
2. Configure **Google Auth Platform** branding/audience. For a personal external app in Testing, add your Google account as a test user.
3. Create an OAuth client with application type **Web application**, not Desktop app. Register the exact authorized redirect URI:
   `http://localhost:8080/api/calendar/callback` for a local default installation. Substitute your actual local port.
   For remote hosting, use your HTTPS domain, such as `https://janet.example.org:8443/api/calendar/callback`, with a trusted certificate.
   Google rejects raw IP-address redirect hosts, including HTTPS LAN addresses; only loopback IPs are exempt.
   Plain HTTP on a LAN address is not supported. For setup on the jaNET computer, open `http://localhost:8080/www/` and use the localhost callback above.
   Open Jubito at the same origin as the registered URI when connecting. The public Branding homepage is separate from this callback.
4. Open **Settings > Google Calendar > Connection & upcoming events**. Enter the web client ID, client secret and the same redirect URI.
   Keep Calendar ID `primary` for your main calendar, or copy another calendar's ID from Google Calendar **Settings > Integrate calendar**.
   Set the time zone, for example `Europe/Paris`. This controls agenda times and the day boundaries of `today`.
5. Click **Connect to Google**, choose your account and approve read-only event access. After returning to Jubito, click **Upcoming events** to check the connection.

For a LAN installation, perform this sign-in in a browser **on the computer running jaNET**, using `http://localhost:8080/www/`.
`localhost` on a phone or another computer points to that device, not your jaNET server.
After connecting, return to your normal HTTPS LAN address, for example `https://192.168.178.23:8443/www/`.
The Calendar connection is stored on the server and works there too; keep the registered localhost callback unchanged.
You do not need to expose jaNET to the internet or disable HTTPS. For a later reconnect, open the localhost page again.

Google sign-in requests only `https://www.googleapis.com/auth/calendar.events.readonly`. Your Google password is never entered in Jubito.
The client secret, refresh token and access token are encrypted in `.googlecalendar` in the data folder with the installation's existing settings key.
Do not put OAuth JSON files or tokens in `www`, source control, instruction text or release archives. Blank client-secret fields retain the saved secret.
Changing the OAuth client, secret or redirect URI requires reconnecting; changing calendar ID or time zone retains the connection.
Protect remotely accessible Jubito with HTTPS and authentication; this connection, like the rest of jaNET's commands, is available to users allowed to access your installation.

For external OAuth apps left in **Testing**, Google normally expires refresh tokens after seven days for Calendar access. Reconnect when prompted,
or review Google's production/verification requirements for your intended distribution. jaNET refreshes normal short-lived access tokens automatically;
revoked access, expired refresh tokens, disabled APIs and network failures report an error, not a misleading empty agenda.
**Disconnect** clears local tokens and attempts to revoke access at Google; if Google cannot be reached, revoke the application manually in your Google Account.

### Home Widget

Home shows **Google Calendar** with up to ten events in the next seven days, including events already in progress.
The compact agenda pairs each event with a weekday, large day number and month, followed by its title, time and location, without horizontal divider lines.
Times use the saved Calendar time zone, not the browser's zone. All-day events show dates; multi-day ranges exclude Google's exclusive end date.
Use the refresh icon for an immediate update or the settings icon to open the existing connection dialog.
The widget checks for updates once a minute while Home is visible, and refreshes after saved Calendar settings change or disconnect.
Disconnected, empty and unavailable agendas have distinct states. Event content is displayed as plain text and is not stored in browser storage.
These background reads do not execute instructions, speak responses or send mail.

### Use Events In Instructions

```text
judo calendar upcoming           # next 7 days, at most 10 events
judo calendar upcoming 14 20     # next 14 days, at most 20 events
judo calendar today             # today's events in the saved time zone
judo calendar next              # next event, looking up to one year ahead
judo calendar json 7 10         # structured agenda, with original event fields
judo calendar status
judo help calendar
```

The `#` comments above are documentation, not part of the command. Days must be 1-365 and limits 1-100.
Upcoming includes events already in progress. Recurring events expand to individual occurrences; all-day dates stay calendar dates,
and the structured `end` date of an all-day event is exclusive, as in Google Calendar. Today also includes earlier events from today.
Human-readable titles neutralize jaNET control markers; JSON preserves the original text using standard escapes. Calendar data is not run as code.

In **Instruction Studio**, the **Google Calendar** palette offers Today's agenda, Next calendar event and Upcoming calendar events as Judo-command blocks.
For a check-in briefing, create an instruction named `my-agenda` with `judo calendar today` as its action, then add a call to `my-agenda`
to your existing `oncheckin` event. Keep the event's existing steps. You can also pin the instruction to Home or run `my-agenda` in Terminal.
No new actions are installed automatically.

API details: [Google Calendar events.list](https://developers.google.com/workspace/calendar/api/v3/reference/events/list),
[OAuth web-server setup](https://developers.google.com/identity/protocols/oauth2/web-server),
[token expiration](https://developers.google.com/identity/protocols/oauth2#expiration).

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
* **Speech on the box:** **Automatic** (the default) uses eSpeak NG when installed, otherwise Windows' built-in modern WinRT speech voices on Windows 10/11, without extra downloads or the old `jspeech.exe`/SAPI helper. To use Windows voices explicitly, select **Windows device voices**. **Voice / language** accepts `default`, a language such as `en` or `en-US`, or an installed Windows voice name/ID. Install additional voices in Windows language/speech settings. On Linux/macOS install [eSpeak NG](https://github.com/espeak-ng/espeak-ng) for small, fast synthesis, or choose [Piper](https://github.com/OHF-Voice/piper1-gpl) for a more natural local voice. Select the executable path in Voice settings. Piper additionally needs a `.onnx` voice model with its matching `.onnx.json` beside it. Linux box playback also needs `aplay` (Debian/Ubuntu: `sudo apt install espeak-ng alsa-utils`). **Server engine audio** + Test voice tests synthesis and playback in this browser; running a normal instruction tests the box's speaker. Explicit existing eSpeak/Piper/off settings are preserved: switch to Automatic to use Windows voices if your selected engine is missing.
* **Automatic instruction speech:** **Speak instruction and event responses** is enabled by default on each browser. An open, visible Jubito page receives all the instruction output jaNET sends to its speaker, including ordinary instructions, check-in/check-out, scheduled, serial and other events, even when the box engine is off or not installed. Ask Jubito no longer silences the box when executing an instruction. Browser playback uses your installed local voice; server-engine playback needs configured Windows voices, eSpeak NG or Piper. Click or tap the page once to allow automatic audio. Answers play in order, not over each other. Closed/hidden pages do not replay old events when reopened. Turn this setting off on extra devices to avoid several devices speaking together. **Speak assistant responses** (also on by default for new preferences) additionally reads direct function/command responses in this browser; saved preferences are preserved. An instruction is not read twice by the browser.
* **Mute/unmute:** the Mute and Unmute instructions (or `%mute%` and `%unmute%`) control both server speech and browser playback. Mute stops active browser audio and disables Read aloud and Test voice until unmuted. Other open devices pick up changes within five seconds. Unmute restores playback permission; automatic spoken replies still follow the per-device Voice settings checkbox. The `{mute}` command prefix silences only that server command, not the global speech switch.

When the server is bound to an explicit LAN IP, it also listens on localhost using the same ports. This makes `http://localhost:<httpPort>/www/` available on the server PC without changing LAN access or certificate trust.

Speech engines and models are optional. Browser playback remains available when the server engine is off. Vosk native libraries are included for Windows x64, Linux x64 and macOS; Linux ARM/ARM64 deployments need a matching `libvosk.so` installed separately. The old `jspeech.exe`, Festival and `say` synthesis calls are no longer used. Voice settings are encrypted in `.speechsettings`; browser voice preferences stay on each device.

Presence events still use existing `AppConfig.xml` instructions. For example:

```xml
<event id="oncheckin">%unmute%; salute; weathertoday</event>
<event id="oncheckout">judo sleep 5000; %unmute%; goodbye</event>
```

Events run only when presence changes. If already present, Check in does not greet again; Check out first. `%unmute%` must precede an instruction you want spoken: the old `goodbye; %unmute%` order cannot speak goodbye if already muted. The 5000 ms delay intentionally delays checkout. Widget refreshes and `{mute}`-prefixed instructions are not broadcast; an explicitly triggered event retains its own mute/unmute behavior. The authenticated speech-event endpoint keeps at most 32 messages for 60 seconds in memory, not on disk, and a newly opened page starts at the current event cursor.

### Install Your Own Language

1. Open the [official Vosk model list](https://alphacephei.com/vosk/models), choose your language, check its license and download a compatible model ZIP. Prefer a small model when available; not every language has one.
2. Extract the ZIP on the **computer running jaNET**, not just the phone/browser. Keep the entire extracted model together. For example, `C:\Models\vosk-model-small-de-0.15\am\final.mdl` must exist.
3. In Voice settings, set **Vosk model folder** to the full extracted folder path, such as `C:\Models\vosk-model-small-de-0.15` or `/opt/models/vosk-model-small-de-0.15`. Select the folder containing `am`, `conf` and the other model files, not the ZIP or its parent folder. Click Save.
4. Record a short phrase in that language. Only one recognition model is active at a time; there is no automatic language detection. Changing this setting affects every device using that jaNET server.

Recognition languages and spoken voices are independent. To add a browser playback language, install its speech voice through your device's OS language/speech settings, then reload Jubito and choose the local voice in Voice settings. If the browser exposes no local voice for it, configure an eSpeak NG voice or a Piper model instead. The built-in recognition download list now contains English only; user-installed Vosk languages remain supported.

## Home And Commands

**Customize Home:** click the plus icon beside the greeting. Search for an instruction and check it to pin its card to Home; uncheck it to remove the pin without deleting the instruction. Cards run the same action and display the same reference value as Dashboard cards. Pins are saved in this browser's local storage, separately on each device; clearing site data clears them.

**Create a new Home item:** in Customize Home, select New instruction, enter a unique Name and its Action, then Save. The newly created instruction is automatically pinned. The optional Add to Dashboard section accepts Category, Header, descriptions, thumbnail URL and a live Reference. Category and Header are both required to also display it on the Dashboard tab. Existing Settings > Instructions > Add New Instruction Set still works; pin that instruction afterward through Customize Home. Arbitrary HTML widgets still require editing the web UI rather than the instruction picker.

**Default icons:** Home shortcuts automatically use the new default set. Dashboard and pinned Home cards translate recognized local legacy PNG paths to matching defaults at display time. Leave Thumbnail URL blank for automatic selection by instruction ID/header, then category; unknown or ambiguous routines get a generic Instruction icon. Custom URLs and explicitly selected icons are preserved. This does not rewrite `AppConfig.xml`, run instructions or change saved actions. The old standalone Check in, Check out, Gmail and Settings PNGs are removed; branding and weather artwork remain.

The optional RaZberry device list uses Outlet, Sensor or Controller icons according to the reported command classes, without changing device commands or enabled states.

Settings > Instructions > Default Icons opens a searchable library of 77 consistent SVG tile icons for automation, messages/voice, networks/services, calendar/weather, system tools and common home-device routines, including IP cameras. In Add New Instruction Set > Add to Dashboard, click the image button beside Thumbnail URL to override the automatic choice. The picker fills a local path such as `icons/default/calendar.svg`. The gallery can copy paths, download individual SVGs or download the complete `www/icons/jubito-default-icons.zip` pack with its catalog and license. Existing instruction XML can use the same paths in its `img` attribute. Studio preserves those attributes when editing actions.

Icons work offline without a CDN or web font. Device symbols are for user-defined MQTT/serial/HTTP routines, not additional built-in device drivers. The generic Gmail symbol is not Google's logo. Lucide artwork is ISC-licensed; keep `www/icons/LICENSE-lucide.txt` when redistributing. Edit `www/icons/catalog.json` and run `node tools/build-icons.mjs` to regenerate SVGs, or `node tools/build-icons.mjs --check` to verify them. Generated icons are committed and included by the normal build/publish web-content glob; Node is not required at runtime.

On Windows, `./tools/build-icon-pack.ps1` regenerates both the SVGs and downloadable ZIP (use `-NodePath` if Node is not on PATH). On other systems, run the Node generator, then archive `default/`, `catalog.json`, `README.txt`, `LICENSE-lucide.txt` and `LICENSE-jaNET.txt` from `www/icons/`, keeping their relative paths.

**Remove an element:** open Settings > Instructions > Remove. Search the current list by name, header or category, select one item, and review the removal targets before choosing Remove. An instruction and its `*launcher` are grouped together and removed together. Include matching event handler is available only when a handler with that exact name exists; event-only entries remove only the handler. A failed removal keeps the dialog and selection open and shows the actual error. Removing an instruction does not rewrite other instructions, events or schedules that refer to it; update those references as needed.

**Natural command matching:** Ask Jubito accepts instruction IDs and friendly Headers, ignoring spaces, underscores, hyphens, case and punctuation. Thus `who am I?` matches an existing `whoami` instruction. Give custom instructions clear Headers such as `Kitchen light on`; the Header becomes an exact spoken/typed name. Built-in phrases include `who am I`, `what is my name`, `where am I`, `what is my status`, `what time is it`, `weather`, `check in` and `check out`. Existing `judo ...` commands and `%function%` expressions remain available.

Partial names produce suggested commands instead of running a guess. Click the intended suggestion to execute it. Duplicate normalized names also require a choice. This is lightweight local matching, not a general-purpose chatbot or a language-model agent; it does not infer arbitrary actions, translate phrases, or automatically execute microphone transcripts. For other recognition languages, use instruction IDs/Headers in that language or existing jaNET syntax.

Home uses a green status dot for present and red for absent. The larger calendar icon is centered beside the clock, with a matching sky background: daytime from 06:00 to 19:59 and night otherwise, using the jaNET server's clock rather than calculated sunrise/sunset. Temperature, humidity and pressure share a single group without separator lines. The weather tile selects an illustrated sky from the provider's current condition code (clear, clouds, overcast, rain, storm, snow, fog or clear night); night conditions are dimmed. This is condition-based artwork, not a live camera image. Unavailable weather does not display a misleading sky. Both OpenWeatherMap and Open-Meteo feed the same UI; switch providers in Settings > Weather without deleting your saved OpenWeatherMap key.

Assistant replies and response dialogs use readable message blocks with distinct completion, pending and error states. Terminal output remains plain text, and command help keeps its searchable layout. The blue speaker button is aligned on the right of the assistant reply, matching Send; it changes to Stop reading during playback. It appears only alongside a visible assistant answer. Background speech from terminal commands, Dashboard actions and events does not create a replay button on Home or replace the answer's replay text. Styling does not change the command output or execution behavior.

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

## External Program Launchers

Existing `./program` actions still run hidden and return **stdout unchanged** when
the program succeeds. They now have a **30-second execution timeout** instead of
waiting indefinitely. Stdout and stderr are drained concurrently; a failed start,
nonzero exit code or timeout returns an `Error:` response. Successful stderr
warnings do not replace stdout. Each output stream is limited to 1,048,576 characters;
larger output is drained but reported as an error rather than accumulated forever.

In **Add Launcher**, enter the existing `./program` action. **Execution mode** and
**Timeout (seconds)** then become available. Choose **Capture output** for scripts
returning data, or **Launch application** for desktop programs such as Notepad.
Ordinary jaNET instruction actions keep their existing behavior and do not show
process controls. The mode and timeout are stored in the instruction action, so
they also work from the console, other browsers, events and schedules.
Existing launchers are not silently converted to launch-only mode. To change one,
remove the old launcher with `judo inset remove Name` and recreate it with the desired
mode and the same name; adding another launcher with the same ID creates duplicate
instruction entries rather than updating the old one.

```text
./notepad.exe
./[launch]notepad.exe
./[capture:120]'/path/to/program' 'arguments'
./[launch]'C:\Program Files\Example\Example.exe' 'arguments'
```

The first line captures output with the default timeout; use the second line to
open Notepad visibly and return immediately with `Application started: notepad.exe`.
Launch-only mode never waits for completion or terminates the application later.
Capture deadlines can be set from 1 to 300 seconds; on timeout jaNET attempts to
terminate only that capture process and its descendants. A descendant that outlives
its parent may still keep a pipe open: the same deadline stops waiting for that pipe,
but jaNET cannot terminate a descendant after its parent has already exited.
Separate stdout/stderr capture is not supported in launch-only mode.

Paths and arguments use the existing single-quote or backtick syntax. Use absolute
paths for reliability; bare executable names depend on the server's environment.
Programs run **on the computer hosting jaNET**, never on the phone/browser device.
Desktop applications require an interactive logged-in desktop; a headless service
cannot make a window appear on a remote phone. A launch acknowledgment confirms
that the OS accepted the start request, not that the application initialized successfully.

Browser API requests also have a 315-second safeguard, including response-body reads.
A request timeout releases the prompt and retains its command, warns that the command
may still be running, and does **not** automatically retry. It does not cancel a
server-side instruction; multi-action instructions may legitimately exceed this limit.
For process-specific syntax use `judo help process`.

## Instruction Studio

Open **Instructions > Instruction Studio** in Jubito (or `/www/studio.html`). Choose **Instructions** or **Events** in the Routines library, then select a definition. The heading identifies the active routine; nothing opens or executes automatically. Use the plus button to create either type. In the Blocks palette, clicking an instruction, launcher or event name **opens its definition**; its separate plus icon **adds a call to the active routine**. Use **Add action** for a searchable chooser, or drag a palette row into the ordered action list. Select a step to edit its properties, and use the move, duplicate and remove controls. Undo and redo remain available until a save or selection change.

On desktop, Studio fits the viewport: the library, editor and Properties scroll independently when their content is longer than the available space. The editor toolbar and status remain visible. Smaller tablet layouts place Properties below the editor; phones retain normal page scrolling so controls are not clipped.

If Studio cannot load and reports that its API is unavailable, stop the running jaNET instance, rebuild the **same project and configuration** you launch, restart it, and reload the page. Studio requires the updated server's `/api/studio` endpoint; new web files served by an older binary or a static web server are not enough. When launching through Visual Studio, rebuild its selected Debug/Release configuration and ensure it opens the intended repository checkout.

Blocks cover text, text templates, registered `%functions%` (including extensions), other instructions, starred launcher references, events, delays, conditions, judo commands and programs. Existing templates and supported conditions open in their typed editors; unchanged conditions retain their exact original source. Complex scripts remain editable as raw source. Program properties expose **Launch application**, **Capture output**, and legacy capture, with separate program/script and arguments fields. Capture timeouts are 1-300 seconds; legacy capture uses the existing 30-second default. The editor generates the existing `./[launch]...` or `./[capture:seconds]...` syntax and retains unchanged old expressions exactly. Single quotes and backticks cannot be entered in the structured path/arguments fields because the existing resolver uses them as delimiters; unusual expressions retain raw-source editing. Condition branches take instruction identifiers separated by spaces; put multiword commands or functions in a referenced instruction. Expressions use jaNET's existing C# evaluator, not JavaScript.

Referenced instruction and event actions are available in collapsed previews beneath their block, including references entered as raw source. Both supported condition branches are listed, and starred references show the action at the corresponding position rather than the whole launcher. Pencil buttons open the referenced definition; unsaved changes still require confirmation before switching. Previews are not extra actions copied into the current routine. Missing targets are marked, and previews never execute functions, devices, programs or events.

Events use the same ordered Builder: add instruction calls, functions, delays, programs or other event calls; reorder, edit or remove steps, then save the event. Saving updates the first matching event definition, preserving its attributes, later duplicate definitions and unrelated instructions. An empty event can be saved to disable its actions. The event runner executes each identical step only once; the editor rejects duplicates instead of implying repeated execution. Put repeated steps, literal text, templates or conditions inside a referenced instruction. Positional launcher references belong inside instructions; legacy starred event steps are displayed as calls to their callable instruction and preserve their exact syntax. Semicolons and ampersands separate event steps even inside quoted text, so move expressions containing them into an instruction. Locked event expressions stay as one raw action; adding other steps to a locked event is rejected because the runner would bypass them. Saving uses the same revision check, atomic writer and backup as instruction saves. Event editing requires the updated server binary, not just the web files.

The **Source** view keeps each XML action separate. Existing complex expressions remain raw source and opening them does not rewrite them. Selecting a canonical legacy `name -> *name` pair opens its launcher; saving a changed action count resizes only that unambiguous callable pair. Arbitrary pointer structures are never automatically changed. Other attributes, metadata and unrelated configuration sections are retained. Saves use the existing atomic XML writer and `.bak` backup. If configuration changed elsewhere, saving is rejected: copy your unsaved source before reloading and reapply the changes to the current version.

**Dependency Map** opens in 2D, with an optional 3D view. Pan/zoom, fit, select nodes for details, or use the accessible dependency list. Simple `name` / `*name` aliases are collapsed: the connector names the source, and Properties retains its identifier and action position. Sentences containing functions are labeled **Text template**. Other references retain their positional targets. The map shows declared dependencies, not a runtime trace or execution timeline. Preview highlights nodes without executing anything. Runtime values, conditional results, dynamic command construction and external program internals cannot be predicted. Both condition branches may be shown; `%function%` and `*pointer` expansion can happen before branch selection. Missing targets, cycles, complex constructs and the map's size limit are flagged rather than guessed. The 2D map works without WebGL and remains available if 3D cannot load. The Jubito logo returns to Home; the back arrow returns to Instructions.

Use **Save** to persist changes, then the separate **Run** button and confirmation to execute on the jaNET host. There is no automatic execution on load, preview or save. Instruction-reference blocks use `judo inset run name`, while event steps retain their existing bare instruction identifiers. Running a saved event explicitly invokes `%~>name%`. Recursive calls are bounded at 32 levels. Existing pointer syntax and execution behavior are preserved. Event dependency maps are rooted at the active event, with current draft actions included.

Studio uses the server's configured authentication and HTTPS settings. `/api/studio` exposes instruction source to authorized same-origin clients and accepts revision-checked JSON saves; the original `/api/instructions` endpoint remains metadata-only. Protect your server with authentication and do not publish it to an untrusted network. Three.js and SortableJS are vendored locally and loaded only by Studio; the locally vendored Lucide library also supplies Home and icon-picker tool controls. No Node.js, Python, npm, CDN or internet connection is required to use it. Browsers without WebGL retain editing and a dependency-list fallback.

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
packages pushed to nuget.org as well. The *CI* workflow builds and tests on Windows, Linux and macOS, including browser JavaScript and build/publish script tests, and replays the original program's behavior. It runs on pull requests and pushes to `master`, `main`, `next`, `modern` and `modernize-*` branches. Release tags run the .NET and JavaScript tests before publishing.

Failed runs retain downloadable diagnostics under **Actions > run > Artifacts**: `test-results-<os>` contains the .NET `.trx` results and, when that step runs, `browser-tests.log`; `behavior-replay` contains the replay transcript. Release runs retain `release-test-results`. Open the failed job's red step for the immediate error. Browser/script test files run sequentially in CI to avoid starting many shell subprocesses at once. The .NET integration test collections also run sequentially so independent tests do not starve background workers; individual tests still exercise concurrent connections, queues and shutdown. Every test still runs.

### Release archives without Python

The release packager uses the same **.NET 10 SDK** as the application build. Neither Python nor Node.js is required to publish and archive binaries. Node.js is needed only for JavaScript tests (and for regenerating the separate website).

```powershell
# Windows: one target, selected targets, or all targets
.\tools\publish.ps1 -Runtime win-x64
.\tools\publish.ps1 -Runtime win-x64,linux-arm64
.\tools\publish.ps1
```

```bash
# Linux / macOS: selected targets, or all targets
bash ./tools/publish.sh linux-x64 linux-arm64
bash ./tools/publish.sh
```

These scripts call a shared .NET packaging tool, cross-publish in Release, and write versioned archives into `artifacts/`: `.zip` for Windows, `.tar.gz` for Linux/macOS. All seven targets from the build scripts are supported. Archives contain a top-level `janet-<version>-<runtime>/` folder with the complete app, web UI, README and license notices; Linux archives also include the systemd example. Unix executable permissions are set in the tar archives even when packaging on Windows. Fresh isolated publish folders prevent stale files or locally saved settings from being picked up.

Add `-FrameworkDependent` (PowerShell) or `--framework-dependent` (Bash) to omit bundled runtimes; the archive name and top-level folder then have a `-framework-dependent` suffix. Install the .NET and ASP.NET Core runtimes on that target. Ordinary `build.ps1 -Publish` / `build.sh --publish` still creates an unpacked folder; framework-dependent folders are `artifacts/publish/<runtime>-framework-dependent/`.

Run the full tests before preparing a release: `.\build.ps1 -Test` or `bash ./build.sh --test`. The archive scripts build and package but do not run tests or create Git tags/releases themselves. For packaging-wrapper regression tests, run `node --test tools/test-publish-scripts.mjs`.

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
<img alt="image" src="https://blogger.googleusercontent.com/img/b/R29vZ2xl/AVvXsEisERFydUs5XrJ8UCUEO1y4tGRResOVsN3SWrsj9rg2J7qyv9GMm1geC3B9L29bmBn9_hjG7hCIxOcV5TbVuAA8LexL3M_IMyOxr4jrF-ZG7rge_2v6E8quuLC0F_0DaZ9WmgMdYyqQJ2QTWtNubs2JXm_Ig8HfZsDNs3qFbH6cs9v_NXG7uTRaP7El-gI/s1600/image.png" />
