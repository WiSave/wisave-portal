#!/usr/bin/env bash

set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "Usage: $0 <from-migration|0> <to-migration> <output-sql-path>" >&2
  exit 1
fi

FROM_MIGRATION="$1"
TO_MIGRATION="$2"
OUTPUT_PATH="$3"

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_PATH="$ROOT_DIR/src/WiSave.Portal/WiSave.Portal.csproj"
STARTUP_PROJECT_PATH="$ROOT_DIR/src/WiSave.Portal.EfTools/WiSave.Portal.EfTools.csproj"

mkdir -p "$(dirname "$OUTPUT_PATH")"

dotnet ef migrations script \
  "$FROM_MIGRATION" \
  "$TO_MIGRATION" \
  --project "$PROJECT_PATH" \
  --startup-project "$STARTUP_PROJECT_PATH" \
  --output "$OUTPUT_PATH"

echo "Generated DbUp script: $OUTPUT_PATH"
