#!/usr/bin/env bash
# The one check (AGT-1): every check CI runs that does not need Docker, in CI's order,
# stopping at the first failure and naming it. A change is done when this passes. The Stop
# hook (scripts/hooks/stop-check.mjs) runs it every time a Claude Code session stops, and
# reads the "check.sh: step failed: <name>" line it prints on a failure.
#
#   scripts/check.sh          run every step
#   scripts/check.sh --list   print the steps
#
# The browser steps need a chrome-headless-shell (tools/perf/README.md#setup). They run the
# production build the way CI runs the container: five API instances on this machine, in
# Development with holidays off, each with a database of its own in a temporary directory,
# and the stub OIDC provider the end-to-end suite starts. Ports 18080-18084 and
# 19000-19001, away from the 8080 and 9000 a hand-started container or suite uses, so the
# check runs beside them. Every process it starts is stopped on exit, failure included;
# the temporary directory, with the instances' logs and the suite's failure screenshots,
# is kept when a step fails and its path printed, until a later run replaces it.
#
# One check runs at a time per repository, worktrees included, since they share the ports
# and a worktree its build outputs: a second run waits for the first, saying so. Without
# network, it works from the package caches and skips the registry audits with a note (CI
# runs them), rather than waiting out the registries' retries.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

steps=(
  "dotnet-restore|.NET restore and NuGet audit"
  "dotnet-build|.NET Release build, 0 warnings"
  "dotnet-test|.NET tests"
  "versions|API and SPA versions agree"
  "web-install|SPA install, frozen lockfile"
  "web-typecheck|SPA typecheck"
  "web-lint|SPA lint"
  "web-format|SPA format check"
  "web-test|SPA tests"
  "web-audit|SPA dependency audit, high and critical"
  "trace|Spec trace check"
  "script-tests|Script tests: trace check, hooks, privacy review"
  "privacy|Privacy review trailers of the commits not pushed yet"
  "browser|chrome-headless-shell present"
  "web-build|SPA production build"
  "instances|Start the local instances"
  "e2e|End-to-end tests"
  "perf|Performance budget at 1000 people"
)

if [[ "${1:-}" == "--list" ]]; then
  for step in "${steps[@]}"; do echo "${step#*|}"; done
  exit 0
