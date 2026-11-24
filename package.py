from pathlib import Path
import zipfile


ROOT = Path(__file__).resolve().parent
NAME = "GunsawDualSenceSupport"
NAMES = {
    "macos-arm64": "libgunsaw-dualsense-sdl.dylib",
    "windows-x64": "gunsaw-dualsense-sdl.dll",
    "linux-x64": "libgunsaw-dualsense-sdl.so",
}


def main():
    managed = ROOT / "bin/Release/netstandard2.0" / f"{NAME}.dll"
    if not managed.is_file():
        raise SystemExit(f"Build {NAME}.csproj in Release first.")
    native = ROOT / "Native"
    output = ROOT / "dist"
    output.mkdir(exist_ok=True)
    prefix = f"BepInEx/plugins/{NAME}/"
    for platform, filename in NAMES.items():
        path = output / f"{NAME}-{platform}.zip"
        with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
            for file in (managed, ROOT / f"{NAME}.dll.config", native / "SDL-LICENSE.txt", native / filename):
                archive.write(file, prefix + file.name)
            if platform == "macos-arm64":
                for name in ("libgunsaw-dualsense-bt-audio.dylib", "OPUS-LICENSE.txt", "DUALSENSE-AUDIO-LICENSE.txt"):
                    archive.write(native / name, prefix + name)
            if platform == "linux-x64":
                archive.write(native / "SDL-DEBIAN-COPYRIGHT.txt", prefix + "SDL-DEBIAN-COPYRIGHT.txt")
        print(path)

    with zipfile.ZipFile(output / f"{NAME}-Emulator.zip", "w", zipfile.ZIP_DEFLATED) as archive:
        for name in ("emulator.py", "index.html"):
            archive.write(ROOT / "Emulator" / name, name)
        archive.write(ROOT / "README.md", "README.md")


if __name__ == "__main__":
    main()
