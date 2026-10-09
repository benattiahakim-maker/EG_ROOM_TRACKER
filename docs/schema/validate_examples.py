"""Valide docs/examples/result.example.json et les résultats attendus de la recette (docs/recette/expected/**)
contre docs/schema/result.schema.json.

Usage : python docs/schema/validate_examples.py  (nécessite le paquet jsonschema)
"""
import json
import pathlib
import sys

from jsonschema import Draft202012Validator, FormatChecker

root = pathlib.Path(__file__).resolve().parents[1]
schema = json.loads((root / "schema" / "result.schema.json").read_text(encoding="utf-8"))
Draft202012Validator.check_schema(schema)
validator = Draft202012Validator(schema, format_checker=FormatChecker())
files = [root / "examples" / "result.example.json", *sorted((root / "recette" / "expected").rglob("*.json"))]
failed = False
for path in files:
    document = json.loads(path.read_text(encoding="utf-8"))
    errors = sorted(validator.iter_errors(document), key=lambda e: list(e.path))
    for error in errors:
        print(f"{path.relative_to(root)} : {'/'.join(map(str, error.path)) or '<racine>'} : {error.message}")
    failed = failed or bool(errors)
if failed:
    sys.exit(1)
print(f"{len(files)} fichier(s) result.json conforme(s) au schéma")
