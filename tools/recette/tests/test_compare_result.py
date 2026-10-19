"""Tests du comparateur de recette (tools/recette/compare_result.py) et de la cohérence du jeu docs/recette.

Exécution : python3 -m unittest discover -s tools/recette/tests -v   (bibliothèque standard uniquement)
"""
import contextlib
import copy
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

import compare_result as cr  # noqa: E402

ROOT = HERE.parents[2]
RECETTE = ROOT / "docs" / "recette"
MANIFEST = json.loads((RECETTE / "cases.json").read_text(encoding="utf-8"))


def load(case: str) -> dict:
    return json.loads((RECETTE / "expected" / f"{case}.json").read_text(encoding="utf-8"))


def room(result: dict, name: str) -> dict:
    return next(r for r in result["rooms"] if r["nom"] == name)


def fake_actual(template: dict, offset: int = 500000) -> dict:
    """Résultat « réel » simulé : identifiants provisoires remplacés par d'autres, valeurs du gabarit conservées."""
    actual = copy.deepcopy(template)
    mapping = {}
    for r in actual["rooms"]:
        new = str(int(r["id_revit"]) - 9000000 + offset)
        mapping[r["id_revit"]] = new
        r["id_revit"], r["unique_id"] = new, f"5b1e2f8a-{new}"
        for kind in ("niveau", "phase", "variante"):
            if isinstance(r.get(kind), dict):
                r[kind]["id_revit"] = str(int(r[kind]["id_revit"]) - 9000000 + 300)
                if "unique_id" in r[kind]:
                    r[kind]["unique_id"] = f"8d2c4f6a-{r[kind]['id_revit']}"
    for a in actual["anomalies"]:
        if a["elementId"] in mapping:
            a["elementId"] = mapping[a["elementId"]]
        if isinstance(a["details"].get("elementIds"), list):
            a["details"]["elementIds"] = [mapping[i] for i in a["details"]["elementIds"]]
    s = actual["summary"]
    s["correlationId"], s["extractorVersion"] = "run-0f3c", "0.3.0"
    s["revit"]["build"], s["startedAtUtc"], s["completedAtUtc"] = "26.0.4.409", "2026-11-02T08:00:00Z", "2026-11-02T08:00:41Z"
    if s["options"]["phaseIds"]:
        s["options"]["phaseIds"] = [str(int(i) - 9000000 + 300) for i in s["options"]["phaseIds"]]
    return actual


def run_cli(*argv: str) -> tuple[int, str]:
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        code = cr.main(list(argv))
    return code, out.getvalue() + err.getvalue()