elif [[ $# -gt 0 ]]; then
  echo "usage: scripts/check.sh [--list]" >&2
  exit 2
fi

browser="${CHROME_HEADLESS_SHELL:-$HOME/.cache/chrome-headless-shell/chrome-headless-shell-linux64/chrome-headless-shell}"
ci_chrome_version="$(grep -oP 'CHROME_HEADLESS_SHELL_VERSION: \K\S+' .github/workflows/ci.yml)"

# Ports of the instances, in the order of APP_URL, APP_URL_NO_PERMISSIONS, APP_URL_OIDC,
# APP_URL_OIDC_SPLIT and APP_URL_OIDC_SPLIT_EXTRA_ORIGIN, and of the stub provider.
app_ports=(18080 18081 18082 18083 18084)
stub_port=19000
stub_token_port=19001

git_common="$(git rev-parse --path-format=absolute --git-common-dir)"

# One run at a time. flock holds the lock and runs this script again under it, closing the
# lock's file descriptor for it: a descriptor the script held would be inherited by every
# process it starts, MSBuild's and the compiler's reused servers among them, which outlive
# the run and would keep the lock. Without flock (not on Linux), no lock.
lock="$git_common/check.sh.lock"
if [[ -z "${CHECK_SH_LOCKED:-}" ]] && command -v flock >/dev/null; then
  export CHECK_SH_LOCKED=1
  status=0
  flock --nonblock --close --conflict-exit-code 75 "$lock" "$0" "$@" || status=$?
  [[ $status -ne 75 ]] && exit "$status"
  echo "check.sh: another check is running in this repository ($(head -n 1 "$lock" 2>/dev/null)); waiting for it to finish"
  exec flock --close "$lock" "$0" "$@"
fi
[[ -n "${CHECK_SH_LOCKED:-}" ]] && echo "pid $$, started $(date '+%H:%M:%S') in $repo_root" >"$lock"

# The directory a failed run kept, listed here, is removed once a later run starts, so they
# do not pile up in the temporary directory.
kept_list="$git_common/check-kept"
if [[ -f "$kept_list" ]]; then
  while IFS= read -r old; do [[ "$old" == */foo-calendar-check.* ]] && rm -rf "$old"; done <"$kept_list"
  rm -f "$kept_list"
fi

work="$(mktemp -d "${TMPDIR:-/tmp}/foo-calendar-check.XXXXXX")"
pids=()
failed=""
current=""

cleanup() {
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done
  for pid in "${pids[@]}"; do wait "$pid" 2>/dev/null || true; done
  if [[ -n "$failed" && -n "$(ls -A "$work" | grep -v '^\.interrupted$')" ]]; then
    echo "$work" >>"$kept_list"
    echo "check.sh: kept $work (instance logs, e2e failures, perf results)"
  else
    rm -rf "$work"
  fi
}
trap cleanup EXIT
# An interrupted run (Ctrl-C, the Stop hook stopping it) still stops its instances, and
# says it was interrupted rather than that a step failed.
interrupted() {
  failed=interrupted
  echo
  echo "check.sh: interrupted${current:+ during: $current}"
  exit 130
}
trap interrupted INT TERM HUP
# Bash runs no INT trap when the command it waits on handles the interrupt and exits on its
# own, as node's test runner does; this watcher, which an interrupt reaches as well, notes
# it, so the step is not reported as failed.
node -e 'process.on("SIGINT", () => { require("fs").writeFileSync(process.argv[1], ""); process.exit(0); }); setInterval(() => {}, 1 << 30);' "$work/.interrupted" &
pids+=("$!")

# Whether the registries answer at all, asked once, in a few seconds. Offline, every pnpm
# command otherwise spends over a minute in retries (pnpm 11 reads its settings from
# pnpm_config_*), and a restore that is not already up to date fails on the NuGet audit
# (NU1900) before anything else; packages already in the caches still install.
online=yes
if ! curl --silent --max-time 5 --output /dev/null https://registry.npmjs.org/-/ping ||
  ! curl --silent --max-time 5 --output /dev/null https://api.nuget.org/v3/index.json; then
  online=""
  export pnpm_config_fetch_retries=0 npm_config_fetch_retries=0
  echo "check.sh: a package registry is unreachable; working from the caches, and the audits are skipped (CI enforces them)"
fi

# Starts one production-build instance in the background. Development is the only
# environment that starts without an identity provider and that accepts the stub's http
# origin (CFG-2); the rest is what CI's containers set. Auth__Provider is set even where
# appsettings.Development.json already sets it, so a developer's user secrets cannot turn
# an instance into something CI never runs.
start_instance() {
  local port="$1"
  shift
  (
    cd src/Api
    exec env \
      ASPNETCORE_ENVIRONMENT=Development \
      ASPNETCORE_URLS="http://localhost:$port" \
      ConnectionStrings__FooCalendarDb="Data Source=$work/app-$port.db" \
      Holidays__Source=None \
      "$@" \
      dotnet bin/Release/net10.0/Api.dll
  ) >"$work/app-$port.log" 2>&1 &
  pids+=("$!")
}

wait_healthy() {
  local port="$1"
  for _ in $(seq 1 60); do
    if curl --fail --silent "http://localhost:$port/healthchecks" | grep -q '"Healthy"'; then
      return 0
    fi
    sleep 0.5
  done
  echo "the instance on port $port did not report healthy within 30 s; its log:" >&2
  tail -n 30 "$work/app-$port.log" >&2
  return 1
}

port_free() {
  ! (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null
}

run() {
  case "$1" in
    dotnet-restore)
      if [[ -n "$online" ]]; then
        dotnet restore
      else
        echo "check.sh: registry unreachable, restoring without the NuGet audit (CI enforces it)"
        dotnet restore -p:NuGetAudit=false
      fi
      ;;
    dotnet-build) dotnet build --no-restore --configuration Release ;;
    dotnet-test) dotnet test --no-build --configuration Release ;;
    versions)
      local api web
      api=$(grep -oP '<Version>\K[^<]+' src/Api/Api.csproj)
      web=$(node -p 'require("./src/Web/package.json").version')
      if [[ "$api" != "$web" ]]; then
        echo "Api.csproj is $api but package.json is $web" >&2
        return 1
      fi
      ;;
    web-install) pnpm --dir src/Web install --frozen-lockfile --prefer-offline ;;
    web-typecheck) pnpm --dir src/Web typecheck ;;
    web-lint) pnpm --dir src/Web lint:check ;;
    web-format) pnpm --dir src/Web format:check ;;
    web-test) pnpm --dir src/Web test ;;
    web-audit)
      if [[ -n "$online" ]]; then
        pnpm --dir src/Web audit --audit-level high
      else
        echo "check.sh: registry unreachable, audit skipped (CI enforces it)"
      fi
      ;;
    trace) node scripts/check-spec-trace.mjs ;;
    script-tests) node --test --test-timeout=60000 'scripts/**/*.test.mjs' ;;
    privacy)
      # What CI's trailer check will see once these commits are pushed: the commits since
      # the upstream branch, or since origin/master. With neither, every commit since the
      # review started.
      local base
      base="$(git rev-parse --verify --quiet '@{upstream}' || git rev-parse --verify --quiet origin/master || echo 0)"
      node scripts/privacy-review.mjs verify "$base" HEAD
      ;;
    browser)
      if [[ ! -x "$browser" ]]; then
        cat >&2 <<EOF
