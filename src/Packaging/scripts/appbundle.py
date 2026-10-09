#!/usr/bin/env python3
"""Assemblage et contrôle des AppBundles Revit d'EG_ROOM_TRACKER (EGR-13).

Sous-commandes :
  assemble  --revit-version 2026 --build-dir <bin/2026/Release> --out <dossier> [--app-version 0.1.0] [--with-symbols]
  check     <zip> --revit-version 2026

Structure produite (APS, Execute a Revit Add-in, Task 4) :
  EgRoomTracker_R2026.zip
  └── EgRoomTracker.bundle/
      ├── PackageContents.xml
      └── Contents/
          ├── EgRoomTracker.addin
          ├── EgRoomTracker.dll, EgRoomTracker.*.dll
          └── DesignAutomationBridge.dll

Le contrôle échoue (code 1) si un fichier requis manque, si un fichier hors liste autorisée est présent
(RevitAPI*.dll, *.deps.json, *.runtimeconfig.json…), ou si les manifestes sont incohérents avec la version.
Aucune dépendance hors bibliothèque standard Python 3.
"""
from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

SUPPORTED_VERSIONS = ("2025", "2026", "2027")
BUNDLE = "EgRoomTracker.bundle"
CONTENTS = f"{BUNDLE}/Contents"
ADDIN_NAME = "EgRoomTracker.addin"
ENTRY_ASSEMBLY = "EgRoomTracker.dll"
ENTRY_CLASS = "EgRoomTracker.EntryPoint.AutomationApplication"
APP_ASSEMBLIES = (
    "EgRoomTracker.dll",
    "EgRoomTracker.Application.dll",
    "EgRoomTracker.Contracts.dll",
    "EgRoomTracker.Export.dll",
    "EgRoomTracker.Logging.dll",
    "EgRoomTracker.Revit.dll",
)
BRIDGE = "DesignAutomationBridge.dll"
REQUIRED = (
    f"{BUNDLE}/PackageContents.xml",
    f"{CONTENTS}/{ADDIN_NAME}",
    *(f"{CONTENTS}/{name}" for name in (*APP_ASSEMBLIES, BRIDGE)),
)
# Symboles facultatifs (--with-symbols) : uniquement ceux de nos assemblies.
OPTIONAL = tuple(f"{CONTENTS}/{name[:-4]}.pdb" for name in APP_ASSEMBLIES)
# Motifs interdits, signalés explicitement même s'ils sont déjà hors liste autorisée.
FORBIDDEN_PATTERNS = (
    (re.compile(r"(^|/)RevitAPI[^/]*\.dll$", re.IGNORECASE), "assembly Revit API (fournie par le moteur)"),
    (re.compile(r"(^|/)RevitAddInUtility\.dll$", re.IGNORECASE), "assembly Revit (fournie par le moteur)"),
    (re.compile(r"(^|/)AdWindows\.dll$", re.IGNORECASE), "assembly d'interface Revit"),
    (re.compile(r"\.deps\.json$", re.IGNORECASE), "*.deps.json inutile (chargement dans le contexte Revit par défaut)"),
    (re.compile(r"\.runtimeconfig\.json$", re.IGNORECASE), "*.runtimeconfig.json inutile pour un add-in"),
    (re.compile(r"(^|/)(settings\.json|\.env|.*\.(pfx|pem|key))$", re.IGNORECASE), "fichier de configuration ou secret"),
)
# Framework attendu de l'assembly d'entrée (build/Revit.props).
EXPECTED_FRAMEWORK = {"2025": ".NETCoreApp,Version=v8.0", "2026": ".NETCoreApp,Version=v8.0", "2027": ".NETCoreApp,Version=v10.0"}

PACKAGING_DIR = Path(__file__).resolve().parent.parent
FIXED_ZIP_TIME = (2026, 1, 1, 0, 0, 0)


def series(version: str) -> str:
    return f"R{version}"


def render_package_contents(version: str, app_version: str) -> bytes:
    template = (PACKAGING_DIR / "bundle" / "PackageContents.xml.template").read_text(encoding="utf-8")
    return template.replace("@REVIT_SERIES@", series(version)).replace("@APP_VERSION@", app_version).encode("utf-8")