class CompareTests(unittest.TestCase):
    def setUp(self):
        self.expected = load("R01")
        self.actual = copy.deepcopy(self.expected)

    def check(self, options=None):
        return cr.compare(self.expected, self.actual, options)

    def test_identical_results_conform(self):
        report = self.check()
        self.assertTrue(report.ok, report.mismatches)

    def test_quantities_within_d5_tolerance_conform(self):
        room(self.actual, "Bureau d'étude")["surface_m2"] = 20.0009
        room(self.actual, "Bureau d'étude")["hauteur_non_limitee_m"] = 2.6995
        self.assertTrue(self.check().ok)

    def test_quantity_beyond_tolerance_is_a_mismatch_with_the_gap(self):
        room(self.actual, "Bureau d'étude")["surface_m2"] = 20.0042
        report = self.check()
        self.assertEqual(1, len(report.mismatches))
        self.assertIn("surface_m2", report.mismatches[0])
        self.assertIn("écart 0.0042", report.mismatches[0])
        self.assertIn("Bureau d'étude", report.mismatches[0])

    def test_tolerances_are_configurable(self):
        room(self.actual, "Bureau d'étude")["surface_m2"] = 20.0042
        options = cr.Options()
        options.tolerances["area"] = 0.01
        self.assertTrue(self.check(options).ok)

    def test_volume_uses_volume_tolerance(self):
        self.expected, self.actual = load("R02"), load("R02")
        room(self.actual, "Bureau d'étude")["volume_m3"] = 54.0009
        self.assertTrue(self.check().ok)
        room(self.actual, "Bureau d'étude")["volume_m3"] = 54.01
        self.assertFalse(self.check().ok)

    def test_shared_numeric_value_uses_shared_tolerance(self):
        room(self.actual, "Bureau d'étude")["parametres_partages"]["longueur"]["valeur"] = 1.2500004
        self.assertTrue(self.check().ok)
        room(self.actual, "Bureau d'étude")["parametres_partages"]["longueur"]["valeur"] = 1.26
        self.assertFalse(self.check().ok)

    def test_other_numbers_are_exact(self):
        self.actual["summary"]["counts"]["roomsExported"] = 12
        self.assertFalse(self.check().ok)

    def test_volatile_fields_are_ignored_by_default_only(self):
        self.actual["summary"]["startedAtUtc"] = "2026-11-02T08:00:00Z"
        self.actual["summary"]["correlationId"] = "run-1"
        self.actual["summary"]["revit"]["build"] = "26.0.4.409"
        self.assertTrue(self.check().ok)
        code, output = self._cli_with_files("--no-default-ignores")
        self.assertEqual(1, code)
        self.assertIn("summary.startedAtUtc", output)

    def _cli_with_files(self, *extra):
        with tempfile.TemporaryDirectory() as tmp:
            e, a = Path(tmp) / "e.json", Path(tmp) / "a.json"
            e.write_text(json.dumps(self.expected, ensure_ascii=False), encoding="utf-8")
            a.write_text(json.dumps(self.actual, ensure_ascii=False), encoding="utf-8")
            return run_cli("compare", str(e), str(a), *extra)

    def test_null_empty_string_and_zero_are_distinct(self):
        room(self.actual, "Local technique")["finitions"]["murs"] = None
        room(self.actual, "Sans attributs")["occupation"] = ""
        room(self.actual, "Salle de réunion « Été »")["parametres_partages"]["entier"]["valeur"] = None
        report = self.check()
        self.assertEqual(3, len(report.mismatches), report.mismatches)

    def test_leading_zeros_matter(self):
        room(self.actual, "Salle de réunion « Été »")["numero"] = "10"
        self.assertFalse(self.check().ok)

    def test_missing_and_unexpected_rooms_are_reported(self):
        removed = self.actual["rooms"].pop(0)
        extra = copy.deepcopy(removed)
        extra["id_revit"], extra["unique_id"] = "9999999", "autre"
        self.actual["rooms"].append(extra)
        report = self.check()
        self.assertTrue(any("pièce attendue absente" in m for m in report.mismatches))
        self.assertTrue(any("pièce inattendue" in m for m in report.mismatches))

    def test_rooms_can_be_matched_by_unique_id(self):
        for r in self.actual["rooms"]:
            r["id_revit"] = str(int(r["id_revit"]) + 1)
        report = self.check(cr.Options(match_by="unique_id"))
        self.assertTrue(all(".id_revit" in m or "elementId=" in m or "anomalie" in m for m in report.mismatches))
        self.assertFalse(any("pièce attendue absente" in m for m in report.mismatches))

    def test_unsorted_rooms_are_reported(self):
        self.actual["rooms"].reverse()
        report = self.check()
        self.assertEqual(["rooms : pièces non triées par id_revit numérique croissant"], report.mismatches)

    def test_quality_flags_order_is_not_significant_but_content_is(self):
        flags = room(self.actual, "Réserve")["indicateurs_qualite"]
        flags.reverse()
        self.assertTrue(self.check().ok)
        flags.pop()
        self.assertFalse(self.check().ok)

    def test_anomalies_are_compared_as_a_multiset(self):
        self.actual["anomalies"].reverse()
        self.assertTrue(self.check().ok)

    def test_missing_extra_and_changed_anomalies(self):
        dup = next(a for a in self.actual["anomalies"] if a["code"] == "DUPLICATE_NUMBER")
        self.actual["anomalies"].remove(dup)
        self.actual["anomalies"].append({"code": "LINK_MISSING", "severity": "warning", "scope": "document", "elementId": None,
                                         "message": "Lien Revit absent : X.rvt.", "details": {"linkName": "X.rvt", "linkPath": None}})
        unplaced = next(a for a in self.actual["anomalies"] if a["code"] == "UNPLACED")
        unplaced["severity"] = "info"
        report = self.check()
        self.assertEqual(3, len(report.mismatches), report.mismatches)
        self.assertTrue(any("anomalie attendue absente" in m for m in report.mismatches))
        self.assertTrue(any("anomalie inattendue" in m for m in report.mismatches))
        self.assertTrue(any(m.startswith("anomalies[code=UNPLACED") and ".severity" in m for m in report.mismatches))

    def test_duplicate_element_ids_order_is_not_significant(self):
        dup = next(a for a in self.actual["anomalies"] if a["code"] == "DUPLICATE_NUMBER")
        dup["details"]["elementIds"].reverse()
        self.assertTrue(self.check().ok)

    def test_messages_are_ignored_unless_requested(self):
        self.actual["anomalies"][0]["message"] = "autre texte"
        self.assertTrue(self.check().ok)
        self.assertFalse(self.check(cr.Options(compare_messages=True)).ok)

    def test_allowed_extra_anomaly_is_a_note_and_tolerates_the_status(self):
        self.expected, self.actual = load("R07"), load("R07")
        self.actual["anomalies"].insert(0, {"code": "REVIT_WARNING_ON_OPEN", "severity": "warning", "scope": "document", "elementId": None,
                                            "message": "1 avertissement(s) Revit toléré(s) à l'ouverture.", "details": {"warningCount": 1}})
        self.actual["summary"]["status"] = "succeededWithWarnings"
        self.assertFalse(self.check().ok)
        report = self.check(cr.Options(allow_extra_anomalies={"REVIT_WARNING_ON_OPEN"}))
        self.assertTrue(report.ok, report.mismatches)
        self.assertEqual(2, len(report.notes))

    def test_status_mismatch_is_reported(self):
        self.actual["summary"]["status"] = "succeeded"
        self.assertEqual(["summary.status : attendu \"succeededWithWarnings\", obtenu \"succeeded\""], self.check().mismatches)

    def test_volume_computation_trace_is_compared(self):
        self.actual["summary"]["volumeComputation"]["computeApplied"] = True
        self.assertEqual(["summary.volumeComputation.computeApplied : attendu false, obtenu true"], self.check().mismatches)
        self.actual["summary"]["volumeComputation"] = None
        self.assertEqual(1, len(self.check().mismatches))

    def test_pending_paths_do_not_fail(self):
        room(self.actual, "Local technique")["finitions"]["murs"] = None
        report = self.check(cr.Options(pending=["rooms[nom=Local technique].finitions.murs"]))
        self.assertTrue(report.ok)
        self.assertEqual(1, len(report.pending))

    def test_ignore_selectors_with_dotted_field_and_wildcard(self):
        for a in self.actual["anomalies"]:
            if a["code"] == "DUPLICATE_NUMBER" and a["details"]["number"] == "011":
                a["severity"] = "error"
        self.assertFalse(self.check().ok)
        self.assertTrue(self.check(cr.Options(ignores=["anomalies[details.number=011]"])).ok)
        room(self.actual, "Placard")["surface_m2"] = 2.0
        self.assertTrue(self.check(cr.Options(ignores=["anomalies[details.number=011]", "rooms[*].surface_m2"])).ok)

    def test_null_selector(self):
        room(self.actual, "Sans attributs")["surface_m2"] = 6.0
        self.assertTrue(self.check(cr.Options(ignores=["rooms[occupation=null,numero=004]"])).ok)

    def test_global_errors_are_compared_by_code_and_stage(self):
        self.expected, self.actual = load("R08"), load("R08")
        self.actual["summary"]["errors"][0]["message"] = "autre"
        self.assertTrue(self.check().ok)
        self.actual["summary"]["errors"][0]["code"] = "RVT_NOT_FOUND"
        report = self.check()
        self.assertEqual(2, len(report.mismatches), report.mismatches)

    def test_failed_result_versus_success_is_reported(self):
        self.actual = load("R10")
        report = self.check()
        self.assertFalse(report.ok)
        self.assertTrue(any("erreur globale inattendue" in m for m in report.mismatches))


