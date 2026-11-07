#!/usr/bin/env node
// Assembles the npm packages for a Velixo CLI release from its archives.
//
//   node scripts/npm-packages.mjs 1.0.2 dist npm-out
//
// dist holds the release's velixo-<version>-<rid>.tar.gz and .zip files. npm-out receives one
// folder per package: a platform package per archive, holding the signed binary, and @velixo/cli,
// which holds the launcher (npm/cli) and lists the platform packages as optional dependencies.
// Publish the platform packages first, then @velixo/cli.
import { execFileSync } from 'node:child_process';
import { chmodSync, copyFileSync, cpSync, existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

// The release's runtime identifiers, and what npm and Node call the same platform.
const PLATFORMS = [
  { rid: 'osx-arm64', name: 'darwin-arm64', os: 'darwin', cpu: 'arm64' },
  { rid: 'osx-x64', name: 'darwin-x64', os: 'darwin', cpu: 'x64' },
  { rid: 'linux-arm64', name: 'linux-arm64', os: 'linux', cpu: 'arm64', libc: 'glibc' },
  { rid: 'linux-x64', name: 'linux-x64', os: 'linux', cpu: 'x64', libc: 'glibc' },
  { rid: 'linux-musl-x64', name: 'linux-x64-musl', os: 'linux', cpu: 'x64', libc: 'musl' },
  { rid: 'win-arm64', name: 'win32-arm64', os: 'win32', cpu: 'arm64' },
  { rid: 'win-x64', name: 'win32-x64', os: 'win32', cpu: 'x64' },
];

const COMMON = {
  author: 'Velixo Solutions Inc.',
  license: 'Apache-2.0',
  homepage: 'https://github.com/VelixoSolutions/velixo-cli#readme',
  repository: { type: 'git', url: 'git+https://github.com/VelixoSolutions/velixo-cli.git' },
  bugs: { url: 'https://github.com/VelixoSolutions/velixo-cli/issues' },
};

function writeJson(file, value) {
  writeFileSync(file, `${JSON.stringify(value, null, 2)}\n`);
}

function extract(archive, into) {
  if (archive.endsWith('.zip')) {
    execFileSync('unzip', ['-q', archive, '-d', into], { stdio: 'inherit' });
  } else {
    execFileSync('tar', ['-xzf', archive, '-C', into], { stdio: 'inherit' });
  }
}

function main(args) {
  if (args.length !== 3) {
    process.stderr.write('usage: node scripts/npm-packages.mjs VERSION ASSETS_DIR OUT_DIR\n');
    process.exit(2);
  }
  const [version, assets, out] = args;
  rmSync(out, { recursive: true, force: true });
  mkdirSync(out, { recursive: true });

  for (const platform of PLATFORMS) {
    const windows = platform.os === 'win32';
    const archive = path.join(assets, `velixo-${version}-${platform.rid}.${windows ? 'zip' : 'tar.gz'}`);
    if (!existsSync(archive)) {
      throw new Error(`missing ${archive}`);
    }

    const scratch = mkdtempSync(path.join(tmpdir(), 'velixo-npm-'));
    extract(archive, scratch);

    const dir = path.join(out, `cli-${platform.name}`);
    const binary = windows ? 'velixo.exe' : 'velixo';
    mkdirSync(path.join(dir, 'bin'), { recursive: true });
    copyFileSync(path.join(scratch, binary), path.join(dir, 'bin', binary));
    if (!windows) {
      chmodSync(path.join(dir, 'bin', binary), 0o755);
    }
    for (const file of ['LICENSE', 'NOTICE']) {
      copyFileSync(path.join(scratch, file), path.join(dir, file));
    }
    rmSync(scratch, { recursive: true, force: true });

    writeFileSync(
      path.join(dir, 'README.md'),
      `# @velixo/cli-${platform.name}\n\nThe Velixo CLI binary for ${platform.name}. Install [@velixo/cli](https://www.npmjs.com/package/@velixo/cli) instead, which picks this package on this platform.\n`,
    );
    writeJson(path.join(dir, 'package.json'), {
      name: `@velixo/cli-${platform.name}`,
      version,
      description: `The Velixo CLI binary for ${platform.name}. Install @velixo/cli instead.`,
      ...COMMON,
      os: [platform.os],
      cpu: [platform.cpu],
      ...(platform.libc ? { libc: [platform.libc] } : {}),
      files: ['bin', 'LICENSE', 'NOTICE', 'README.md'],
      preferUnplugged: true,
    });
  }

  const cli = path.join(out, 'cli');
  cpSync(path.join(root, 'npm', 'cli'), cli, { recursive: true });
  chmodSync(path.join(cli, 'bin', 'velixo.js'), 0o755);
  for (const file of ['LICENSE', 'NOTICE']) {
    copyFileSync(path.join(root, file), path.join(cli, file));
  }
  writeJson(path.join(cli, 'package.json'), {
    name: '@velixo/cli',
    version,
    description: 'Query an ERP, run ERP commands and imports from the command line, for AI coding agents, scripts and people.',
    keywords: ['velixo', 'cli', 'erp', 'acumatica', 'sage-intacct', 'business-central', 'mcp', 'agents'],
    ...COMMON,
    bin: { velixo: 'bin/velixo.js' },
    files: ['bin', 'LICENSE', 'NOTICE', 'README.md'],
    engines: { node: '>=18' },
    optionalDependencies: Object.fromEntries(PLATFORMS.map((p) => [`@velixo/cli-${p.name}`, version])),
  });

  process.stdout.write(`${PLATFORMS.length + 1} packages in ${out}\n`);
}

main(process.argv.slice(2));
