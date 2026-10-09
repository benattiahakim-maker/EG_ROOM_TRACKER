"""Tests des scripts de packaging (EGR-13), sans réseau ni Revit : python3 -m unittest discover src/Packaging/scripts/tests"""
from __future__ import annotations

import io
import json
import re
import shutil
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock

SCRIPTS = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(SCRIPTS))
import appbundle  # noqa: E402
import deploy  # noqa: E402


def fake_build_dir(root: Path, version: str, extra: dict[str, bytes] | None = None) -> Path:
    build = root / f"bin{version}"
    build.mkdir(exist_ok=True)
    framework = appbundle.EXPECTED_FRAMEWORK[version].encode()
    for name in (*appbundle.APP_ASSEMBLIES, appbundle.BRIDGE):
        (build / name).write_bytes(b"MZ" + (framework if name == appbundle.ENTRY_ASSEMBLY else b""))
    for name, data in (extra or {}).items():
        (build / name).write_bytes(data)
    return build


def rewrite(zip_path: Path, drop: str | None = None, add: dict[str, bytes] | None = None, replace: dict[str, bytes] | None = None) -> None:
    with zipfile.ZipFile(zip_path) as source:
        entries = {n: source.read(n) for n in source.namelist() if n != drop}
    entries.update(add or {})
    entries.update(replace or {})
    with zipfile.ZipFile(zip_path, "w") as target:
        for name, data in entries.items():
            target.writestr(name, data)


class AppBundleCheckTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp, True)

    def build(self, version="2026", with_symbols=False) -> Path:
        build = fake_build_dir(self.tmp, version, {"EgRoomTracker.pdb": b"pdb", "EgRoomTracker.deps.json": b"{}"})
        return appbundle.assemble(version, build, self.tmp / "out", "0.1.0", with_symbols)

    def test_assembled_bundle_passes_for_every_version(self):
        for version in appbundle.SUPPORTED_VERSIONS:
            with self.subTest(version=version):
                self.tmp = Path(tempfile.mkdtemp())
                self.addCleanup(shutil.rmtree, self.tmp, True)
                self.assertEqual([], appbundle.check(self.build(version), version))

    def test_assembled_bundle_excludes_deps_json_and_symbols_by_default(self):
        names = zipfile.ZipFile(self.build()).namelist()
        self.assertFalse(any(n.endswith((".deps.json", ".pdb")) for n in names))
        self.assertIn("EgRoomTracker.bundle/Contents/DesignAutomationBridge.dll", names)

    def test_symbols_are_optional(self):
        zip_path = self.build(with_symbols=True)
        self.assertIn("EgRoomTracker.bundle/Contents/EgRoomTracker.pdb", zipfile.ZipFile(zip_path).namelist())
        self.assertEqual([], appbundle.check(zip_path, "2026"))

    def test_assembly_is_reproducible(self):
        first = self.build().read_bytes()
        second = self.build().read_bytes()
        self.assertEqual(first, second)

    def test_forbidden_files_fail(self):
        for name in ("RevitAPI.dll", "RevitAPIUI.dll", "EgRoomTracker.deps.json", "EgRoomTracker.runtimeconfig.json", "settings.json"):
            with self.subTest(name=name):
                zip_path = self.build()
                rewrite(zip_path, add={f"EgRoomTracker.bundle/Contents/{name}": b"x"})
                errors = appbundle.check(zip_path, "2026")
                self.assertTrue(any("interdit" in e and name in e for e in errors), errors)

    def test_unlisted_file_fails(self):
        zip_path = self.build()
        rewrite(zip_path, add={"EgRoomTracker.bundle/Contents/Newtonsoft.Json.dll": b"x"})
        self.assertTrue(any("non autorisé" in e for e in appbundle.check(zip_path, "2026")))

    def test_missing_required_files_fail(self):
        for name in appbundle.REQUIRED:
            with self.subTest(name=name):
                zip_path = self.build()
                rewrite(zip_path, drop=name)
                self.assertIn(f"fichier requis manquant : {name}", appbundle.check(zip_path, "2026"))

    def test_wrong_series_fails(self):
        self.assertTrue(any("SeriesMin" in e for e in appbundle.check(self.build("2026"), "2025")))

    def test_wrong_framework_fails(self):
        # Assemblies net8.0 (build 2026) déclarées pour 2027 (.NET 10) : SeriesMax et framework incorrects.
        zip_path = self.build("2026")
        package = zipfile.ZipFile(zip_path).read("EgRoomTracker.bundle/PackageContents.xml").replace(b"R2026", b"R2027")
        rewrite(zip_path, replace={"EgRoomTracker.bundle/PackageContents.xml": package})
        self.assertTrue(any("framework" in e for e in appbundle.check(zip_path, "2027")))

    def test_absolute_assembly_path_in_addin_fails(self):
        zip_path = self.build()
        addin = zipfile.ZipFile(zip_path).read("EgRoomTracker.bundle/Contents/EgRoomTracker.addin")
        addin = addin.replace(rb".\EgRoomTracker.dll", rb"C:\Addins\EgRoomTracker.dll")
        rewrite(zip_path, replace={"EgRoomTracker.bundle/Contents/EgRoomTracker.addin": addin})
        self.assertTrue(any("Assembly" in e for e in appbundle.check(zip_path, "2026")))

    def test_second_root_folder_fails(self):
        zip_path = self.build()
        rewrite(zip_path, add={"Other.bundle/PackageContents.xml": b"<x/>"})
        self.assertTrue(any("dossier racine" in e for e in appbundle.check(zip_path, "2026")))


