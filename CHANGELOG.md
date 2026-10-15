# Changelog

All notable changes to jaNET Framework. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), the version numbers follow
[Semantic Versioning](https://semver.org/). The version is set in `Directory.Build.props`; a release is a tag `v<version>` (see README, *Releasing*).

## [Unreleased]

## [1.0.0-rc.3] - 2026-10-15

### Fixed

* Speech uses one configured destination for instruction/event output instead of speaking through both the box and browser. Saved device voices survive delayed enumeration and dialog reopening; missing named voices report an error instead of falling back. Saving changed settings stops old playback and clears queued browser speech. Test voice previews the configured box engine when box output is selected.
* Windows box voices now use an installed-voice dropdown instead of an ambiguous language field alongside unrelated browser voices. Only controls for the active speech destination are shown; box mode no longer reads assistant replies with a different browser voice. Added real installed-voice synthesis and authenticated voice-discovery tests.

* GitHub release publishing refreshes assets on existing releases without recreating the release or changing its notes/state. Added matching SHA-256 checksums, serialized publishing per tag, manual recovery against an existing tag, and regression tests for repeat runs and publication failures.

## [1.0.0-rc.2] - 2026-10-15

### Added

* Read-only Google Calendar integration: encrypted OAuth credentials, browser-bound sign-in with PKCE, automatic token refresh and disconnect/revocation. Settings loads saved values without exposing secrets and previews upcoming events.
* Home Google Calendar widget with calendar-style date columns, upcoming events, saved-zone times, all-day ranges, refresh/settings controls and separate disconnected, empty and error states. Documented localhost sign-in followed by normal HTTPS LAN access.
* Default icon library: 77 locally bundled, consistently styled SVG tiles, searchable gallery, thumbnail picker and downloadable pack with catalog/license. Existing custom images and instruction behavior remain unchanged.
* Automatically apply default icons to Home shortcuts and dashboard/pinned cards, resolving known legacy image paths without rewriting saved XML. Removed the four legacy shortcut PNGs; custom thumbnails, logos and weather artwork are preserved.
* IP camera profiles with encrypted credentials, Basic/Digest HTTP support, automatic JPEG/PNG snapshot or MJPEG detection and Home widgets. Added pause/resume, fullscreen, connection testing, visibility-aware streaming and a camera icon; RTSP requires a browser-compatible gateway.
* Stored-ID camera proxy with same-origin/HTTPS guards, credential redaction, DNS destination checks, redirect blocking, bounded snapshots/streams/connections and cancellation when profiles change. Camera reads never execute instructions or trigger mail/speech.
* `judo calendar upcoming`, `today`, `next`, `json` and `status`, with explicit time-zone handling, recurring-event expansion, all-day dates, bounded event limits and actionable errors. Instruction Studio includes Calendar command blocks; no events or schedules are installed automatically.

### Fixed

* MQTT reports a usable connection only after the broker acknowledges configured subscriptions, preventing messages sent immediately after connection from racing subscription setup. Added a delayed-acknowledgement regression test and clearer flood-test diagnostics.
* Behavior replay includes the reviewed Google Calendar help additions in its expected transcript; other recorded behavior remains unchanged.
* Pin System.Security.Cryptography.Pkcs to 10.0.13 to include the security fixes for CVE-2026-70162 and CVE-2026-84008 and keep audited Release restores passing.
* Desktop Home places cameras beside Calendar, expands camera tiles into the available space and uses compact shortcut/camera controls. Mobile keeps its stacked layout and touch-sized camera buttons.
* Instruction Studio's Dependency Map toolbar no longer leaves an empty scrollbar gutter beside the Properties pane. Builder and Source retain stable scrollbar spacing.
* Google Calendar sign-in rejects non-loopback IP redirect hosts before saving or navigating to Google, including previously saved HTTPS LAN callbacks. The UI no longer suggests unsupported IP redirects and catches page/callback origin mismatches before changing credentials.
* JSON web-service saves ignore inactive XML namespace/index fields; failed saves keep the form open, and resetting it restores the correct format controls.
* XML web-service failures report malformed XML, unmatched nodes and invalid XPath instead of silently returning an empty result. Valid empty values remain supported.
* Saved web services preserve quoted predicates, wildcard selectors, keys with spaces and percent-encoded endpoint query values. Namespace-qualified XPath accepts standard node/attribute paths alongside the legacy syntax.

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

[Unreleased]: https://github.com/jambelnet/janet-framework/compare/v1.0.0-rc.3...HEAD
[1.0.0-rc.3]: https://github.com/jambelnet/janet-framework/compare/v1.0.0-rc.2...v1.0.0-rc.3
[1.0.0-rc.2]: https://github.com/jambelnet/janet-framework/compare/v1.0.0-rc.1...v1.0.0-rc.2
[1.0.0-rc.1]: https://github.com/jambelnet/janet-framework/releases/tag/v1.0.0-rc.1
