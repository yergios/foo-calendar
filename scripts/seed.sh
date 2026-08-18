#!/usr/bin/env bash
# Repeatable local dev seed: empties the database, then loads 50 people across 10
# regions, each with 50+ absences for 2026. See tools/Seed/Program.cs for the data.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

dotnet run --project "$repo_root/tools/Seed"
