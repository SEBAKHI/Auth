import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..",
);
const args = new Map();
for (let index = 2; index < process.argv.length; index += 2) {
  args.set(process.argv[index], process.argv[index + 1]);
}

const threshold = Number(args.get("--threshold") ?? "90");
const requestedBase = args.get("--base") ?? process.env.COVERAGE_BASE;

function git(...commandArgs) {
  return execFileSync("git", commandArgs, {
    cwd: repoRoot,
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"],
  });
}

function defaultBase() {
  if (requestedBase) return requestedBase;
  const dirty = git("status", "--porcelain", "--untracked-files=all").trim();
  if (dirty) return "HEAD";
  try {
    git("rev-parse", "HEAD^");
    return "HEAD^";
  } catch {
    return "HEAD";
  }
}

function normalizeRepoPath(value) {
  const absolute = path.isAbsolute(value)
    ? value
    : path.resolve(repoRoot, value);
  const relative = path.relative(repoRoot, absolute).replaceAll("\\", "/");
  // Always lower-cased, on every platform. These strings are only ever map keys
  // and prefix tests, and the scopes below are written lower-case: folding only
  // on Windows meant that on macOS and Linux "Auth_UI/..." matched no scope at
  // all, the changed-line intersection came out empty, and the gate reported
  // success over a diff it had not measured.
  return relative.toLowerCase();
}

function isProductSource(file) {
  return (
    // Frontend product source is exactly the tree the coverage run
    // instruments (vitest's `include`): app and package sources. Config
    // files, the e2e harness and repo-root scripts are real code, but no
    // unit run reports on them, and counting them here would leave the gate
    // unable to tell "not instrumented" from "not covered".
    /^(auth\/.*\.cs|auth_ui\/(?:apps|packages)\/[^/]+\/src\/.*\.(?:ts|tsx))$/i.test(file) &&
    !/(?:^|\/)(?:bin|obj|coverage|testresults)(?:\/|$)/i.test(file) &&
    !/(?:\.test|\.spec)\.(?:ts|tsx)$/i.test(file) &&
    !/\.d\.ts$/i.test(file) &&
    !/^auth\/auth_api\.tests\//i.test(file) &&
    // HTTP contracts are declaration-only request DTOs. They contain no
    // behavior to exercise; controller tests verify that every field maps.
    !/^auth\/auth_api\/modules\/[^/]+\/contracts\//i.test(file)
  );
}

