#!/usr/bin/env python3
r"""Package a Unity macOS app as a self-contained GitHub release ZIP.

Usage (Python 3.10+, standard library only)::

    python Tools/package_macos.py \
        --app "Builds/macOS/Bomberman Indie Studio.app" \
        --readme "path/to/release-README.md" \
        --output "Builds/Releases/Bomberman-Indie-Studio-macOS-ARM64.zip"

The archive contains Bomberman-Indie-Studio-macOS/<app name> and README.md.
Directories use Unix mode 0755, ordinary files 0644, and Mach-O binaries 0755.
Internal relative symlinks are preserved without traversal; external, absolute,
and broken links are rejected. Fixed ZIP timestamps make identical inputs
reproducible. The selected output and its adjacent .zip.sha256 file are replaced
only after archive validation. No application files or directories are changed.

Validation covers the bundle's ARM64 main executable, ZIP CRCs, file contents,
entry names and Unix modes. It does not execute the app, verify Apple code
signatures, or notarize it. macOS testing and distribution signing remain
separate steps. Supply an English release README; the script never invents one.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import plistlib
import stat
import struct
import sys
import tempfile
from typing import BinaryIO
import zipfile


ARCHIVE_ROOT = "Bomberman-Indie-Studio-macOS"
CHUNK_SIZE = 1024 * 1024
ARM64 = 0x0100000C
THIN_MAGICS = {
    b"\xce\xfa\xed\xfe": ("<", 28),
    b"\xfe\xed\xfa\xce": (">", 28),
    b"\xcf\xfa\xed\xfe": ("<", 32),
    b"\xfe\xed\xfa\xcf": (">", 32),
}
FAT_MAGICS = {
    b"\xca\xfe\xba\xbe": (">", False),
    b"\xbe\xba\xfe\xca": ("<", False),
    b"\xca\xfe\xba\xbf": (">", True),
    b"\xbf\xba\xfe\xca": ("<", True),
}


class PackageError(ValueError):
    """An input bundle or generated archive failed validation."""


@dataclass(frozen=True)
class Entry:
    name: str
    kind: str
    mode: int
    source: Path | None = None
    payload: bytes = b""
    architectures: tuple[int, ...] = ()


def stream_hash(stream: BinaryIO) -> str:
    digest = hashlib.sha256()
    for block in iter(lambda: stream.read(CHUNK_SIZE), b""):
        digest.update(block)
    return digest.hexdigest()


def file_hash(path: Path) -> str:
    with path.open("rb") as stream:
        return stream_hash(stream)


def read_at(stream: BinaryIO, offset: int, length: int) -> bytes:
    stream.seek(offset)
    result = stream.read(length)
    if len(result) != length:
        raise PackageError("Truncated Mach-O header or architecture table")
    return result


def macho_architectures(stream: BinaryIO, size: int, require_executable: bool = False) -> tuple[int, ...]:
    """Recognize thin/fat Mach-O and validate fat slices without loading binaries."""
    stream.seek(0)
    magic = stream.read(4)
    if magic in THIN_MAGICS:
        endian, header_size = THIN_MAGICS[magic]
        if size < header_size:
            raise PackageError("Truncated Mach-O binary")
        if require_executable and struct.unpack(endian + "I", read_at(stream, 12, 4))[0] != 2:
            raise PackageError("Main Mach-O binary is not an executable")
        return (struct.unpack(endian + "I", read_at(stream, 4, 4))[0],)
    if magic not in FAT_MAGICS:
        return ()

    endian, fat64 = FAT_MAGICS[magic]
    count = struct.unpack(endian + "I", read_at(stream, 4, 4))[0]
    if not 1 <= count <= 128:
        raise PackageError("Invalid fat Mach-O architecture count")
    record_format = endian + ("IIQQII" if fat64 else "IIIII")
    record_size = struct.calcsize(record_format)
    table_end = 8 + count * record_size
    if table_end > size:
        raise PackageError("Truncated fat Mach-O architecture table")
    architectures: list[int] = []
    ranges: list[tuple[int, int]] = []
    for index in range(count):
        record = struct.unpack(
            record_format, read_at(stream, 8 + index * record_size, record_size)
        )
        cpu, _, offset, slice_size, alignment = record[:5]
        if (
            offset < table_end
            or slice_size < 28
            or offset + slice_size > size
            or alignment > 32
            or offset % (1 << alignment)
        ):
            raise PackageError("Invalid fat Mach-O slice bounds or alignment")
        if any(offset < end and offset + slice_size > start for start, end in ranges):
            raise PackageError("Overlapping fat Mach-O slices")
        slice_magic = read_at(stream, offset, 4)
        if slice_magic not in THIN_MAGICS:
            raise PackageError("Fat Mach-O slice does not contain a Mach-O header")
        slice_endian, header_size = THIN_MAGICS[slice_magic]
        slice_cpu = struct.unpack(slice_endian + "I", read_at(stream, offset + 4, 4))[0]
        if slice_size < header_size or slice_cpu != cpu:
            raise PackageError("Fat Mach-O slice disagrees with its architecture table")
        if require_executable and struct.unpack(slice_endian + "I", read_at(stream, offset + 12, 4))[0] != 2:
            raise PackageError("Main fat Mach-O slice is not an executable")
        ranges.append((offset, offset + slice_size))
        architectures.append(cpu)
    return tuple(architectures)


def binary_architectures(path: Path, require_executable: bool = False) -> tuple[int, ...]:
    with path.open("rb") as stream:
        return macho_architectures(stream, os.fstat(stream.fileno()).st_size, require_executable)


def directory_entry(name: str) -> Entry:
    return Entry(name.rstrip("/") + "/", "directory", stat.S_IFDIR | 0o755)


def app_entries(app: Path) -> list[Entry]:
    """Walk directory entries explicitly so symlink directories are never followed."""
    entries = [directory_entry(ARCHIVE_ROOT), directory_entry(f"{ARCHIVE_ROOT}/{app.name}")]

    def visit(directory: Path) -> None:
        with os.scandir(directory) as iterator:
            children = sorted(iterator, key=lambda child: child.name)
        for child in children:
            source = Path(child.path)
            relative = source.relative_to(app).as_posix()
            if "\\" in relative:
                raise PackageError(f"Backslash in bundle path: {relative}")
            name = f"{ARCHIVE_ROOT}/{app.name}/{relative}"
            attributes = child.stat(follow_symlinks=False)
            if getattr(attributes, "st_file_attributes", 0) & 0x400 and not child.is_symlink():
                raise PackageError(f"Unsupported Windows reparse point in app: {relative}")
            if child.is_symlink():
                target = os.readlink(source)
                if (
                    "\\" in target
                    or PurePosixPath(target).is_absolute()
                    or (len(target) >= 2 and target[1] == ":")
                ):
                    raise PackageError(f"Symlink must use a relative Unix path: {relative}")
                resolved = source.resolve(strict=True)
                if not resolved.is_relative_to(app):
                    raise PackageError(f"Symlink escapes the application bundle: {relative}")
                entries.append(Entry(name, "symlink", stat.S_IFLNK | 0o777, source, os.fsencode(target)))
            elif child.is_dir(follow_symlinks=False):
                entries.append(directory_entry(name))
                visit(source)
            elif child.is_file(follow_symlinks=False):
                architectures = binary_architectures(source)
                mode = stat.S_IFREG | (0o755 if architectures else 0o644)
                entries.append(Entry(name, "file", mode, source, architectures=architectures))
            else:
                raise PackageError(f"Unsupported special file in app: {relative}")

    visit(app)
    return entries


def validate_bundle(app: Path) -> tuple[str, tuple[int, ...]]:
    plist_path = app / "Contents" / "Info.plist"
    if not plist_path.is_file():
        raise PackageError("Application is missing Contents/Info.plist")
    with plist_path.open("rb") as stream:
        properties = plistlib.load(stream)
    if not isinstance(properties, dict):
        raise PackageError("Application Info.plist is not a dictionary")
    executable = properties.get("CFBundleExecutable")
    if (
        not isinstance(executable, str)
        or not executable
        or executable in {".", ".."}
        or any(character in executable for character in "/\\\0")
    ):
        raise PackageError("Info.plist has an invalid CFBundleExecutable")
    if properties.get("CFBundlePackageType", "APPL") != "APPL":
        raise PackageError("Info.plist does not identify an application bundle")
    executable_path = app / "Contents" / "MacOS" / executable
    if not executable_path.is_file():
        raise PackageError(f"Main executable is missing: {executable}")
    architectures = binary_architectures(executable_path, require_executable=True)
    if ARM64 not in architectures:
        raise PackageError("CFBundleExecutable does not contain an ARM64 Mach-O slice")
    return executable, architectures


def write_archive(path: Path, entries: list[Entry]) -> dict[str, str]:
    digests: dict[str, str] = {}
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for entry in entries:
            info = zipfile.ZipInfo(entry.name, date_time=(1980, 1, 1, 0, 0, 0))
            info.create_system = 3
            info.external_attr = (entry.mode << 16) | (0x10 if entry.kind == "directory" else 0)
            info.compress_type = zipfile.ZIP_DEFLATED
            if entry.kind == "file":
                digest = hashlib.sha256()
                with entry.source.open("rb") as source, archive.open(info, "w", force_zip64=True) as target:
                    for block in iter(lambda: source.read(CHUNK_SIZE), b""):
                        target.write(block)
                        digest.update(block)
                digests[entry.name] = digest.hexdigest()
            else:
                archive.writestr(info, entry.payload)
                digests[entry.name] = hashlib.sha256(entry.payload).hexdigest()
    return digests


def verify_archive(path: Path, entries: list[Entry], digests: dict[str, str]) -> None:
    with zipfile.ZipFile(path, "r") as archive:
        if archive.namelist() != [entry.name for entry in entries]:
            raise PackageError("Archive entries differ from the input manifest")
        corrupt = archive.testzip()
        if corrupt is not None:
            raise PackageError(f"Archive CRC verification failed: {corrupt}")
        for entry in entries:
            info = archive.getinfo(entry.name)
            if info.create_system != 3 or info.external_attr >> 16 != entry.mode:
                raise PackageError(f"Archive Unix mode mismatch: {entry.name}")
            with archive.open(info) as stream:
                if stream_hash(stream) != digests[entry.name]:
                    raise PackageError(f"Archive contents mismatch: {entry.name}")
            if entry.kind == "file" and file_hash(entry.source) != digests[entry.name]:
                raise PackageError(f"Input changed while packaging: {entry.source}")
            if entry.kind == "symlink" and os.fsencode(os.readlink(entry.source)) != entry.payload:
                raise PackageError(f"Input symlink changed while packaging: {entry.source}")


def package(app_argument: Path, readme_argument: Path, output_argument: Path) -> dict:
    if app_argument.is_symlink() or not app_argument.is_dir() or app_argument.suffix.lower() != ".app":
        raise PackageError("--app must be an existing .app directory, not a symlink")
    app = app_argument.resolve(strict=True)
    readme = readme_argument.resolve(strict=True)
    if not readme.is_file():
        raise PackageError("--readme must be an existing file")
    output = output_argument.absolute()
    checksum = output.with_suffix(output.suffix + ".sha256")
    if output.suffix.lower() != ".zip":
        raise PackageError("--output must end in .zip")
    for destination in (output, checksum):
        if destination.is_symlink() or destination.is_dir():
            raise PackageError(f"Output must be a regular file path: {destination}")
        resolved = destination.resolve()
        if resolved.is_relative_to(app) or resolved == readme:
            raise PackageError("Output paths must be outside the app and must not replace the README")

    entries = app_entries(app)
    executable, architectures = validate_bundle(app)
    entries.append(Entry(f"{ARCHIVE_ROOT}/README.md", "file", stat.S_IFREG | 0o644, readme))
    names = [entry.name for entry in entries]
    if len(names) != len(set(names)):
        raise PackageError("Duplicate archive entry names")

    output.parent.mkdir(parents=True, exist_ok=True)
    temporary_paths: list[Path] = []
    try:
        with tempfile.NamedTemporaryFile(prefix=output.name + ".", suffix=".tmp", dir=output.parent, delete=False) as temp:
            zip_temporary = Path(temp.name)
            temporary_paths.append(zip_temporary)
        digests = write_archive(zip_temporary, entries)
        verify_archive(zip_temporary, entries, digests)
        if app_entries(app) != entries[:-1]:
            raise PackageError("Application entries changed while packaging")
        digest = file_hash(zip_temporary)
        with tempfile.NamedTemporaryFile(prefix=checksum.name + ".", suffix=".tmp", dir=output.parent, delete=False) as temp:
            checksum_temporary = Path(temp.name)
            temporary_paths.append(checksum_temporary)
            temp.write(f"{digest}  {output.name}\n".encode("utf-8"))
            temp.flush()
            os.fsync(temp.fileno())
        os.replace(zip_temporary, output)
        os.replace(checksum_temporary, checksum)
        if file_hash(output) != digest:
            raise PackageError("Final ZIP SHA256 verification failed")
        if checksum.read_text(encoding="utf-8") != f"{digest}  {output.name}\n":
            raise PackageError("SHA256 sidecar verification failed")
        return {
            "output": str(output),
            "sha256_file": str(checksum),
            "sha256": digest,
            "bytes": output.stat().st_size,
            "entries": len(entries),
            "files": sum(entry.kind == "file" for entry in entries),
            "native_binaries": sum(bool(entry.architectures) for entry in entries),
            "symlinks": sum(entry.kind == "symlink" for entry in entries),
            "main_executable": executable,
            "main_cpu_types": [f"0x{cpu:08x}" for cpu in architectures],
            "arm64": True,
            "validated": True,
        }
    finally:
        for temporary in temporary_paths:
            temporary.unlink(missing_ok=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--app", type=Path, required=True)
    parser.add_argument("--readme", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    try:
        result = package(arguments.app, arguments.readme, arguments.output)
    except (OSError, ValueError, RuntimeError, zipfile.BadZipFile, struct.error) as error:
        print(json.dumps({"error": str(error)}, ensure_ascii=False), file=sys.stderr)
        return 1
    print(json.dumps(result, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