def assemble(version: str, build_dir: Path, out_dir: Path, app_version: str, with_symbols: bool) -> Path:
    entries: dict[str, bytes] = {
        f"{BUNDLE}/PackageContents.xml": render_package_contents(version, app_version),
        f"{CONTENTS}/{ADDIN_NAME}": (PACKAGING_DIR / "bundle" / ADDIN_NAME).read_bytes(),
    }
    names = [*APP_ASSEMBLIES, BRIDGE]
    if with_symbols:
        names += [n[:-4] + ".pdb" for n in APP_ASSEMBLIES if (build_dir / (n[:-4] + ".pdb")).is_file()]
    for name in names:
        source = build_dir / name
        if not source.is_file():
            raise SystemExit(f"Fichier de build manquant : {source}")
        entries[f"{CONTENTS}/{name}"] = source.read_bytes()

    out_dir.mkdir(parents=True, exist_ok=True)
    target = out_dir / f"EgRoomTracker_R{version}.zip"
    temporary = target.with_suffix(".zip.tmp")
    # ZIP reproductible : entrées triées, date fixe, droits fixes.
    with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name in sorted(entries):
            info = zipfile.ZipInfo(name, date_time=FIXED_ZIP_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            archive.writestr(info, entries[name])
    temporary.replace(target)
    return target


def check(zip_path: Path, version: str) -> list[str]:
    errors: list[str] = []
    try:
        archive = zipfile.ZipFile(zip_path)
    except (OSError, zipfile.BadZipFile) as exc:
        return [f"ZIP illisible : {exc}"]

    with archive:
        names = [n for n in archive.namelist() if not n.endswith("/")]
        for name in names:
            if name.startswith("/") or "\\" in name or ".." in name.split("/"):
                errors.append(f"chemin interdit dans le ZIP : {name}")
        tops = {n.split("/", 1)[0] for n in archive.namelist()}
        if tops != {BUNDLE}:
            errors.append(f"un seul dossier racine attendu ({BUNDLE}), trouvé : {sorted(tops)}")

        for name in names:
            for pattern, reason in FORBIDDEN_PATTERNS:
                if pattern.search(name):
                    errors.append(f"fichier interdit : {name} ({reason})")
        allowed = set(REQUIRED) | set(OPTIONAL)
        for name in names:
            if name not in allowed and not any(p.search(name) for p, _ in FORBIDDEN_PATTERNS):
                errors.append(f"fichier non autorisé : {name}")
        for name in REQUIRED:
            if name not in names:
                errors.append(f"fichier requis manquant : {name}")
        if errors:
            return errors

        errors += check_package_contents(archive.read(f"{BUNDLE}/PackageContents.xml"), version, names)
        errors += check_addin(archive.read(f"{CONTENTS}/{ADDIN_NAME}"), names)
        framework = EXPECTED_FRAMEWORK[version].encode("utf-8")
        if framework not in archive.read(f"{CONTENTS}/{ENTRY_ASSEMBLY}"):
            errors.append(f"{ENTRY_ASSEMBLY} : framework attendu {EXPECTED_FRAMEWORK[version]} introuvable (mauvaise RevitVersion ?)")
    return errors


def check_package_contents(data: bytes, version: str, names: list[str]) -> list[str]:
    errors = []
    try:
        root = ET.fromstring(data)
    except ET.ParseError as exc:
        return [f"PackageContents.xml invalide : {exc}"]
    requirements = root.findall("./Components/RuntimeRequirements")
    entries = root.findall("./Components/ComponentEntry")
    if len(requirements) != 1 or len(entries) != 1:
        return ["PackageContents.xml : un seul RuntimeRequirements et un seul ComponentEntry attendus"]
    req, entry = requirements[0].attrib, entries[0].attrib
    expected = series(version)
    if req.get("Platform") != "Revit" or req.get("OS") != "Win64":
        errors.append("PackageContents.xml : Platform=Revit et OS=Win64 attendus")
    if req.get("SeriesMin") != expected or req.get("SeriesMax") != expected:
        errors.append(f"PackageContents.xml : SeriesMin/SeriesMax = {expected} attendus (engine Autodesk.Revit+{version})")
    module = entry.get("ModuleName", "")
    if module != f"./Contents/{ADDIN_NAME}" or f"{BUNDLE}/{module[2:]}" not in names:
        errors.append(f"PackageContents.xml : ModuleName ./Contents/{ADDIN_NAME} attendu, trouvé {module!r}")
    if "@" in data.decode("utf-8", errors="replace"):
        errors.append("PackageContents.xml : gabarit non remplacé (@…@)")
    return errors


def check_addin(data: bytes, names: list[str]) -> list[str]:
    errors = []
    try:
        root = ET.fromstring(data)
    except ET.ParseError as exc:
        return [f"{ADDIN_NAME} invalide : {exc}"]
    addins = root.findall("./AddIn")
    if len(addins) != 1:
        return [f"{ADDIN_NAME} : un seul AddIn attendu"]
    addin = addins[0]
    if addin.get("Type") != "DBApplication":
        errors.append(f"{ADDIN_NAME} : Type=DBApplication attendu")
    assembly = (addin.findtext("Assembly") or "").strip()
    relative = assembly.replace("\\", "/").removeprefix("./")
    if re.match(r"^([A-Za-z]:|/)", relative) or f"{CONTENTS}/{relative}" not in names:
        errors.append(f"{ADDIN_NAME} : Assembly relative vers {ENTRY_ASSEMBLY} attendue, trouvé {assembly!r}")
    if (addin.findtext("FullClassName") or "").strip() != ENTRY_CLASS:
        errors.append(f"{ADDIN_NAME} : FullClassName {ENTRY_CLASS} attendu")
    if not re.fullmatch(r"[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}", (addin.findtext("AddInId") or "").strip()):
        errors.append(f"{ADDIN_NAME} : AddInId GUID attendu")
    if (addin.findtext("ManifestSettings/UseRevitContext") or "").strip().lower() == "false":
        errors.append(f"{ADDIN_NAME} : contexte isolé (UseRevitContext=False) non prévu sans *.deps.json")
    return errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    a = sub.add_parser("assemble")
    a.add_argument("--revit-version", required=True, choices=SUPPORTED_VERSIONS)
    a.add_argument("--build-dir", required=True, type=Path)
    a.add_argument("--out", required=True, type=Path)
    a.add_argument("--app-version", default="0.0.0")
    a.add_argument("--with-symbols", action="store_true")
    c = sub.add_parser("check")
    c.add_argument("zip", type=Path)
    c.add_argument("--revit-version", required=True, choices=SUPPORTED_VERSIONS)
    args = parser.parse_args(argv)

    if args.command == "assemble":
        target = assemble(args.revit_version, args.build_dir, args.out, args.app_version, args.with_symbols)
        print(target)
        return 0

    errors = check(args.zip, args.revit_version)
    if errors:
        print(f"ÉCHEC du contrôle de {args.zip} :", file=sys.stderr)
        for error in errors:
            print(f"  - {error}", file=sys.stderr)
        return 1
    print(f"OK : {args.zip} (Revit {args.revit_version})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
