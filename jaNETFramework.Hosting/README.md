# jaNETFramework.Hosting

Runs [jaNET Framework](https://github.com/jambelnet/janet-framework) as a background service of a .NET generic host.

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddJanet(options => {
    options.RootDirectory = "/var/lib/janet";                          // AppConfig.xml, settings, key, log (optional)
    options.Functions["garage"] = () => door.IsOpen ? "open" : "closed";   // %garage% in every instruction
    options.Commands.Add(new BlindsCommand());                          // judo blinds up|down|state
});

builder.Build().Run();
```

jaNET starts and stops with the application. Unless you set `RootDirectory` (or the environment variable `JANET_HOME`), AppConfig.xml and the
settings live in the application folder, not in the working directory; the `www` folder is copied there by the core package when you build. Inject `JanetHost` to run instructions from your own code:

```csharp
app.MapGet("/status", (JanetHost janet) => janet.Execute("%whereami%"));
```

A service that could not start (port in use, no rights), a damaged `AppConfig.xml` and unsafe settings are written to the application's log at
start-up (`ILogger`, category `JanetHostedService`); `JanetHost.Notices` returns the same list for your own code.

`%exit%` stops the application instead of ending the process. See the core package `jaNETFramework` for the commands, functions and the
web, socket and serial interfaces. jaNET is licensed under the GPL (version 3 or later): applications that use it must be GPL compatible.