class BindTests(unittest.TestCase):
    def test_bind_copies_identifiers_and_keeps_expected_values(self):
        template = load("R01")
        actual = fake_actual(template)
        actual["summary"]["revit"]["version"] = "2025"
        room(actual, "Bureau d'étude")["surface_m2"] = 99.0  # une valeur réelle ne doit jamais être recopiée
        bound, log = cr.bind(template, actual, "2025")
        self.assertEqual("2025", bound["summary"]["revit"]["version"])
        bureau = room(bound, "Bureau d'étude")
        self.assertEqual(room(actual, "Bureau d'étude")["id_revit"], bureau["id_revit"])
        self.assertEqual(room(actual, "Bureau d'étude")["unique_id"], bureau["unique_id"])
        self.assertEqual(20.0, bureau["surface_m2"])
        self.assertEqual(room(actual, "Bureau d'étude")["niveau"], bureau["niveau"])
        dup = next(a for a in bound["anomalies"] if a["code"] == "DUPLICATE_NUMBER" and a["details"]["number"] == "001")
        self.assertEqual(sorted([room(actual, "Bureau d'étude")["id_revit"], room(actual, "Réserve")["id_revit"]], key=int),
                         dup["details"]["elementIds"])
        self.assertTrue(any("phase 'Existant'" in line for line in log))
        actual_ok = copy.deepcopy(actual)
        room(actual_ok, "Bureau d'étude")["surface_m2"] = 20.0
        self.assertTrue(cr.compare(bound, actual_ok).ok)
        actual_ok["summary"]["revit"]["version"] = "2027"
        self.assertEqual(['summary.revit.version : attendu "2025", obtenu "2027"'], cr.compare(bound, actual_ok).mismatches)

    def test_bind_maps_phase_ids_of_the_options(self):
        template = load("R03")
        actual = fake_actual(template)
        bound, _ = cr.bind(template, actual, "2026")
        self.assertEqual(actual["summary"]["options"]["phaseIds"], bound["summary"]["options"]["phaseIds"])
        self.assertTrue(cr.compare(bound, actual).ok)

    def test_bind_refuses_missing_or_ambiguous_rooms(self):
        template = load("R01")
        actual = fake_actual(template)
        actual["rooms"] = [r for r in actual["rooms"] if r["nom"] != "Couloir"]
        with self.assertRaises(cr.BindError) as ctx:
            cr.bind(template, actual, "2027")
        self.assertIn("Couloir", str(ctx.exception))

    def test_bind_cli_writes_the_file(self):
        template = load("R07")
        with tempfile.TemporaryDirectory() as tmp:
            actual_path, out = Path(tmp) / "actual.json", Path(tmp) / "2026" / "R07.json"
            actual_path.write_text(json.dumps(fake_actual(template), ensure_ascii=False), encoding="utf-8")
            code, output = run_cli("bind", str(RECETTE / "expected" / "R07.json"), str(actual_path), "--year", "2026", "-o", str(out))
            self.assertEqual(0, code, output)
            code, output = run_cli("compare", "--case", "R07", str(out), str(actual_path))
            self.assertEqual(0, code, output)
            self.assertIn("CONFORME", output)