class DefinitionTests(unittest.TestCase):
    def test_activity_definitions(self):
        for version in appbundle.SUPPORTED_VERSIONS:
            with self.subTest(version=version):
                activity = deploy.load_definition(version, "activity", "nick", "test")
                bundle = deploy.load_definition(version, "appbundle")
                self.assertEqual(f"Autodesk.Revit+{version}", activity["engine"])
                self.assertEqual(activity["engine"], bundle["engine"])
                command = " ".join(activity["commandLine"])
                self.assertNotIn("/i ", command)  # D1 : le document est ouvert par l'add-in
                self.assertIn(f'/al "$(appbundles[{bundle["id"]}].path)"', command)
                self.assertEqual([f"nick.{bundle['id']}+test"], activity["appbundles"])
                params = activity["parameters"]
                self.assertEqual(("get", True, "input"), (params["inputZip"]["verb"], params["inputZip"]["zip"], params["inputZip"]["localName"]))
                self.assertEqual(("get", "settings.json"), (params["settings"]["verb"], params["settings"]["localName"]))
                self.assertEqual(("put", "result.json"), (params["result"]["verb"], params["result"]["localName"]))
                for item_id in (activity["id"], bundle["id"]):
                    self.assertRegex(item_id, r"^[A-Za-z0-9_]{1,40}$")

    def test_local_names_match_activity_contract(self):
        # Noms locaux lus par l'add-in (src/EntryPoint/ActivityContract.cs).
        source = (SCRIPTS.parents[2] / "src" / "EntryPoint" / "ActivityContract.cs").read_text(encoding="utf-8")
        constants = dict(re.findall(r'public const string (\w+) = "([^"]+)";', source))
        params = deploy.load_definition("2026", "activity", "nick", "test")["parameters"]
        self.assertEqual(constants["InputDirectoryName"], params["inputZip"]["localName"])
        self.assertEqual(constants["SettingsFileName"], params["settings"]["localName"])
        self.assertEqual(constants["ResultFileName"], params["result"]["localName"])

    def test_addin_manifest_matches_entry_point(self):
        addin = (SCRIPTS.parent / "bundle" / "EgRoomTracker.addin").read_text(encoding="utf-8")
        self.assertIn('Type="DBApplication"', addin)
        self.assertIn(f"<FullClassName>{appbundle.ENTRY_CLASS}</FullClassName>", addin)
        csproj = (SCRIPTS.parents[1] / "EntryPoint" / "EgRoomTracker.EntryPoint.csproj").read_text(encoding="utf-8")
        self.assertIn("<AssemblyName>EgRoomTracker</AssemblyName>", csproj)
        entry = (SCRIPTS.parents[1] / "EntryPoint" / "AutomationApplication.cs").read_text(encoding="utf-8")
        self.assertIn("namespace EgRoomTracker.EntryPoint;", entry)
        self.assertIn("class AutomationApplication : IExternalDBApplication", entry)

    def test_template_placeholders_must_be_resolved(self):
        with self.assertRaises(deploy.DeployError):
            deploy.load_definition("2026", "activity")


class DeployDryRunTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp, True)
        for version in appbundle.SUPPORTED_VERSIONS:
            (self.tmp / version).mkdir()
            build = fake_build_dir(self.tmp / version, version)
            appbundle.assemble(version, build, self.tmp / "bundles", "0.1.0", False)
        # Garde-fou : toute tentative d'appel réseau fait échouer le test.
        patcher = mock.patch("urllib.request.urlopen", side_effect=AssertionError("appel réseau en dry-run"))
        patcher.start()
        self.addCleanup(patcher.stop)

    def run_main(self, *argv, **env):
        environ = {"APPBUNDLE_DIR": str(self.tmp / "bundles"), **env}
        out, err = io.StringIO(), io.StringIO()
        with mock.patch("sys.stdout", out), mock.patch("sys.stderr", err):
            code = deploy.main(list(argv), environ)
        return code, out.getvalue(), err.getvalue()

    def test_dry_run_is_default_and_never_prints_the_token(self):
        code, out, _ = self.run_main("test", APS_TOKEN="secret-token-value", APS_NICKNAME="nick")
        self.assertEqual(0, code)
        self.assertIn("[dry-run]", out)
        self.assertNotIn("secret-token-value", out)

    def test_first_publication_plan(self):
        http = deploy.DryRunHttp(out=io.StringIO())
        deploy.publish_test(http, "nick", ["2026"], self.tmp / "bundles", log=lambda *_: None)
        calls = [(m, p) for m, p, _ in http.requests]
        self.assertEqual([
            ("GET", "/engines"),
            ("GET", "/appbundles/EgRoomTracker_R2026/aliases"),
            ("POST", "/appbundles"),
            ("UPLOAD", "<endpointURL>"),
            ("POST", "/appbundles/EgRoomTracker_R2026/aliases"),
            ("GET", "/activities/ExtractRooms_R2026/aliases"),
            ("POST", "/activities"),
            ("POST", "/activities/ExtractRooms_R2026/aliases"),
        ], calls)

    def test_update_creates_versions_and_patches_aliases(self):
        http = deploy.DryRunHttp(out=io.StringIO(), simulate_existing=True)
        deploy.publish_test(http, "nick", ["2026"], self.tmp / "bundles", log=lambda *_: None)
        calls = [(m, p) for m, p, _ in http.requests]
        self.assertIn(("POST", "/appbundles/EgRoomTracker_R2026/versions"), calls)
        self.assertIn(("PATCH", "/appbundles/EgRoomTracker_R2026/aliases/test"), calls)
        self.assertIn(("POST", "/activities/ExtractRooms_R2026/versions"), calls)
        self.assertIn(("PATCH", "/activities/ExtractRooms_R2026/aliases/test"), calls)
        version_body = next(b for m, p, b in http.requests if p == "/appbundles/EgRoomTracker_R2026/versions")
        self.assertNotIn("id", version_body)

    def test_promote_points_prod_to_test_and_activity_references_prod_bundle(self):
        http = deploy.DryRunHttp(out=io.StringIO(), simulate_existing=True)
        logs = []
        deploy.promote(http, "nick", ["2026"], log=logs.append)
        bundle_patch = next(b for m, p, b in http.requests if p == "/appbundles/EgRoomTracker_R2026/aliases/prod")
        self.assertEqual({"version": 2}, bundle_patch)
        activity = next(b for m, p, b in http.requests if p == "/activities/ExtractRooms_R2026/versions")
        self.assertEqual(["nick.EgRoomTracker_R2026+prod"], activity["appbundles"])
        self.assertTrue(any("rollback" in line for line in logs))

    def test_rollback_patches_alias(self):
        http = deploy.DryRunHttp(out=io.StringIO())
        deploy.rollback(http, "activity", "2027", "prod", 4, log=lambda *_: None)
        self.assertEqual([("PATCH", "/activities/ExtractRooms_R2027/aliases/prod", {"version": 4})], http.requests)

    def test_invalid_bundle_is_refused_before_any_write(self):
        rewrite(self.tmp / "bundles" / "EgRoomTracker_R2025.zip", add={"EgRoomTracker.bundle/Contents/RevitAPI.dll": b"x"})
        http = deploy.DryRunHttp(out=io.StringIO())
        with self.assertRaises(deploy.DeployError):
            deploy.publish_test(http, "nick", ["2025"], self.tmp / "bundles", log=lambda *_: None)
        self.assertEqual([("GET", "/engines")], [(m, p) for m, p, _ in http.requests])

    def test_apply_requires_token_and_nickname(self):
        code, _, err = self.run_main("--apply", "status", APS_NICKNAME="nick")
        self.assertEqual(2, code)
        self.assertIn("APS_TOKEN", err)

    def test_unsupported_version_is_refused(self):
        code, _, _ = self.run_main("test", REVIT_VERSIONS="2024")
        self.assertEqual(2, code)


if __name__ == "__main__":
    unittest.main()
