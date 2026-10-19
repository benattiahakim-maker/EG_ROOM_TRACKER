#!/usr/bin/env python3
"""Recette EG_ROOM_TRACKER (EGR-18 / EGR-14) : comparaison d'un result.json réel à un résultat attendu.

Commandes :
  compare  ATTENDU REEL   compare un result.json produit par un WorkItem à un fichier attendu ;
                          code de sortie 0 = conforme, 1 = écart(s), 2 = erreur d'usage ou de fichier.
  bind     GABARIT REEL --year AAAA -o SORTIE
                          produit le fichier attendu d'une année à partir d'un gabarit (identifiants provisoires
                          9xxxxxx / EGR18-…) : les pièces sont appariées par nom, numéro, phase et variante, et seuls
                          les identifiants (pièces, niveaux, phases, variantes) sont recopiés depuis le résultat réel.
                          Les valeurs attendues (textes, quantités, indicateurs, anomalies) restent celles du gabarit.

Règles de comparaison (docs/RECETTE.md) :
  - pièces appariées par id_revit (défaut) ou unique_id ; pièces manquantes ou en trop signalées ;
  - quantités comparées avec les tolérances D5 (0,001 m, m², m³ par défaut, configurables), autres nombres exacts ;
  - champs volatils ignorés par défaut (correlationId, extractorVersion, revit.build, apsEngine, dates) ;
  - anomalies comparées comme un multiensemble (code, portée, elementId, discriminant de détail) ; messages
    ignorés sauf --compare-messages ; ordre des anomalies non vérifié, ordre des pièces vérifié (tri par id_revit) ;
  - chemins « à confirmer » (--pending) : un écart y est signalé « À CONFIRMER » sans faire échouer.

Chemins (--ignore, --pending) : segments séparés par des points ; pour une liste, `rooms[*]` (toutes) ou
`rooms[champ=valeur]` (ex. `rooms[nom=Réserve].niveau`, champ éventuellement pointé : `anomalies[details.number=010]`, valeur `null` acceptée,
plusieurs conditions séparées par des virgules). Un chemin couvre aussi ses sous-chemins.

Bibliothèque standard uniquement (Python 3.10+).
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from dataclasses import dataclass, field
from pathlib import Path

DEFAULT_TOLERANCES = {"length": 0.001, "area": 0.001, "volume": 0.001, "shared": 0.001}
DEFAULT_IGNORES = (
    "summary.correlationId",
    "summary.extractorVersion",
    "summary.revit.build",
    "summary.apsEngine",
    "summary.startedAtUtc",
    "summary.completedAtUtc",
)
QUANTITY_FIELDS = {"surface_m2": "area", "hauteur_non_limitee_m": "length", "volume_m3": "volume"}
ANOMALY_DISCRIMINANTS = ("linkName", "key", "parameter", "number")
PLACEHOLDER_PREFIX = "EGR18-"


# --------------------------------------------------------------------------------------------- chemins

@dataclass(frozen=True)
class Item:
    """Élément de liste dans un chemin de diff : objet de référence (attendu ou réel) et libellé lisible."""

    name: str
    obj: object
    label: str


def parse_pattern(text: str) -> list[tuple[str, object]]:
    """`rooms[nom=Réserve].niveau` → [("rooms", None), ("[]", {"nom": "Réserve"}), ("niveau", None)]."""
    segments: list[tuple[str, object]] = []
    for raw in split_top(text):
        name, selector = raw, None
        if "[" in raw and raw.endswith("]"):
            name, selector = raw[: raw.index("[")], raw[raw.index("[") + 1 : -1]
        if name:
            segments.append((name, None))
        if selector is not None:
            if selector == "*":
                segments.append(("[]", {}))
            else:
                conditions = {}
                for condition in selector.split(","):
                    key, _, value = condition.partition("=")
                    conditions[key.strip()] = value.strip()
                segments.append(("[]", conditions))
    return segments


def split_top(text: str) -> list[str]:
    parts, depth, current = [], 0, ""
    for char in text:
        if char == "[":
            depth += 1
        elif char == "]":
            depth -= 1
        if char == "." and depth == 0:
            parts.append(current)
            current = ""
        else:
            current += char
    parts.append(current)
    return [p for p in parts if p]


def lookup(obj: object, dotted: str) -> object:
    for key in dotted.split("."):
        if not isinstance(obj, dict) or key not in obj:
            return _MISSING
        obj = obj[key]
    return obj


_MISSING = object()


def condition_holds(obj: object, key: str, wanted: str) -> bool:
    value = lookup(obj, key)
    if value is _MISSING:
        return False
    if value is None:
        return wanted == "null"
    if isinstance(value, bool):
        return wanted == str(value).lower()
    if isinstance(value, (int, float)):
        try:
            return float(wanted) == float(value)
        except ValueError:
            return False
    return str(value) == wanted


def path_matches(pattern: list[tuple[str, object]], path: list) -> bool:
    """Le motif couvre le chemin s'il en est un préfixe (segments de liste filtrés par leurs conditions)."""
    if len(pattern) > len(path):
        return False
    for (kind, conditions), step in zip(pattern, path):
        if kind == "[]":
            if not isinstance(step, Item):
                return False
            if not all(condition_holds(step.obj, k, v) for k, v in conditions.items()):
                return False
        elif isinstance(step, Item) or step != kind:
            return False
    return True


