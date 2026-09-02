#!/usr/bin/env bash
# Repeatable local dev seed: empties the database, then loads people across regions, each
# with 50+ absences for 2026. Defaults to 50 people over 10 regions; pass --people N
# and/or --regions N to change that, --help for the full list. See tools/Seed/Program.cs
# for the data.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

dotnet run --project "$repo_root/tools/Seed" -- "$@"