class CliTests(unittest.TestCase):
    def test_exit_codes(self):
        template = load("R01")
        with tempfile.TemporaryDirectory() as tmp:
            actual = fake_actual(template)
            bound, _ = cr.bind(template, actual, "2026")
            e, a = Path(tmp) / "e.json", Path(tmp) / "a.json"
            e.write_text(json.dumps(bound, ensure_ascii=False), encoding="utf-8")
            a.write_text(json.dumps(actual, ensure_ascii=False), encoding="utf-8")
            self.assertEqual(0, run_cli("compare", "--case", "R01", str(e), str(a))[0])
            room(actual, "Bureau d'étude")["surface_m2"] = 20.5
            room(actual, "Local technique")["finitions"]["murs"] = None
            a.write_text(json.dumps(actual, ensure_ascii=False), encoding="utf-8")
            code, output = run_cli("compare", "--case", "R01", str(e), str(a))
            self.assertEqual(1, code)
            self.assertIn("ÉCART        rooms[", output)
            self.assertIn("À CONFIRMER  rooms[", output)
            self.assertIn("NON CONFORME : 1 écart(s), 1 point(s) à confirmer.", output)
            code, output = run_cli("compare", str(e), str(Path(tmp) / "absent.json"))
            self.assertEqual(2, code)
            code, output = run_cli("compare", "--case", "R99", str(e), str(a))
            self.assertEqual(2, code)