def render(path: list) -> str:
    out = ""
    for step in path:
        if isinstance(step, Item):
            out += f"[{step.label}]"
        else:
            out += ("." if out else "") + str(step)
    return out


# --------------------------------------------------------------------------------------------- comparaison

@dataclass
class Options:
    tolerances: dict = field(default_factory=lambda: dict(DEFAULT_TOLERANCES))
    ignores: list = field(default_factory=lambda: list(DEFAULT_IGNORES))
    pending: list = field(default_factory=list)
    match_by: str = "id_revit"
    compare_messages: bool = False
    allow_extra_anomalies: set = field(default_factory=set)


@dataclass
class Report:
    mismatches: list = field(default_factory=list)
    pending: list = field(default_factory=list)
    notes: list = field(default_factory=list)

    @property
    def ok(self) -> bool:
        return not self.mismatches


class Comparator:
    def __init__(self, options: Options):
        self.o = options
        self.ignores = [parse_pattern(p) for p in options.ignores]
        self.pendings = [parse_pattern(p) for p in options.pending]
        self.report = Report()

    # Enregistrement d'un écart, selon les motifs ignorés et « à confirmer ».
    def diff(self, path: list, message: str) -> None:
        if any(path_matches(p, path) for p in self.ignores):
            return
        line = f"{render(path)} : {message}"
        if any(path_matches(p, path) for p in self.pendings):
            self.report.pending.append(line)
        else:
            self.report.mismatches.append(line)

    def ignored(self, path: list) -> bool:
        return any(path_matches(p, path) for p in self.ignores)

    def compare(self, expected: dict, actual: dict) -> Report:
        for key in sorted(set(expected) | set(actual)):
            if key in ("summary", "rooms", "anomalies"):
                continue
            self.values([key], expected.get(key, _MISSING), actual.get(key, _MISSING))
        self.summary(expected.get("summary") or {}, actual.get("summary") or {})
        self.rooms(expected.get("rooms") or [], actual.get("rooms") or [])
        self.anomalies(expected.get("anomalies") or [], actual.get("anomalies") or [], expected, actual)
        return self.report

    def summary(self, expected: dict, actual: dict) -> None:
        for key in sorted(set(expected) | set(actual)):
            path = ["summary", key]
            if key == "errors":
                self.errors(expected.get(key) or [], actual.get(key) or [])
            elif key == "status":
                continue  # comparé après les anomalies (tolérance des anomalies supplémentaires admises)
            else:
                self.values(path, expected.get(key, _MISSING), actual.get(key, _MISSING))

    def errors(self, expected: list, actual: list) -> None:
        def key(e):
            return (e.get("code"), e.get("stage"))

        remaining = list(actual)
        for error in expected:
            match = next((a for a in remaining if key(a) == key(error)), None)
            label = Item("errors", error, f"code={error.get('code')}")
            if match is None:
                self.diff(["summary", "errors", label], f"erreur globale attendue absente ({error.get('code')}, étape {error.get('stage')})")
                continue
            remaining.remove(match)
            if self.o.compare_messages and match.get("message") != error.get("message"):
                self.diff(["summary", "errors", label, "message"], f"attendu {fmt(error.get('message'))}, obtenu {fmt(match.get('message'))}")
        for extra in remaining:
            self.diff(["summary", "errors", Item("errors", extra, f"code={extra.get('code')}")],
                      f"erreur globale inattendue ({extra.get('code')}, étape {extra.get('stage')}) : {extra.get('message')}")

    def room_label(self, room: dict) -> str:
        return f"{room.get('id_revit')} « {room.get('nom')} » n°{fmt(room.get('numero'))}"

    def rooms(self, expected: list, actual: list) -> None:
        key = self.o.match_by
        index = {}
        for room in actual:
            index.setdefault(room.get(key), []).append(room)
        ids = [r.get("id_revit") for r in actual]
        if ids != sorted(ids, key=numeric_key):
            self.diff(["rooms"], "pièces non triées par id_revit numérique croissant")
        seen = set()
        for room in expected:
            item = Item("rooms", room, self.room_label(room))
            candidates = index.get(room.get(key), [])
            if not candidates:
                self.diff(["rooms", item], f"pièce attendue absente ({key} {room.get(key)})")
                continue
            if len(candidates) > 1:
                self.diff(["rooms", item], f"{key} {room.get(key)} présent {len(candidates)} fois dans le résultat")
            seen.add(room.get(key))
            self.room(item, room, candidates[0])
        for room in actual:
            if room.get(key) not in seen:
                self.diff(["rooms", Item("rooms", room, self.room_label(room))], "pièce inattendue (absente du résultat attendu)")

    def room(self, item: Item, expected: dict, actual: dict) -> None:
        for name in sorted(set(expected) | set(actual)):
            path = ["rooms", item, name]
            e, a = expected.get(name, _MISSING), actual.get(name, _MISSING)
            if name in QUANTITY_FIELDS:
                self.number(path, e, a, self.o.tolerances[QUANTITY_FIELDS[name]])
            elif name == "indicateurs_qualite":
                if isinstance(e, list) and isinstance(a, list) and sorted(e) != sorted(a):
                    self.diff(path, f"attendu {fmt(sorted(e))}, obtenu {fmt(sorted(a))}")
                elif not (isinstance(e, list) and isinstance(a, list)):
                    self.values(path, e, a)
            elif name == "parametres_partages" and isinstance(e, dict) and isinstance(a, dict):
                for key in sorted(set(e) | set(a)):
                    ev, av = e.get(key, _MISSING), a.get(key, _MISSING)
                    if ev is _MISSING or av is _MISSING or not isinstance(ev, dict) or not isinstance(av, dict):
                        self.values(path + [key], ev, av)
                        continue
                    self.values(path + [key, "guid"], ev.get("guid"), av.get("guid"))
                    self.number(path + [key, "valeur"], ev.get("valeur", _MISSING), av.get("valeur", _MISSING), self.o.tolerances["shared"])
            else:
                self.values(path, e, a)

    def anomalies(self, expected: list, actual: list, expected_doc: dict, actual_doc: dict) -> None:
        remaining = list(actual)
        for anomaly in expected:
            item = Item("anomalies", anomaly, anomaly_label(anomaly))
            match = next((a for a in remaining if anomaly_key(a) == anomaly_key(anomaly)), None)
            if match is None:
                self.diff(["anomalies", item], "anomalie attendue absente")
                continue
            remaining.remove(match)
            for name in ("severity", "scope"):
                self.values(["anomalies", item, name], anomaly.get(name), match.get(name))
            if self.o.compare_messages:
                self.values(["anomalies", item, "message"], anomaly.get("message"), match.get("message"))
            self.values(["anomalies", item, "details"], normalize_details(anomaly.get("details")), normalize_details(match.get("details")))
        tolerated_warning = False
        for extra in remaining:
            item = Item("anomalies", extra, anomaly_label(extra))
            if extra.get("code") in self.o.allow_extra_anomalies:
                self.report.notes.append(f"{render(['anomalies', item])} : anomalie supplémentaire admise ({extra.get('message')})")
                tolerated_warning |= extra.get("severity") in ("warning", "error")
                continue
            self.diff(["anomalies", item], f"anomalie inattendue : {extra.get('message')}")
        e_status = (expected_doc.get("summary") or {}).get("status")
        a_status = (actual_doc.get("summary") or {}).get("status")
        if e_status != a_status:
            if tolerated_warning and e_status == "succeeded" and a_status == "succeededWithWarnings":
                self.report.notes.append("summary.status : succeededWithWarnings dû aux seules anomalies supplémentaires admises")
            else:
                self.diff(["summary", "status"], f"attendu {fmt(e_status)}, obtenu {fmt(a_status)}")

    # Valeurs génériques (récursif), nombres exacts hors champs à tolérance.
    def values(self, path: list, expected: object, actual: object) -> None:
        if self.ignored(path):
            return
        if expected is _MISSING or actual is _MISSING:
            if expected is not actual:
                self.diff(path, "champ absent du résultat" if actual is _MISSING else f"champ inattendu (valeur {fmt(actual)})")
            return
        if isinstance(expected, dict) and isinstance(actual, dict):
            for key in sorted(set(expected) | set(actual)):
                self.values(path + [key], expected.get(key, _MISSING), actual.get(key, _MISSING))
            return
        if isinstance(expected, list) and isinstance(actual, list):
            if len(expected) != len(actual):
                self.diff(path, f"attendu {fmt(expected)}, obtenu {fmt(actual)}")
                return
            for i, (e, a) in enumerate(zip(expected, actual)):
                self.values(path + [Item(str(path[-1]), e, str(i))], e, a)
            return
        if is_number(expected) and is_number(actual):
            self.number(path, expected, actual, 0.0)
            return
        if expected != actual or type(expected) is not type(actual):
            self.diff(path, f"attendu {fmt(expected)}, obtenu {fmt(actual)}")

    def number(self, path: list, expected: object, actual: object, tolerance: float) -> None:
        if self.ignored(path):
            return
        if not (is_number(expected) and is_number(actual)):
            if is_number(expected) or is_number(actual):
                self.diff(path, f"attendu {fmt(expected)}, obtenu {fmt(actual)}")
            else:
                self.values(path, expected, actual)
            return
        gap = abs(float(expected) - float(actual))
        if not math.isfinite(gap) or gap > tolerance + 1e-12:
            limit = f" > tolérance {tolerance:g}" if tolerance else ""
            self.diff(path, f"attendu {fmt(expected)}, obtenu {fmt(actual)} (écart {gap:.6g}{limit})")


