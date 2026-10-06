# Third-party notices

jaNET Framework itself is licensed under the GNU General Public License, version 3 or later (see [LICENSE](LICENSE)).
It uses these components, all under the MIT license; they are restored as NuGet packages and are not part of this repository:

| Component | Used by | License |
| --- | --- | --- |
| [Microsoft.CodeAnalysis.CSharp.Scripting (Roslyn)](https://github.com/dotnet/roslyn) | jaNETFramework: `{ condition; yes; no; }` expressions | MIT |
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
