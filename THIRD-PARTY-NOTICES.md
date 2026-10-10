# Third-party notices

jaNET Framework itself is licensed under the GNU General Public License, version 3 or later (see [LICENSE](LICENSE)).
It uses these components; they are restored as NuGet packages and are not part of this repository:

| Component | Used by | License |
| --- | --- | --- |
| [Microsoft.CodeAnalysis.CSharp.Scripting (Roslyn)](https://github.com/dotnet/roslyn) | jaNETFramework: `{ condition; yes; no; }` expressions | MIT |
| [MailKit / MimeKit](https://github.com/jstedfast/MailKit) | jaNETFramework: IMAP unread messages, SMTP and POP3 | MIT |
| [Vosk](https://github.com/alphacep/vosk-api) | Optional, offline speech recognition | Apache-2.0 |
| [System.IO.Ports](https://github.com/dotnet/runtime) | jaNETFramework: serial port | MIT |
| [Microsoft.Extensions.Hosting.Abstractions](https://github.com/dotnet/runtime) | jaNETFramework.Hosting | MIT |
| [MQTTnet](https://github.com/dotnet/MQTTnet) | jaNETFramework: the MQTT client (the tests also use its broker) | MIT |
| [Spectre.Console](https://github.com/spectreconsole/spectre.console) | jaNETProgram: the console interface | MIT |

## Obviex "RijndaelSimple"

Earlier versions of jaNET encrypted the settings files with a class based on the sample "Symmetric key encryption and decryption using Rijndael
algorithm", Copyright (C) 2002 Obviex(TM), provided "as is" without warranty. That class is gone. The settings files are now written with
AES-256-GCM (`SettingsCipher`). `LegacySettingsCipher` only keeps the parameters of the old file format (pass phrase based key, SHA1, CBC)
so that files written by older versions can be read once and converted; it contains none of the sample's code.

## Other sources

The web UI (`www`) uses no third-party scripts or style sheets; the earlier jQuery, jQuery Mobile, Raphael, JustGage, d3 and datebox
copies were replaced by plain HTML, CSS and ES modules.

## Optional speech and weather

Vosk native libraries are included in build/publish outputs on supported platforms. Vosk is Apache-2.0; see its upstream repository for its license and native dependency notices.
English `vosk-model-small-en-us-0.15` is downloaded only on request and is Apache-2.0 according to the [official model list](https://alphacephei.com/vosk/models). User-installed language models retain their respective licenses.

The condition-based sky sprite `www/images/weather-skies.png` was generated with OpenAI's built-in image generation tool for this project. It is illustrated weather artwork, not meteorological imagery. Prompt: eight regular sky panels in a two-column/four-row grid: sunny, partly cloudy, overcast, rain, thunderstorm, snow, fog and clear night; no text; quiet space for UI labels.

The date-card calendar and completion icons use Lucide's `calendar-days` and `circle-check` artwork under the ISC license. Copyright (c) 2026 Lucide Icons and Contributors. The notice and license are included in `www/icons/LICENSE-lucide.txt`; source: <https://github.com/lucide-icons/lucide>.
eSpeak NG (GPL-3.0-or-later) and Piper (GPL-3.0) are optional external executables, not installed or bundled by jaNET. Piper voice models have independent licenses; consult each model card before use or redistribution.

Open-Meteo weather data is attributed in the UI to [Open-Meteo](https://open-meteo.com/), under CC BY 4.0. OpenWeatherMap remains available under its own service terms; its weather icons are retrieved over HTTPS.
