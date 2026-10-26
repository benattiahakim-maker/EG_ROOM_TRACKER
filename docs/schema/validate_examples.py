"""Valide les exemples contre leurs schémas JSON.

- docs/examples/result.example.json et docs/recette/expected/** contre docs/schema/result.schema.json ;
- docs/examples/generic.example.json contre docs/schema/generic.schema.json, puis les règles croisées de l'enveloppe commune de la
  plateforme (ids uniques, ref résolues, clé unique par objet hors ref, viewerId texte, modelKey connue, boîtes min <= max,
  profondeur 64 au plus), que le schéma ne peut pas exprimer.

Usage : python docs/schema/validate_examples.py  (nécessite le paquet jsonschema)
"""
import json
import pathlib
import sys

from jsonschema import Draft202012Validator, FormatChecker

MAX_DEPTH = 64

root = pathlib.Path(__file__).resolve().parents[1]


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def validator_for(schema_name):
    schema = load(root / "schema" / schema_name)
    Draft202012Validator.check_schema(schema)
    return Draft202012Validator(schema, format_checker=FormatChecker())


def schema_errors(validator, document):
    return sorted(validator.iter_errors(document), key=lambda e: list(e.path))


def envelope_structure_errors(envelope):
    """Règles croisées de l'enveloppe commune (validateEnvelope de la plateforme) ; liste de messages."""
    errors = []
    model_keys = [m["key"] for m in envelope.get("models", [])]
    if len(model_keys) != len(set(model_keys)):
        errors.append("models : clés en double")
    ids = {}  # id -> contenu canonique : un id répété doit être le même objet partagé (identique)
    refs = []

    def walk(node, path, depth):
        if depth > MAX_DEPTH:
            errors.append(f"{path} : profondeur maximale dépassée")
            return
        shape = json.dumps(node, sort_keys=True, ensure_ascii=False)
        if node["id"] in ids:
            if ids[node["id"]] != shape:
                errors.append(f"{path} : id en double « {node['id']} » avec un contenu différent (un objet partagé doit être identique)")
            return  # objet partagé déjà parcouru : sous-arbre identique
        ids[node["id"]] = shape
        keys = set()
        for prop in node["properties"]:
            key, kind, value = prop["key"], prop["type"], prop["value"]
            if kind != "ref":
                if key in keys:
                    errors.append(f"{path} : clé en double « {key} »")
                keys.add(key)
            if kind == "ref":
                refs.append((path, value))
            if kind == "box":
                for axis in range(3):
                    if value["min"][axis] > value["max"][axis]:
                        errors.append(f"{path} : box min > max sur l'axe {axis}")
            if key == "viewerId" and kind != "text":
                errors.append(f"{path} : viewerId doit être un texte")
            if key == "modelKey" and value not in model_keys:
                errors.append(f"{path} : modelKey « {value} » absente de models")
        for child in node["children"]:
            walk(child, f"{path}/{node['id']}", depth + 1)

    for node in envelope["objects"]:
        walk(node, "objects", 0)
    errors.extend(f"{path} : la référence « {target} » ne désigne aucun objet" for path, target in refs if target not in ids)
    return errors


failed = False
checked = 0

result_validator = validator_for("result.schema.json")
result_files = [root / "examples" / "result.example.json", *sorted((root / "recette" / "expected").rglob("*.json"))]
for path in result_files:
    for error in schema_errors(result_validator, load(path)):
        print(f"{path.relative_to(root)} : {'/'.join(map(str, error.path)) or '<racine>'} : {error.message}")
        failed = True
    checked += 1
print(f"{len(result_files)} fichier(s) result.json conforme(s) au schéma" if not failed else "result.json : erreurs ci-dessus")

generic_validator = validator_for("generic.schema.json")
generic_files = [root / "examples" / "generic.example.json"]
generic_failed = False
for path in generic_files:
    document = load(path)
    messages = [
        f"{path.relative_to(root)} : {'/'.join(map(str, error.path)) or '<racine>'} : {error.message}"
        for error in schema_errors(generic_validator, document)
    ]
    if not messages:
        messages = [f"{path.relative_to(root)} : {message}" for message in envelope_structure_errors(document)]
    for message in messages:
        print(message)
    generic_failed = generic_failed or bool(messages)
if not generic_failed:
    print(f"{len(generic_files)} fichier(s) generic conforme(s) au schéma et aux règles de l'enveloppe")

if failed or generic_failed:
    sys.exit(1)