class ManifestTests(unittest.TestCase):
    """Cohérence du jeu versionné : fichiers présents, options conformes aux settings, motifs valides."""

    def all_paths(self, value, path=()):
        yield list(path)
        if isinstance(value, dict):
            for k, v in value.items():
                yield from self.all_paths(v, path + (k,))
        elif isinstance(value, list):
            for i, v in enumerate(value):
                yield from self.all_paths(v, path + (cr.Item(str(path[-1]) if path else "", v, str(i)),))

    def test_every_case_has_its_files_and_years(self):
        ids = [c["id"] for c in MANIFEST["cases"]]
        self.assertEqual(sorted(ids), ids)
        self.assertEqual(len(ids), len(set(ids)))
        for case in MANIFEST["cases"]:
            with self.subTest(case=case["id"]):
                self.assertTrue((RECETTE / case["settings"]).is_file())
                self.assertTrue((RECETTE / case["expected"]).is_file())
                self.assertTrue(set(case["years"]) <= {"2025", "2026", "2027"})
                self.assertTrue(case["zip"])
                # Années non applicables : hors de "years", avec un motif (R09 en 2027).
                for year, reason in case.get("notApplicable", {}).items():
                    self.assertNotIn(year, case["years"])
                    self.assertTrue(reason)
                self.assertIsInstance(case.get("optional", False), bool)
        self.assertEqual({"2027"}, set(next(c for c in MANIFEST["cases"] if c["id"] == "R09")["notApplicable"]))

    def test_duplicate_numbers_follow_the_context_rule(self):
        """Règle du lead (EGR-18) : doublon si même phase et même numéro non vide, sauf entre deux variantes
        différentes d'un MÊME jeu d'options ; des variantes de jeux différents sont comparées ; une pièce du modèle
        principal est comparée à tout. Implémentée (PR #45) : plus aucun cas marqué pendingImplementation."""
        set_of = {o: name for name, options in MANIFEST["designOptionSets"]["sets"].items() for o in options}

        def option(r):
            return (r["variante"] or {}).get("id_revit")

        def conflict(a, b):
            exclusive = (option(a) is not None and option(b) is not None and option(a) != option(b)
                         and set_of[option(a)] == set_of[option(b)])
            return (a is not b and bool(a["numero"]) and a["numero"] == b["numero"]
                    and (a["phase"] or {}).get("id_revit") == (b["phase"] or {}).get("id_revit")
                    and not exclusive)

        checked = set()
        for case in MANIFEST["cases"]:
            expected = json.loads((RECETTE / case["expected"]).read_text(encoding="utf-8"))
            rooms = expected["rooms"]
            duplicates = {a["elementId"]: a for a in expected["anomalies"] if a["code"] == "DUPLICATE_NUMBER"}
            with self.subTest(case=case["id"]):
                for r in rooms:
                    group = sorted([r["id_revit"]] + [o["id_revit"] for o in rooms if conflict(r, o)], key=int)
                    if len(group) > 1:
                        self.assertEqual(group, duplicates[r["id_revit"]]["details"]["elementIds"])
                        self.assertIn("DUPLICATE_NUMBER", r["indicateurs_qualite"])
                    else:
                        self.assertNotIn(r["id_revit"], duplicates)
                        self.assertNotIn("DUPLICATE_NUMBER", r["indicateurs_qualite"])
                names = {r["nom"]: r for r in rooms}
                if {"Bureau variante A", "Bureau variante B", "Accueil commun"} <= set(names):
                    checked.add(case["id"])
                    self.assertNotIn("DUPLICATE_NUMBER", names["Bureau variante A"]["indicateurs_qualite"])
                    self.assertNotIn("DUPLICATE_NUMBER", names["Bureau variante B"]["indicateurs_qualite"])
                    self.assertIn("DUPLICATE_NUMBER", names["Accueil commun"]["indicateurs_qualite"])
                    # Variantes de deux jeux d'options différents (Option A / Option C) : comparées, donc doublon.
                    a2, c = names["Bureau variante A2"], names["Salle aménagement C"]
                    self.assertNotEqual(set_of[a2["variante"]["id_revit"]], set_of[c["variante"]["id_revit"]])
                    self.assertIn("DUPLICATE_NUMBER", a2["indicateurs_qualite"])
                    self.assertIn("DUPLICATE_NUMBER", c["indicateurs_qualite"])
                    self.assertNotIn("pendingImplementation", case)
        self.assertEqual({"R01", "R02", "R06", "R11"}, checked)

    def test_options_reflect_the_settings_when_they_are_loaded(self):
        for case in MANIFEST["cases"]:
            settings = json.loads((RECETTE / case["settings"]).read_text(encoding="utf-8"))
            expected = json.loads((RECETTE / case["expected"]).read_text(encoding="utf-8"))
            error = (expected["summary"]["errors"] or [{}])[0].get("code")
            options = expected["summary"]["options"]
            with self.subTest(case=case["id"]):
                if error in ("RVT_AMBIGUOUS", "CONFIG_SCHEMA_UNSUPPORTED"):
                    # Configuration non chargée : options par défaut (RoomExtractionService.BuildResult).
                    self.assertEqual({"phaseIds": None, "volumeMode": "preserve", "sharedParameterKeys": []}, options)
                else:
                    self.assertEqual(settings["phaseIds"], options["phaseIds"])
                    self.assertEqual(settings["volumeMode"], options["volumeMode"])
                    self.assertEqual([m["key"] for m in settings["sharedParameters"]], options["sharedParameterKeys"])
                    for r in expected["rooms"]:
                        self.assertEqual(options["sharedParameterKeys"], list(r["parametres_partages"]))

    def test_every_pending_and_ignore_pattern_targets_the_expected_file(self):
        for case in MANIFEST["cases"]:
            expected = json.loads((RECETTE / case["expected"]).read_text(encoding="utf-8"))
            paths = list(self.all_paths(expected))
            for text in case.get("pending", []) + case.get("ignore", []):
                with self.subTest(case=case["id"], pattern=text):
                    pattern = cr.parse_pattern(text)
                    self.assertTrue(any(cr.path_matches(pattern, p) for p in paths), f"{text} ne désigne rien dans {case['expected']}")

    def test_template_identifiers_are_placeholders(self):
        for case in MANIFEST["cases"]:
            expected = json.loads((RECETTE / case["expected"]).read_text(encoding="utf-8"))
            for r in expected["rooms"]:
                with self.subTest(case=case["id"], room=r["nom"]):
                    self.assertTrue(r["unique_id"].startswith(cr.PLACEHOLDER_PREFIX))
                    self.assertTrue(r["id_revit"].startswith("900"))

    def test_shared_parameter_file_declares_the_mapped_guids(self):
        text = (RECETTE / "EGR_Recette_SharedParameters.txt").read_text(encoding="utf-8")
        declared = {line.split("\t")[1]: line.split("\t")[2] for line in text.splitlines() if line.startswith("PARAM\t")}
        for case in MANIFEST["cases"]:
            settings = json.loads((RECETTE / case["settings"]).read_text(encoding="utf-8"))
            for mapping in settings["sharedParameters"]:
                with self.subTest(case=case["id"], key=mapping["key"]):
                    if mapping["key"] == "inconnu":
                        self.assertNotIn(mapping["guid"], declared)  # GUID volontairement absent (SHARED_PARAMETER_NOT_FOUND)
                    else:
                        self.assertIn(mapping["guid"], declared)


if __name__ == "__main__":
    unittest.main()
