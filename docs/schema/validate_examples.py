"""Valide docs/examples/result.example.json contre docs/schema/result.schema.json.

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
example = json.loads((root / "examples" / "result.example.json").read_text(encoding="utf-8"))
errors = sorted(validator.iter_errors(example), key=lambda e: list(e.path))
for error in errors:
    print(f"{'/'.join(map(str, error.path)) or '<racine>'} : {error.message}")
if errors:
    sys.exit(1)
print("result.example.json conforme au schéma")