def is_number(value: object) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def numeric_key(value: object) -> tuple:
    try:
        return (0, int(str(value)))
    except ValueError:
        return (1, str(value))


def fmt(value: object) -> str:
    if value is _MISSING:
        return "(absent)"
    return json.dumps(value, ensure_ascii=False)


def anomaly_key(anomaly: dict) -> tuple:
    details = anomaly.get("details") or {}
    discriminant = next((f"{k}={details[k]}" for k in ANOMALY_DISCRIMINANTS if k in details), "")
    return (anomaly.get("code"), anomaly.get("scope"), anomaly.get("elementId"), discriminant)


def anomaly_label(anomaly: dict) -> str:
    code, _, element_id, discriminant = anomaly_key(anomaly)
    parts = [f"code={code}"]
    if element_id is not None:
        parts.append(f"elementId={element_id}")
    if discriminant:
        parts.append(discriminant)
    return ",".join(parts)


def normalize_details(details: object) -> object:
    """Listes d'elementIds comparées sans tenir compte de l'ordre."""
    if isinstance(details, dict):
        return {k: (sorted(v, key=numeric_key) if k == "elementIds" and isinstance(v, list) else v) for k, v in details.items()}
    return details


def compare(expected: dict, actual: dict, options: Options | None = None) -> Report:
    return Comparator(options or Options()).compare(expected, actual)


