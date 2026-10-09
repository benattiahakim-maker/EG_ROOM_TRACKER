#!/usr/bin/env python3
"""Publication des AppBundles et Activities EG_ROOM_TRACKER sur l'Automation API (EGR-13).

PAR DÉFAUT, AUCUN APPEL RÉSEAU : le script affiche le plan (dry-run). Ajouter --apply pour exécuter.

Commandes :
  test      publie une nouvelle version de chaque AppBundle (ZIP contrôlé) et de chaque Activity,
            puis pointe l'alias « test » sur ces versions.
  promote   pointe l'alias « prod » de chaque AppBundle sur la version actuellement en « test », crée une
            version d'Activity qui référence l'AppBundle « +prod » et pointe l'alias « prod » de l'Activity dessus.
            Les versions « prod » précédentes sont affichées : elles servent au retour arrière.
  rollback  repointe un alias (prod par défaut) sur une version antérieure :
            --kind appbundle|activity --revit 2026 --to-version N [--alias prod]
  status    liste les alias des AppBundles et Activities (lecture seule).

Variables d'environnement :
  APS_TOKEN         jeton 2-legged (scope code:all) ; obligatoire avec --apply ; jamais affiché.
  APS_NICKNAME      nickname de l'application APS ; obligatoire avec --apply.
  APS_DA_BASE_URL   défaut https://developer.api.autodesk.com/da/us-east/v3
  REVIT_VERSIONS    défaut « 2025 2026 2027 »
  APPBUNDLE_DIR     défaut artifacts/appbundles (sortie de build-appbundle.sh)

Le dry-run simule une première publication (AppBundles et Activities inexistants) avec des numéros de
version fictifs ; avec --apply, l'état réel est lu (GET …/aliases) avant toute écriture.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.error
import urllib.request
import uuid
from dataclasses import dataclass
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
PACKAGING_DIR = SCRIPT_DIR.parent
REPO_ROOT = PACKAGING_DIR.parent.parent
DEFAULT_BASE_URL = "https://developer.api.autodesk.com/da/us-east/v3"
SUPPORTED_VERSIONS = ("2025", "2026", "2027")
TEST_ALIAS = "test"
PROD_ALIAS = "prod"

sys.path.insert(0, str(SCRIPT_DIR))
import appbundle  # noqa: E402  (contrôle du ZIP avant publication)


class DeployError(Exception):
    pass


@dataclass
class Response:
    status: int
    body: object


class Http:
    """Client HTTP minimal (urllib). Le jeton n'est jamais journalisé."""

    def __init__(self, base_url: str, token: str):
        self.base_url = base_url.rstrip("/")
        self._token = token

    def call(self, method: str, path: str, body: dict | None = None, expected: tuple[int, ...] = (200,)) -> Response:
        url = path if path.startswith("https://") else f"{self.base_url}{path}"
        data = json.dumps(body).encode("utf-8") if body is not None else None
        request = urllib.request.Request(url, data=data, method=method)
        request.add_header("Authorization", f"Bearer {self._token}")
        if data is not None:
            request.add_header("Content-Type", "application/json")
        return self._send(request, method, path, expected)

    def upload(self, endpoint: str, form_data: dict[str, str], zip_path: Path) -> Response:
        # Champs du formulaire d'abord, fichier en dernier (« The 'file' field must be at the end »).
        boundary = f"----egr{uuid.uuid4().hex}"
        parts: list[bytes] = []
        for key, value in form_data.items():
            parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{key}"\r\n\r\n{value}\r\n'.encode("utf-8"))
        parts.append(
            f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{zip_path.name}"\r\n'
            f"Content-Type: application/octet-stream\r\n\r\n".encode("utf-8"))
        parts.append(zip_path.read_bytes())
        parts.append(f"\r\n--{boundary}--\r\n".encode("utf-8"))
        request = urllib.request.Request(endpoint, data=b"".join(parts), method="POST")
        request.add_header("Content-Type", f"multipart/form-data; boundary={boundary}")
        return self._send(request, "POST", "<endpointURL de téléversement>", (200, 201, 204))

    @staticmethod
    def _send(request, method, label, expected) -> Response:
        try:
            with urllib.request.urlopen(request, timeout=300) as response:
                raw = response.read()
                status = response.status
        except urllib.error.HTTPError as error:
            raw = error.read()
            status = error.code
        text = raw.decode("utf-8", errors="replace")
        try:
            body = json.loads(text) if text else None
        except json.JSONDecodeError:
            body = text
        if status not in expected:
            raise DeployError(f"{method} {label} : HTTP {status} {text[:500]}")
        return Response(status, body)