function changedLines(base) {
  const changed = new Map();
  const diff = git(
    "diff",
    "--unified=0",
    "--no-ext-diff",
    "--no-renames",
    base,
    "--",
    "*.cs",
    "*.ts",
    "*.tsx",
  );

  let currentFile = null;
  for (const line of diff.split(/\r?\n/)) {
    if (line.startsWith("+++ ")) {
      const name = line.slice(4);
      currentFile =
        name === "/dev/null"
          ? null
          : normalizeRepoPath(name.replace(/^b\//, ""));
      if (
        currentFile &&
        isProductSource(currentFile) &&
        !changed.has(currentFile)
      ) {
        changed.set(currentFile, new Set());
      }
      continue;
    }
    if (!currentFile || !isProductSource(currentFile) || !line.startsWith("@@"))
      continue;
    const match = line.match(/\+(\d+)(?:,(\d+))?\s/);
    if (!match) continue;
    const start = Number(match[1]);
    const count = match[2] === undefined ? 1 : Number(match[2]);
    for (let offset = 0; offset < count; offset += 1) {
      changed.get(currentFile).add(start + offset);
    }
  }

  const untracked = git(
    "ls-files",
    "--others",
    "--exclude-standard",
    "-z",
    "--",
    "*.cs",
    "*.ts",
    "*.tsx",
  );
  for (const name of untracked.split("\0").filter(Boolean)) {
    const file = normalizeRepoPath(name);
    if (!isProductSource(file)) continue;
    const count = fs
      .readFileSync(path.resolve(repoRoot, name), "utf8")
      .split(/\r?\n/).length;
    changed.set(
      file,
      new Set(Array.from({ length: count }, (_, index) => index + 1)),
    );
  }

  return changed;
}

function addLine(target, line, hits) {
  target.set(line, Math.max(target.get(line) ?? 0, Number(hits)));
}

function readIstanbul(reportPath) {
  const report = JSON.parse(
    fs.readFileSync(path.resolve(repoRoot, reportPath), "utf8"),
  );
  const files = new Map();
  for (const [name, coverage] of Object.entries(report)) {
    const file = normalizeRepoPath(name);
    const lines = new Map();
    for (const [statementId, location] of Object.entries(
      coverage.statementMap ?? {},
    )) {
      addLine(lines, location.start.line, coverage.s?.[statementId] ?? 0);
    }
    files.set(file, lines);
  }
  return files;
}

function readCoverlet(reportPath) {
  const report = JSON.parse(
    fs.readFileSync(path.resolve(repoRoot, reportPath), "utf8"),
  );
  const files = new Map();
  for (const module of Object.values(report)) {
    for (const [name, classes] of Object.entries(module)) {
      const file = normalizeRepoPath(name);
      if (!isProductSource(file)) continue;
      const lines = files.get(file) ?? new Map();
      for (const methods of Object.values(classes)) {
        for (const method of Object.values(methods)) {
          for (const [line, hits] of Object.entries(method.Lines ?? {})) {
            addLine(lines, Number(line), hits);
          }
        }
      }
      files.set(file, lines);
    }
  }
  return files;
}

function verify(label, report, changed, scope) {
  let covered = 0;
  let total = 0;
  const misses = [];

  for (const [file, sourceLines] of changed) {
    if (!file.startsWith(scope)) continue;
    const executable = report.get(file);
    if (!executable) continue;
    for (const line of sourceLines) {
      if (!executable.has(line)) continue;
      total += 1;
      if (executable.get(line) > 0) covered += 1;
      else misses.push(`${file}:${line}`);
    }
  }

  if (total === 0) {
    const inScope = [...changed.keys()].filter((file) =>
      file.startsWith(scope),
    );
    if (inScope.length === 0) {
      console.log(`${label}: no changed source files in this scope.`);
      return true;
    }
    // Files changed here, yet none of them appear in the coverage report. That
    // is the shape of a broken gate - a wrong scope, a path-casing mismatch, a
    // report read from the wrong run - not of a well-covered diff.
    console.error(
      `${label}: ${inScope.length} changed file(s) in scope, but none appear in the coverage report.`,
    );
    for (const file of inScope.slice(0, 20)) console.error(`  missing ${file}`);
    return false;
  }

  const percentage = (covered / total) * 100;
  console.log(
    `${label}: ${percentage.toFixed(2)}% changed-line coverage (${covered}/${total}).`,
  );
  if (percentage + Number.EPSILON >= threshold) return true;

  console.error(`Changed-line coverage must be at least ${threshold}%.`);
  for (const miss of misses.slice(0, 40)) console.error(`  uncovered ${miss}`);
  if (misses.length > 40) console.error(`  ...and ${misses.length - 40} more`);
  return false;
}

function verifyGlobalFloor(label, report, floor) {
  let covered = 0;
  let total = 0;
  for (const lines of report.values()) {
    for (const hits of lines.values()) {
      total += 1;
      if (hits > 0) covered += 1;
    }
  }

  if (total === 0) {
    console.error(`${label}: the report contains no executable lines.`);
    return false;
  }

  const percentage = (covered / total) * 100;
  console.log(
    `${label}: ${percentage.toFixed(2)}% global line coverage (${covered}/${total}).`,
  );
  if (percentage + Number.EPSILON >= floor) return true;

  console.error(
    `${label} global line coverage must remain at least ${floor}%.`,
  );
  return false;
}

const changes = changedLines(defaultBase());
let passed = true;

const frontendPath = args.get("--frontend");
if (frontendPath) {
  passed =
    verify("Frontend", readIstanbul(frontendPath), changes, "auth_ui/") &&
    passed;
}

const backendPath = args.get("--backend");
if (backendPath) {
  const backendReport = readCoverlet(backendPath);
  passed = verify("Backend", backendReport, changes, "auth/") && passed;
  const backendFloor = args.get("--backend-floor");
  if (backendFloor !== undefined) {
    passed =
      verifyGlobalFloor("Backend", backendReport, Number(backendFloor)) &&
      passed;
  }
}

if (!frontendPath && !backendPath) {
  throw new Error("Pass --frontend and/or --backend coverage report paths.");
}

if (!passed) process.exitCode = 1;