# --------------------------------------------------------------------------------------------- bind

class BindError(Exception):
    pass


def natural_key(room: dict) -> tuple:
    return (
        room.get("nom"),
        room.get("numero"),
        (room.get("phase") or {}).get("nom"),
        (room.get("variante") or {}).get("nom"),
    )


def bind(template: dict, actual: dict, year: str) -> tuple[dict, list[str]]:
    """Recopie dans le gabarit les identifiants du résultat réel ; renvoie (résultat attendu lié, journal)."""
    bound = json.loads(json.dumps(template))
    actual_rooms = actual.get("rooms") or []
    by_key: dict[tuple, list[dict]] = {}
    for room in actual_rooms:
        by_key.setdefault(natural_key(room), []).append(room)

    problems, room_ids = [], {}
    refs: dict[str, dict[str, dict]] = {"niveau": {}, "phase": {}, "variante": {}}
    for room in actual_rooms:
        for kind in refs:
            if isinstance(room.get(kind), dict):
                refs[kind][room[kind]["nom"]] = room[kind]

    template_ref_ids: dict[str, str] = {}
    for room in bound.get("rooms") or []:
        matches = by_key.get(natural_key(room), [])
        if len(matches) != 1:
            problems.append(f"pièce {natural_key(room)} : {len(matches)} correspondance(s) dans le résultat réel")
            continue
        real = matches[0]
        room_ids[room["id_revit"]] = real["id_revit"]
        room["id_revit"], room["unique_id"] = real["id_revit"], real["unique_id"]
        for kind in refs:
            ref = room.get(kind)
            if isinstance(ref, dict):
                real_ref = refs[kind].get(ref["nom"])
                if real_ref is None:
                    problems.append(f"{kind} « {ref['nom']} » absent du résultat réel")
                    continue
                template_ref_ids[ref["id_revit"]] = real_ref["id_revit"]
                room[kind] = dict(ref, **{k: real_ref[k] for k in ref if k in real_ref})
    if problems:
        raise BindError("; ".join(problems))

    for anomaly in bound.get("anomalies") or []:
        if anomaly.get("elementId") in room_ids:
            anomaly["elementId"] = room_ids[anomaly["elementId"]]
        details = anomaly.get("details") or {}
        if isinstance(details.get("elementIds"), list):
            details["elementIds"] = sorted((room_ids.get(i, i) for i in details["elementIds"]), key=numeric_key)

    summary = bound.get("summary") or {}
    options = summary.get("options") or {}
    if isinstance(options.get("phaseIds"), list):
        options["phaseIds"] = [template_ref_ids.get(i, i) for i in options["phaseIds"]]
    if isinstance(summary.get("revit"), dict):
        summary["revit"]["version"] = str(year)
    bound["rooms"] = sorted(bound.get("rooms") or [], key=lambda r: numeric_key(r.get("id_revit")))

    log = [f"pièce {r['nom']!r} : {r['id_revit']} ({r['unique_id']})" for r in bound.get("rooms") or []]
    for kind, names in refs.items():
        for name, ref in sorted(names.items()):
            log.append(f"{kind} {name!r} : {ref['id_revit']}")
    leftovers = [r["unique_id"] for r in bound.get("rooms") or [] if str(r.get("unique_id", "")).startswith(PLACEHOLDER_PREFIX)]
    if leftovers:
        raise BindError(f"identifiants provisoires restants : {', '.join(leftovers)}")
    return bound, log


