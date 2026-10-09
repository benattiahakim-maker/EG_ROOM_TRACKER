#!/usr/bin/env bash
# Construit et contrôle les AppBundles Revit d'EG_ROOM_TRACKER (EGR-13). Aucun appel APS.
#
# Usage : src/Packaging/scripts/build-appbundle.sh [2025] [2026] [2027]   (défaut : les trois)
# Variables facultatives :
#   CONFIGURATION=Release   configuration de build
#   OUT_DIR=artifacts/appbundles
#   WITH_SYMBOLS=1          inclut les .pdb des assemblies EgRoomTracker.* (par défaut : non)
#   SKIP_BUILD=1            réutilise une sortie de build existante
# Prérequis : SDK .NET (global.json), python3. Fonctionne sous Linux, macOS et Windows (Git Bash / WSL).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
CONFIGURATION="${CONFIGURATION:-Release}"
OUT_DIR="${OUT_DIR:-$REPO_ROOT/artifacts/appbundles}"
PYTHON="${PYTHON:-python3}"

versions=("$@")
if [[ ${#versions[@]} -eq 0 ]]; then
  versions=(2025 2026 2027)
fi

app_version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$REPO_ROOT/Directory.Build.props" | head -n1)"
app_version="${app_version:-0.0.0}"

symbols=()
if [[ "${WITH_SYMBOLS:-0}" == "1" ]]; then
  symbols=(--with-symbols)
fi

for version in "${versions[@]}"; do
  case "$version" in
    2025|2026|2027) ;;
    *) echo "Version Revit non prise en charge : $version (attendu : 2025, 2026 ou 2027)" >&2; exit 2 ;;
  esac

  build_dir="$REPO_ROOT/src/EntryPoint/bin/$version/$CONFIGURATION"
  if [[ "${SKIP_BUILD:-0}" != "1" ]]; then
    echo "== Build Revit $version ($CONFIGURATION)"
    dotnet build "$REPO_ROOT/src/EntryPoint/EgRoomTracker.EntryPoint.csproj" \
      -c "$CONFIGURATION" -p:RevitVersion="$version" -nologo -v quiet
  fi

  echo "== Assemblage EgRoomTracker_R$version.zip"
  zip_path="$("$PYTHON" "$SCRIPT_DIR/appbundle.py" assemble --revit-version "$version" \
    --build-dir "$build_dir" --out "$OUT_DIR" --app-version "$app_version" "${symbols[@]}")"

  echo "== Contrôle du contenu"
  "$PYTHON" "$SCRIPT_DIR/appbundle.py" check "$zip_path" --revit-version "$version"
done

echo "AppBundles prêts dans $OUT_DIR"
