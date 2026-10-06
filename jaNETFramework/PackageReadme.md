# jaNETFramework

[jaNET Framework](https://github.com/jambelnet/janet-framework) is an instruction runner for home automation and IoT: instruction sets,
events, a scheduler, a web server (with the Jubito web UI), a socket server, a serial port (Arduino, Z-Wave sticks) and mail, SMS, weather and
dynamic DNS services. This package lets you run it inside your own .NET application.

```csharp
using jaNET.Hosting;

using var janet = new JanetHost(new JanetHostOptions { RootDirectory = "/var/lib/janet" });
janet.Start();                                   // web server, socket server, serial port, scheduler as set up in AppConfig.xml

Console.WriteLine(janet.Execute("%time%"));              // functions
Console.WriteLine(janet.Execute("judo schedule ls"));    // commands
Console.WriteLine(janet.Execute("whoami"));              // your instruction sets
```

## Extend it

```csharp
public sealed class BlindsCommand : JudoCommand                 // judo blinds up | down | state
{
    public BlindsCommand() {
        On(i => { Move(up: true);  return "Blinds are going up."; },   "up");
        On(i => { Move(up: false); return "Blinds are going down."; }, "down");
        On(i => IsUp ? "up" : "down", "state");
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "blinds" };
    // ...
}

var options = new JanetHostOptions();
options.Commands.Add(new BlindsCommand());
options.Functions["garage"] = () => door.IsOpen ? "open" : "closed";      // %garage% in every instruction
options.Speaker = new MyTextToSpeech();                                    // implements ISpeaker
using var janet = new JanetHost(options);
```

Names must be new: a name that already exists makes the constructor throw an `ArgumentException`. Your commands and functions appear in
`janet.Syntax` (for completion) and can be used from the console, the web UI, the socket, events and schedules like the built-in ones.

The built-in web server is Kestrel (http and https, `judo server https on`), so the package references the ASP.NET Core shared framework and the
ASP.NET Core runtime must be installed where your application runs.

For a Worker Service or ASP.NET Core application see the package `jaNETFramework.Hosting` (`services.AddJanet()`).

The web UI (`www`) is copied next to your program when you build and served from there; a `www` folder inside `RootDirectory` takes precedence
(`<JanetCopyWebUi>false</JanetCopyWebUi>` stops the automatic copy). `RootDirectory` is the data folder (AppConfig.xml, settings, key, log); it is
created by `Start()` if it does not exist. Without it, jaNET uses `JANET_HOME`, an existing installation's folder, or a folder of its own
(`%LOCALAPPDATA%/jaNET`, `/var/lib/janet`, `~/.local/share/janet`).

The settings files are encrypted with a key that is created on the first run in `.janet.key` next to AppConfig.xml: keep it with your backups.
jaNET is licensed under the GPL (version 3 or later): applications that use it must be GPL compatible.