# --------------------------------------------------------------------------------------------- CLI

def load_json(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as ex:
        raise SystemExit(f"ERREUR : lecture de {path} impossible : {ex}") from None


def case_options(manifest_path: Path, case_id: str) -> tuple[dict, dict]:
    manifest = load_json(manifest_path)
    case = next((c for c in manifest.get("cases", []) if c.get("id") == case_id), None)
    if case is None:
        raise SystemExit(f"ERREUR : cas {case_id} absent de {manifest_path}")
    return manifest, case


def build_options(args: argparse.Namespace) -> Options:
    options = Options(match_by=args.match_by, compare_messages=args.compare_messages)
    if args.no_default_ignores:
        options.ignores = []
    if args.case:
        manifest, case = case_options(Path(args.manifest), args.case)
        options.tolerances.update(manifest.get("tolerances", {}))
        manifest_ignores = [] if args.no_default_ignores else manifest.get("ignore", [])
        options.ignores += [p for p in manifest_ignores + case.get("ignore", []) if p not in options.ignores]
        options.pending += case.get("pending", [])
        options.allow_extra_anomalies |= set(case.get("allowExtraAnomalies", []))
    for name in ("length", "area", "volume", "shared"):
        value = getattr(args, f"tol_{name}")
        if value is not None:
            options.tolerances[name] = value
    options.ignores += args.ignore
    options.pending += args.pending
    options.allow_extra_anomalies |= set(args.allow_extra_anomaly)
    return options


def print_report(report: Report, out=None) -> None:
    out = out or sys.stdout
    for line in report.mismatches:
        print(f"ÉCART        {line}", file=out)
    for line in report.pending:
        print(f"À CONFIRMER  {line}", file=out)
    for line in report.notes:
        print(f"NOTE         {line}", file=out)
    verdict = "CONFORME" if report.ok else "NON CONFORME"
    print(f"{verdict} : {len(report.mismatches)} écart(s), {len(report.pending)} point(s) à confirmer.", file=out)


def main(argv: list[str] | None = None) -> int:
    default_manifest = Path(__file__).resolve().parents[2] / "docs" / "recette" / "cases.json"
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)

    c = sub.add_parser("compare", help="compare un result.json réel à un résultat attendu")
    c.add_argument("expected", type=Path)
    c.add_argument("actual", type=Path)
    c.add_argument("--case", help="identifiant du cas (R01…) : applique ignore/pending/anomalies admises du manifeste")
    c.add_argument("--manifest", default=str(default_manifest))
    c.add_argument("--match-by", choices=("id_revit", "unique_id"), default="id_revit")
    c.add_argument("--tol-length", type=float, help="tolérance en m (défaut 0.001)")
    c.add_argument("--tol-area", type=float, help="tolérance en m² (défaut 0.001)")
    c.add_argument("--tol-volume", type=float, help="tolérance en m³ (défaut 0.001)")
    c.add_argument("--tol-shared", type=float, help="tolérance des valeurs numériques de paramètres partagés (défaut 0.001)")
    c.add_argument("--ignore", action="append", default=[], help="chemin ignoré (répétable)")
    c.add_argument("--pending", action="append", default=[], help="chemin à confirmer, sans échec (répétable)")
    c.add_argument("--allow-extra-anomaly", action="append", default=[], help="code d'anomalie supplémentaire admis (répétable)")
    c.add_argument("--no-default-ignores", action="store_true", help="compare aussi les champs volatils")
    c.add_argument("--compare-messages", action="store_true", help="compare aussi les messages (anomalies, erreurs)")

    b = sub.add_parser("bind", help="lie un gabarit attendu aux identifiants d'un résultat réel")
    b.add_argument("template", type=Path)
    b.add_argument("actual", type=Path)
    b.add_argument("--year", required=True, choices=("2025", "2026", "2027"))
    b.add_argument("-o", "--output", type=Path, required=True)

    args = parser.parse_args(argv)
    try:
        if args.command == "compare":
            report = compare(load_json(args.expected), load_json(args.actual), build_options(args))
            print_report(report)
            return 0 if report.ok else 1
        bound, log = bind(load_json(args.template), load_json(args.actual), args.year)
    except SystemExit as ex:
        print(ex, file=sys.stderr)
        return 2
    except BindError as ex:
        print(f"ERREUR : liaison impossible : {ex}", file=sys.stderr)
        return 2
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(bound, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for line in log:
        print(line)
    print(f"Résultat attendu lié écrit : {args.output} (à relire avant commit)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
