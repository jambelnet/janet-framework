Jubito Default Icons
===================

77 local SVG tile icons, grouped into Home & Devices, Automation, Messages &
Voice, Networks & Services, Calendar & Weather, and System. All use a 64 x 64
viewBox, rounded color tile and consistent white Lucide outline artwork.

In Jubito, open Settings > Instructions > Default Icons to browse, search,
copy a Thumbnail URL or download an individual SVG. In Add New Instruction
Set > Add to Dashboard, the image button beside Thumbnail URL opens the
same searchable library. A selection sets a local path such as:

    icons/default/calendar.svg

Home shortcuts use this set automatically. Dashboard and pinned Home cards
replace recognized local legacy images at display time. If Thumbnail URL is
blank, Jubito chooses an icon from the instruction ID or header, then its
category, falling back to Instruction. Ambiguous names use the fallback.
Custom image URLs and explicitly chosen SVGs are preserved. AppConfig.xml
and its old thumbnail attributes are not rewritten. Brand logos and weather
artwork are retained; the four old Home shortcut PNGs are removed.
The optional RaZberry device list uses Outlet, Sensor or Controller icons
based on command classes, with no change to device control behavior.
The icons represent actions; choosing one does not run a command,
install a device integration, change credentials or add a schedule.
Home-device icons are available for user-defined routines implemented with
MQTT, serial, HTTP or other supported actions, not a claim of built-in drivers.
Gmail uses a generic mail symbol, not Google's trademarked logo.

This archive preserves paths relative to www/: default/*.svg, catalog.json,
README.txt, LICENSE-lucide.txt and LICENSE-jaNET.txt. Install these under www/icons/.
The same files ship with jaNET build/publish outputs and work offline.

Catalog source: www/icons/catalog.json
Regenerate SVG assets: node tools/build-icons.mjs
Check generated assets: node tools/build-icons.mjs --check
Regenerate the complete downloadable ZIP on Windows:
    ./tools/build-icon-pack.ps1
On other systems, regenerate SVGs, then zip default/, catalog.json,
README.txt, LICENSE-lucide.txt and LICENSE-jaNET.txt from www/icons/.
The generator uses the already bundled www/vendor/lucide/lucide.min.js;
there is no npm install, CDN, web font or runtime Node requirement.

Licensing
---------
Icon artwork is from Lucide, Copyright (c) 2026 Lucide Icons and Contributors,
under the ISC license. Preserve LICENSE-lucide.txt when redistributing.
Jubito's catalog, generator and UI are part of jaNET Framework (GPL-3.0-or-later),
with the full GPL text in LICENSE-jaNET.txt. SVG artwork remains ISC-licensed.