class DryRunHttp:
    """Aucun appel réseau : affiche les requêtes. Simule une première publication (ressources inexistantes) ou,
    avec simulate_existing, des ressources existantes avec les alias test -> v2 et prod -> v1."""

    def __init__(self, out=None, simulate_existing: bool = False):
        self.out = out if out is not None else sys.stdout
        self.simulate_existing = simulate_existing
        self.requests: list[tuple[str, str, dict | None]] = []
        self._versions: dict[str, int] = {}

    def call(self, method, path, body=None, expected=(200,)) -> Response:
        self.requests.append((method, path, body))
        print(f"[dry-run] {method} {path}", file=self.out)
        if body is not None:
            print("          " + json.dumps(body, ensure_ascii=False), file=self.out)
        if method == "GET" and path.endswith("/aliases"):
            if not self.simulate_existing:
                return Response(404, None)  # simulation : ressource inexistante
            return Response(200, {"data": [{"id": TEST_ALIAS, "version": 2}, {"id": PROD_ALIAS, "version": 1}]})
        if method == "GET" and path == "/engines":
            return Response(200, {"data": [f"Autodesk.Revit+{v}" for v in SUPPORTED_VERSIONS], "paginationToken": ""})
        if method == "POST" and (path in ("/appbundles", "/activities") or path.endswith("/versions")):
            key = path if path.endswith("/versions") else f"{path}/{body['id']}"
            key = key.removesuffix("/versions")
            self._versions[key] = self._versions.get(key, 2 if self.simulate_existing else 0) + 1
            response = {"version": self._versions[key]}
            if path.startswith("/appbundles"):
                response["uploadParameters"] = {"endpointURL": "<endpointURL>", "formData": {"key": "<…>"}}
            return Response(200, response)
        return Response(200, {"version": (body or {}).get("version")})

    def upload(self, endpoint, form_data, zip_path) -> Response:
        self.requests.append(("UPLOAD", endpoint, {"file": zip_path.name}))
        print(f"[dry-run] POST {endpoint} (multipart : champs formData puis file={zip_path.name})", file=self.out)
        return Response(200, None)


def load_definition(version: str, kind: str, nickname: str | None = None, bundle_alias: str | None = None) -> dict:
    text = (PACKAGING_DIR / "definitions" / version / f"{kind}.json").read_text(encoding="utf-8")
    if nickname is not None:
        text = text.replace("{{NICKNAME}}", nickname)
    if bundle_alias is not None:
        text = text.replace("{{APPBUNDLE_ALIAS}}", bundle_alias)
    definition = json.loads(text)
    if "{{" in json.dumps(definition):
        raise DeployError(f"definitions/{version}/{kind}.json : paramètre de gabarit non remplacé")
    return definition


def definition_id(version: str, kind: str) -> str:
    """Identifiant non qualifié (champ id), sans exiger les paramètres de gabarit."""
    return json.loads((PACKAGING_DIR / "definitions" / version / f"{kind}.json").read_text(encoding="utf-8"))["id"]


def without_id(definition: dict) -> dict:
    # POST …/versions : « You can omit id from the request body ».
    return {k: v for k, v in definition.items() if k != "id"}


def aliases(http, kind: str, item_id: str) -> dict[str, int] | None:
    response = http.call("GET", f"/{kind}/{item_id}/aliases", expected=(200, 404))
    if response.status == 404:
        return None
    return {a["id"]: a["version"] for a in response.body.get("data", [])}


def set_alias(http, kind: str, item_id: str, alias: str, version: int, existing: dict[str, int] | None) -> None:
    if existing is not None and alias in existing:
        http.call("PATCH", f"/{kind}/{item_id}/aliases/{alias}", {"version": version})
    else:
        http.call("POST", f"/{kind}/{item_id}/aliases", {"id": alias, "version": version})


def check_engines(http, versions: list[str]) -> None:
    engines: set[str] = set()
    page = ""
    while True:
        response = http.call("GET", "/engines" + (f"?page={page}" if page else ""))
        engines.update(response.body.get("data", []))
        page = response.body.get("paginationToken") or ""
        if not page:
            break
    missing = [v for v in versions if f"Autodesk.Revit+{v}" not in engines]
    if missing:
        raise DeployError(f"engine(s) indisponible(s) : {', '.join('Autodesk.Revit+' + v for v in missing)}")


def publish_test(http, nickname: str, versions: list[str], bundle_dir: Path, log=print) -> None:
    check_engines(http, versions)
    for version in versions:
        zip_path = bundle_dir / f"EgRoomTracker_R{version}.zip"
        if not zip_path.is_file():
            raise DeployError(f"{zip_path} introuvable : lancer d'abord build-appbundle.sh {version}")
        errors = appbundle.check(zip_path, version)
        if errors:
            raise DeployError(f"{zip_path} refusé par le contrôle : " + "; ".join(errors))

        bundle = load_definition(version, "appbundle")
        bundle_id = bundle["id"]
        existing = aliases(http, "appbundles", bundle_id)
        if existing is None:
            created = http.call("POST", "/appbundles", bundle)
        else:
            created = http.call("POST", f"/appbundles/{bundle_id}/versions", without_id(bundle))
        bundle_version = created.body["version"]
        upload = created.body["uploadParameters"]
        http.upload(upload["endpointURL"], upload["formData"], zip_path)
        set_alias(http, "appbundles", bundle_id, TEST_ALIAS, bundle_version, existing)

        activity = load_definition(version, "activity", nickname, TEST_ALIAS)
        activity_id = activity["id"]
        existing = aliases(http, "activities", activity_id)
        if existing is None:
            created = http.call("POST", "/activities", activity)
        else:
            created = http.call("POST", f"/activities/{activity_id}/versions", without_id(activity))
        set_alias(http, "activities", activity_id, TEST_ALIAS, created.body["version"], existing)
        log(f"Revit {version} : {nickname}.{bundle_id}+{TEST_ALIAS} -> v{bundle_version}, "
            f"{nickname}.{activity_id}+{TEST_ALIAS} -> v{created.body['version']}")


