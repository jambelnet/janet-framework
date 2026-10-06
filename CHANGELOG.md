# Changelog

All notable changes to jaNET Framework. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), the version numbers follow
[Semantic Versioning](https://semver.org/). The version is set in `Directory.Build.props`; a release is a tag `v<version>` (see README, *Releasing*).

## [Unreleased]

## [1.0.0-rc.1] - 2026-10-06

First release of the modernized framework, after 0.3.1.94. It is a release candidate: it passes 480+ automated tests and a replay of the original
program's behavior, but it has not yet been run against real serial devices, mail/SMS/DynDNS accounts, an ESP32 or a real MQTT broker.

### Breaking changes

* **.NET 10** (was .NET Framework 4.x / Mono). Running a published build needs the .NET and the **ASP.NET Core runtime** (the web server is Kestrel).
* **The public library API is new.** `JanetHost` (with `JanetHostOptions`) replaces `Application`, `Methods`, `Parser`, the `AppConfig` classes and the `Parse()`
  extension. The documented ways to talk to jaNET (judo API over http, socket, serial port, console) are unchanged.
* **Settings files** (`.htaccess`, `.smtpsettings`, ...) are written with AES-256-GCM and a key per installation (`.janet.key` or `JANET_KEY`). Files of older versions are
  read and converted automatically on first use; older versions cannot read the converted files, so keep a copy if you want to go back.
* **Data folder:** `AppConfig.xml`, the settings, the key and `log.txt` live in a folder of their own (`JANET_HOME` / `--root`, an existing installation's folder, or
  `%LOCALAPPDATA%\jaNET`, `/var/lib/janet`, `~/.local/share/janet`, `~/Library/Application Support/jaNET`). An existing installation keeps working where it is.
* **The update check** reads versions as versions ("1.2.0"); the old number-without-dots file never announces an update to 1.0 and later.
* `judo help` has two new chapters (MQTT is 13, Help moved to 14). `/www` and `/` answer `301` to `/www/` instead of `404`.

### Added

* **HTTPS:** `judo server https on|off|cert`, a self-signed certificate that is kept and renewed, or your own `.pfx` (password kept encrypted); the plain port then serves this
  computer only and redirects everybody else.
* **MQTT:** `judo mqtt ...`: subscriptions run actions with `%mqtttopic%`, `%mqttpayload%` and `%mqttnumber%` (made safe against injection), publishing, reconnect, status topic.
* **Library:** extension points (`JudoCommand`, custom `%functions%`, `ISpeaker`), `services.AddJanet()` for the .NET generic host, NuGet packages `jaNETFramework` and
  `jaNETFramework.Hosting` (with the web UI), `JanetHost.Notices`.
* **Console:** a fancy console (banner, service table, line editor with history, shell-like Tab completion, colours, tables for lists and for `judo help`) that falls back to the
  classic text console when input or output is redirected; `--headless` for services and containers, `--plain`, `--root`.
* **Notices:** the console, headless mode and `AddJanet` say why a service did not start (port in use, no rights, bad address, certificate, broker login), damaged files, and unsafe
  settings (no password, password in clear text, default `admin`/`admin`). `judo server start`, `judo socket start`, `judo serial open` answer with a `Reason:` line.
* **Web UI:** rewritten without jQuery Mobile: mobile first, light and dark theme, native dialogs, quick actions always in one row, settings groups collapsed.
* **Programs for every system** that run without installing anything: `tools/publish.py` (and the release workflow) make `janet-<version>-<system>.tar.gz` for linux-x64, linux-arm64, linux-arm and
  osx, `.zip` for Windows, with the executable flag set; `--framework-dependent` makes small ones.
* `judo weather key` (the OpenWeatherMap key is kept encrypted), `/api/instructions`, `LICENSE`, licence headers, `THIRD-PARTY-NOTICES.md`, CI, this changelog.

### Changed

* The web server runs on Kestrel instead of `HttpListener`; listening on a network address no longer needs administrator rights on Windows.
* Structure: one composition root (`JanetHost`), one class per judo command group, an async web server and scheduler, a typed configuration store with atomic writes
  (`AppConfig.xml.bak` is kept); see `ARCHITECTURE.md`.
* Fixed deliberately (see `tools/approved-differences.md`): `judo mailheaders set` saves; the socket server answers an unknown instruction; `~>` trims its operands.

### Security

* Only the `www` folder is served; the web login is compared in constant time and wrong guesses are slowed down; secrets are encrypted; ids and values are never spliced into XPath.
* No API key ships in the default configuration (the OpenWeatherMap key of the original is gone from it, revoke it at the provider if it was yours).

### Removed

* jQuery, jQuery Mobile, Raphael, JustGage, d3 and the datebox copies of the web UI (plain HTML, CSS and ES modules now).

[Unreleased]: https://github.com/jambelnet/janet-framework/compare/v1.0.0-rc.1...HEAD
[1.0.0-rc.1]: https://github.com/jambelnet/janet-framework/releases/tag/v1.0.0-rc.1
