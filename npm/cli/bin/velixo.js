#!/usr/bin/env node
// Runs the velixo binary from the platform package npm installed next to this one.
// @velixo/cli lists one package per platform as optional dependencies, and npm installs only the
// one whose os, cpu and libc match this machine.
'use strict';

const { spawnSync } = require('node:child_process');
const path = require('node:path');

const PACKAGES = {
  'darwin-arm64': '@velixo/cli-darwin-arm64',
  'darwin-x64': '@velixo/cli-darwin-x64',
  'linux-arm64': '@velixo/cli-linux-arm64',
  'linux-x64': '@velixo/cli-linux-x64',
  'linux-x64-musl': '@velixo/cli-linux-x64-musl',
  'win32-arm64': '@velixo/cli-win32-arm64',
  'win32-x64': '@velixo/cli-win32-x64',
};

function isMusl() {
  if (process.platform !== 'linux') {
    return false;
  }
  try {
    // A glibc Node reports the glibc version it runs on; a musl Node (Alpine) reports none.
    return !process.report.getReport().header.glibcVersionRuntime;
  } catch {
    return false;
  }
}

function fail(message) {
  process.stderr.write(`velixo: ${message}\n`);
  process.exit(1);
}

const key = `${process.platform}-${process.arch}${isMusl() ? '-musl' : ''}`;
const name = PACKAGES[key];
if (!name) {
  fail(`no Velixo CLI build for ${key}. Builds exist for: ${Object.keys(PACKAGES).join(', ')}.`);
}

let binary;
try {
  const manifest = require.resolve(`${name}/package.json`);
  binary = path.join(path.dirname(manifest), 'bin', process.platform === 'win32' ? 'velixo.exe' : 'velixo');
} catch {
  fail(
    `the package ${name} is not installed. npm skips it when optional dependencies are turned off ` +
      `(--omit=optional or --no-optional). Install again with them: npm install -g @velixo/cli`,
  );
}

const result = spawnSync(binary, process.argv.slice(2), { stdio: 'inherit', windowsHide: false });

if (result.error) {
  fail(`could not run ${binary}: ${result.error.message}`);
}

if (result.signal) {
  // Ends this process the way the binary ended, so the shell sees the same signal.
  process.kill(process.pid, result.signal);
}

process.exit(result.status ?? 1);