No chrome-headless-shell at $browser.
Download the linux64 chrome-headless-shell of the release CI pins ($ci_chrome_version), from
  https://storage.googleapis.com/chrome-for-testing-public/$ci_chrome_version/linux64/chrome-headless-shell-linux64.zip
and unzip it into ~/.cache/chrome-headless-shell, or point CHROME_HEADLESS_SHELL at the
binary. tools/perf/README.md#setup has the details.
EOF
        return 1
      fi
      ;;
    web-build) pnpm --dir src/Web build ;;
    instances)
      local port
      for port in "${app_ports[@]}" "$stub_port" "$stub_token_port"; do
        if ! port_free "$port"; then
          echo "port $port is in use; the check needs 18080-18084 and 19000-19001" >&2
          return 1
        fi
      done
      local oidc=(Auth__Provider=Oidc Auth__Audience=foo-calendar-e2e Auth__ClientId=foo-calendar-e2e-spa)
      start_instance "${app_ports[0]}" Auth__Provider=None
      start_instance "${app_ports[1]}" Auth__Provider=None Auth__DevUser__GrantAllPermissions=false
      start_instance "${app_ports[2]}" "${oidc[@]}" Auth__Authority="http://localhost:$stub_port"
      start_instance "${app_ports[3]}" "${oidc[@]}" Auth__Authority="http://localhost:$stub_port/split"
      start_instance "${app_ports[4]}" "${oidc[@]}" Auth__Authority="http://localhost:$stub_port/split" \
        Auth__ExtraOrigins__0="http://localhost:$stub_token_port"
      for port in "${app_ports[@]}"; do wait_healthy "$port" || return 1; done
      # CI's smoke test: the shell is served, with its base href.
      curl --fail --silent --show-error "http://localhost:${app_ports[0]}/" | grep -q '<base href=' || {
        echo "the instance on port ${app_ports[0]} does not serve the SPA shell" >&2
        return 1
      }
      ;;
    e2e)
      APP_URL="http://localhost:${app_ports[0]}/" \
        APP_URL_NO_PERMISSIONS="http://localhost:${app_ports[1]}/" \
        APP_URL_OIDC="http://localhost:${app_ports[2]}/" \
        APP_URL_OIDC_SPLIT="http://localhost:${app_ports[3]}/" \
        APP_URL_OIDC_SPLIT_EXTRA_ORIGIN="http://localhost:${app_ports[4]}/" \
        OIDC_STUB_PORT="$stub_port" OIDC_STUB_TOKEN_PORT="$stub_token_port" \
        E2E_OUT="$work/e2e" CHROME_HEADLESS_SHELL="$browser" \
        node --test --test-timeout=120000 --test-reporter=spec tools/e2e/flows.test.mjs
      ;;
    perf)
      APP_URL="http://localhost:${app_ports[0]}/" \
        RESULT_OUT="$work/perf-results.json" TRACE_OUT="$work/perf-trace.json" \
        CHROME_HEADLESS_SHELL="$browser" \
        node tools/perf/harness.mjs 1000 --budget
      ;;
  esac
}

total_start=$SECONDS
count=${#steps[@]}
index=0
for step in "${steps[@]}"; do
  index=$((index + 1))
  id="${step%%|*}"
  name="${step#*|}"
  echo
  echo "==> [$index/$count] $name"
  start=$SECONDS
  current="$name"
  if run "$id"; then
    echo "--- ok: $name ($((SECONDS - start)) s)"
  else
    # Give the watcher a moment to note an interrupt that arrived as the step ended.
    sleep 0.2
    [[ -e "$work/.interrupted" ]] && interrupted
    failed="$name"
    echo
    echo "check.sh: step failed: $name ($((SECONDS - start)) s)"
    exit 1
  fi
done

echo
echo "check.sh: all $count steps passed in $((SECONDS - total_start)) s"
