#!/bin/bash
# Runs the live integration tests, then runs each test that failed on a timeout once more.
#
#   scripts/live-tests.sh RESULTS_DIR
#
# The ERPs behind nx-dev sometimes answer later than the gateway waits (HTTP 524), and such a failure
# says nothing about the CLI. A test that fails for any other reason runs only once, and a test that
# times out twice fails the run. The script needs the integration test project built in Release.
set -u

if [ "$#" -ne 1 ]; then
  echo "usage: $0 RESULTS_DIR" >&2
  exit 2
fi

results=$1
project=tests/Velixo.Cli.IntegrationTests
# Matched against a failed test's message, which holds the CLI's stdout and stderr.
timeout_pattern='HTTP 52[24]|HTTP 50[24]|did not answer in time|ran longer than [0-9]+ minutes'

run_tests() {
  local report=$1
  shift
  dotnet test --project "$project" -c Release --no-build \
    --results-directory "$results" --report-xunit-ctrf --report-xunit-ctrf-filename "$report" "$@"
}

# The fully qualified methods of the failed test cases, those that timed out or those that did not.
failed_methods() {
  jq -r --arg pattern "$timeout_pattern" --arg kind "$2" '
    .results.tests[]
    | select(.status == "failed")
    | select(((.message // "") | test($pattern)) == ($kind == "timed-out"))
    | "\(.extra.type).\(.extra.method)"' "$1" | sort -u
}

run_tests first.ctrf.json && exit 0

first="$results/first.ctrf.json"
if [ ! -f "$first" ]; then
  echo "::error::The live tests failed before they wrote a report, so no test runs again."
  exit 1
fi

other=$(failed_methods "$first" other)
# A theory whose other case failed for another reason fails the run anyway, so it does not run again.
timed_out=$(comm -23 <(failed_methods "$first" timed-out) <(printf '%s\n' "$other" | sed '/^$/d'))

if [ -z "$other" ] && [ -z "$timed_out" ]; then
  echo "::error::dotnet test failed, but its report holds no failed test."
  exit 1
fi

if [ -n "$other" ]; then
  echo "::error::Failed for a reason other than a timeout, so not run again: ${other//$'\n'/ }"
fi

status=0
if [ -n "$timed_out" ]; then
  echo "Timed out, so run once more: ${timed_out//$'\n'/ }"
  filters=()
  for method in $timed_out; do
    filters+=(--filter-method "$method")
  done
  if run_tests retry.ctrf.json "${filters[@]}"; then
    echo "::warning::Timed out and passed when run again: ${timed_out//$'\n'/ }"
  else
    status=1
  fi
fi

[ -z "$other" ] && exit "$status"
exit 1
