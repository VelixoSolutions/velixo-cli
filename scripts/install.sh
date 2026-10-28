#!/bin/sh
# Installs the Velixo CLI from the latest GitHub Release, or from the version in the first argument.
#   curl -fsSL https://cli.velixo.com/install.sh | sh
#   curl -fsSL https://cli.velixo.com/install.sh | sh -s -- 0.1.0
# VELIXO_INSTALL_DIR sets the install folder. The default is ~/.local/bin.
set -eu

REPO="VelixoSolutions/velixo-cli"

say() { printf '%s\n' "$*" >&2; }
fail() { say "error: $*"; exit 1; }

if command -v curl >/dev/null 2>&1; then
  fetch() { curl -fsSL -o "$2" "$1"; }
  fetch_stdout() { curl -fsSL "$1"; }
elif command -v wget >/dev/null 2>&1; then
  fetch() { wget -q -O "$2" "$1"; }
  fetch_stdout() { wget -q -O - "$1"; }
else
  fail "curl or wget is required to download the Velixo CLI."
fi

detect_arch() {
  case "$(uname -m)" in
    x86_64 | amd64) echo x64 ;;
    aarch64 | arm64) echo arm64 ;;
    *) fail "unsupported processor '$(uname -m)'. The Velixo CLI supports x64 and arm64." ;;
  esac
}

detect_rid() {
  os="$(uname -s)"
  arch="$(detect_arch)"
  case "$os" in
    Linux)
      if [ -n "$(ls /lib/ld-musl-* 2>/dev/null || true)" ]; then
        [ "$arch" = x64 ] || fail "unsupported platform: Linux with musl on $arch. The Velixo CLI supports musl on x64 only."
        echo linux-musl-x64
      else
        echo "linux-$arch"
      fi
      ;;
    Darwin)
      # A shell that runs under Rosetta reports x86_64 on an Apple silicon Mac.
      if [ "$arch" = x64 ] && [ "$(sysctl -n sysctl.proc_translated 2>/dev/null || echo 0)" = 1 ]; then
        arch=arm64
      fi
      echo "osx-$arch"
      ;;
    *)
      fail "unsupported operating system '$os'. On Windows, use: irm https://cli.velixo.com/install.ps1 | iex"
      ;;
  esac
}

latest_version() {
  tag="$(fetch_stdout "https://api.github.com/repos/$REPO/releases/latest" \
    | sed -n 's/.*"tag_name"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n 1)"
  [ -n "$tag" ] || fail "could not find the latest release of $REPO. Pass a version as the first argument."
  echo "${tag#v}"
}

sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$1" | awk '{print $1}'
  elif command -v openssl >/dev/null 2>&1; then
    openssl dgst -sha256 "$1" | awk '{print $NF}'
  else
    fail "sha256sum, shasum or openssl is required to verify the download."
  fi
}

rid="$(detect_rid)"
version="${1:-}"
version="${version#v}"
[ -n "$version" ] || version="$(latest_version)"

asset="velixo-$version-$rid.tar.gz"
base="https://github.com/$REPO/releases/download/v$version"
install_dir="${VELIXO_INSTALL_DIR:-$HOME/.local/bin}"

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT INT TERM

say "Installing Velixo CLI $version for $rid"
fetch "$base/$asset" "$tmp/$asset" || fail "could not download $base/$asset. Check that version $version exists and has a file for $rid."
fetch "$base/checksums.txt" "$tmp/checksums.txt" || fail "could not download $base/checksums.txt."

expected="$(awk -v name="$asset" '{ f = $2; sub(/^\*/, "", f); if (f == name) { print $1; exit } }' "$tmp/checksums.txt")"
[ -n "$expected" ] || fail "checksums.txt has no entry for $asset."
actual="$(sha256_of "$tmp/$asset")"
if [ "$expected" != "$actual" ]; then
  fail "SHA-256 mismatch for $asset. Expected $expected, got $actual. Nothing was installed."
fi

mkdir -p "$tmp/extract"
tar -xzf "$tmp/$asset" -C "$tmp/extract"
[ -f "$tmp/extract/velixo" ] || fail "the archive does not contain the velixo binary."

mkdir -p "$install_dir"
cp "$tmp/extract/velixo" "$install_dir/velixo"
chmod 755 "$install_dir/velixo"

say "Installed $install_dir/velixo"
case ":$PATH:" in
  *":$install_dir:"*) ;;
  *)
    say ""
    say "$install_dir is not on your PATH. Add this line to your shell profile:"
    say "  export PATH=\"$install_dir:\$PATH\""
    ;;
esac
say ""
say "Next step: velixo login"