def promote(http, nickname: str, versions: list[str], log=print) -> None:
    for version in versions:
        bundle_id = definition_id(version, "appbundle")
        current = aliases(http, "appbundles", bundle_id) or {}
        if TEST_ALIAS not in current:
            raise DeployError(f"{bundle_id} : aucun alias {TEST_ALIAS} à promouvoir")
        previous_bundle = current.get(PROD_ALIAS)
        set_alias(http, "appbundles", bundle_id, PROD_ALIAS, current[TEST_ALIAS], current)

        activity = load_definition(version, "activity", nickname, PROD_ALIAS)
        activity_id = activity["id"]
        current_activity = aliases(http, "activities", activity_id) or {}
        previous_activity = current_activity.get(PROD_ALIAS)
        created = http.call("POST", f"/activities/{activity_id}/versions", without_id(activity))
        set_alias(http, "activities", activity_id, PROD_ALIAS, created.body["version"], current_activity)
        log(f"Revit {version} : {bundle_id}+{PROD_ALIAS} -> v{current[TEST_ALIAS]} (avant : v{previous_bundle}), "
            f"{activity_id}+{PROD_ALIAS} -> v{created.body['version']} (avant : v{previous_activity})")
        if previous_bundle is None or previous_activity is None:
            log("  Première promotion : pas de version prod antérieure (retour arrière impossible par alias).")
        else:
            log(f"  Retour arrière : deploy.py rollback --apply --kind appbundle --revit {version} --to-version {previous_bundle} ; "
                f"deploy.py rollback --apply --kind activity --revit {version} --to-version {previous_activity}")


def rollback(http, kind: str, version: str, alias: str, to_version: int, log=print) -> None:
    item_id = definition_id(version, kind)
    path_kind = "appbundles" if kind == "appbundle" else "activities"
    http.call("PATCH", f"/{path_kind}/{item_id}/aliases/{alias}", {"version": to_version})
    log(f"{item_id}+{alias} -> v{to_version}")


def status(http, versions: list[str], log=print) -> None:
    for version in versions:
        for kind, path_kind in (("appbundle", "appbundles"), ("activity", "activities")):
            item_id = definition_id(version, kind)
            log(f"{item_id} : {aliases(http, path_kind, item_id)}")


def main(argv: list[str] | None = None, environ: dict[str, str] | None = None) -> int:
    environ = dict(os.environ if environ is None else environ)
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--apply", action="store_true", help="exécute réellement les appels APS (sinon dry-run)")
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("test")
    sub.add_parser("promote")
    sub.add_parser("status")
    r = sub.add_parser("rollback")
    r.add_argument("--kind", required=True, choices=("appbundle", "activity"))
    r.add_argument("--revit", required=True, choices=SUPPORTED_VERSIONS)
    r.add_argument("--to-version", required=True, type=int)
    r.add_argument("--alias", default=PROD_ALIAS)
    args = parser.parse_args(argv)

    versions = environ.get("REVIT_VERSIONS", " ".join(SUPPORTED_VERSIONS)).split()
    unknown = [v for v in versions if v not in SUPPORTED_VERSIONS]
    if unknown:
        print(f"REVIT_VERSIONS : version(s) non prise(s) en charge : {unknown}", file=sys.stderr)
        return 2
    bundle_dir = Path(environ.get("APPBUNDLE_DIR", REPO_ROOT / "artifacts" / "appbundles"))

    if args.apply:
        token, nickname = environ.get("APS_TOKEN", ""), environ.get("APS_NICKNAME", "")
        if not token or not nickname:
            print("--apply : APS_TOKEN et APS_NICKNAME sont obligatoires.", file=sys.stderr)
            return 2
        http = Http(environ.get("APS_DA_BASE_URL", DEFAULT_BASE_URL), token)
    else:
        nickname = environ.get("APS_NICKNAME") or "<APS_NICKNAME>"
        http = DryRunHttp(simulate_existing=args.command != "test")
        print("[dry-run] aucun appel APS ; ajouter --apply pour publier.")

    try:
        if args.command == "test":
            publish_test(http, nickname, versions, bundle_dir)
        elif args.command == "promote":
            promote(http, nickname, versions)
        elif args.command == "rollback":
            rollback(http, args.kind, args.revit, args.alias, args.to_version)
        else:
            status(http, versions)
    except DeployError as error:
        print(f"ÉCHEC : {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
