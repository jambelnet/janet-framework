#!/usr/bin/env python3
"""Publishes jaNETProgram for other systems and packs it the way that system wants it.

    python tools/publish.py                          every system below, self-contained
    python tools/publish.py linux-arm64 win-x64      only these
    python tools/publish.py --framework-dependent    small: needs the .NET *and* the ASP.NET Core runtime on the target

The result is artifacts/janet-<version>-<rid>.tar.gz (Linux, macOS) or .zip (Windows). A self-contained build runs without anything installed.
The archives are made here and not by "zip" on Windows because only a tar keeps the "executable" flag of jaNETProgram.
Which system am I? Linux: uname -m   x86_64 = linux-x64, aarch64 = linux-arm64, armv7l = linux-arm   (macOS: osx-arm64 or osx-x64).
"""
import io
import os
import shutil
import subprocess
import sys
import tarfile
import zipfile

SYSTEMS = ["linux-x64", "linux-arm64", "linux-arm", "osx-arm64", "osx-x64", "win-x64", "win-arm64"]
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXECUTABLE = {"jaNETProgram", "createdump"}


def run(*command):
    subprocess.run(command, cwd=ROOT, check=True)


def version():
    out = subprocess.run(["dotnet", "msbuild", "jaNETFramework/jaNETFramework.csproj", "-getProperty:Version"],
                         cwd=ROOT, check=True, capture_output=True, text=True)
    return out.stdout.strip()


def extras(rid):
    yield os.path.join(ROOT, "LICENSE"), "LICENSE"
    yield os.path.join(ROOT, "CHANGELOG.md"), "CHANGELOG.md"
    yield os.path.join(ROOT, "THIRD-PARTY-NOTICES.md"), "THIRD-PARTY-NOTICES.md"
    if rid.startswith("linux"):
        yield os.path.join(ROOT, "deploy", "janet.service"), "janet.service"


def files(folder, rid):
    for base, _, names in os.walk(folder):
        for name in sorted(names):
            path = os.path.join(base, name)
            yield path, os.path.relpath(path, folder).replace(os.sep, "/")
    yield from extras(rid)


def pack_tar(target, top, folder, rid):
    with tarfile.open(target, "w:gz", compresslevel=9) as tar:
        directories = set()
        for path, relative in files(folder, rid):
            parts = relative.split("/")
            for i in range(1, len(parts)):
                directories.add("/".join(parts[:i]))
        for directory in sorted(directories | {""}):
            info = tarfile.TarInfo(f"{top}/{directory}".rstrip("/"))
            info.type, info.mode = tarfile.DIRTYPE, 0o755
            tar.addfile(info)
        for path, relative in files(folder, rid):
            info = tarfile.TarInfo(f"{top}/{relative}")
            info.size = os.path.getsize(path)
            info.mode = 0o755 if os.path.basename(path) in EXECUTABLE else 0o644
            with open(path, "rb") as stream:
                tar.addfile(info, stream)


def pack_zip(target, top, folder, rid):
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path, relative in files(folder, rid):
            archive.write(path, f"{top}/{relative}")


def main(arguments):
    dependent = "--framework-dependent" in arguments
    chosen = [a for a in arguments if not a.startswith("--")] or SYSTEMS
    unknown = [a for a in chosen if a not in SYSTEMS]
    if unknown:
        sys.exit(f"Unknown system: {' '.join(unknown)}. Known: {' '.join(SYSTEMS)}")

    number = version()
    output = os.path.join(ROOT, "artifacts")
    os.makedirs(output, exist_ok=True)

    for rid in chosen:
        folder = os.path.join(output, "build", rid)
        shutil.rmtree(folder, ignore_errors=True)
        run("dotnet", "publish", "jaNETProgram", "-c", "Release", "-r", rid,
            "--self-contained", "false" if dependent else "true", "-o", folder, "-nologo", "-v:q")

        top = f"janet-{number}-{rid}" + ("-framework-dependent" if dependent else "")
        target = os.path.join(output, top + (".zip" if rid.startswith("win") else ".tar.gz"))
        (pack_zip if rid.startswith("win") else pack_tar)(target, top, folder, rid)
        print(f"{target}  ({os.path.getsize(target) // 1024 // 1024} MB)")

    shutil.rmtree(os.path.join(output, "build"), ignore_errors=True)


if __name__ == "__main__":
    main(sys.argv[1:])
