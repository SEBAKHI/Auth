/**
 * Repository governance guards: the job "Repository governance" in
 * .github/workflows/ci.yml runs them with
 *   node --test "Tools/github/*.test.mjs"
 * from the repository root. They read this repository's own CI, Dependabot,
 * MSBuild and pnpm settings and fail when one of them silently weakens the
 * dependency-vulnerability checks (card S03). Later cards add their guards to
 * this file (G-S04, G-X06, G-S05, G-S07) and reuse its two readers.
 *
 * Shape of every guard: a pure function over text (or a list of paths) that
 * returns violation messages, each starting with the guard's name. Every guard
 * has two kinds of test: the real tracked files give no violation, and a
 * fixture that breaks one rule gives that rule's violation. Fixtures are
 * strings in this file or temporary files written at run time, never
 * committed files named like a manifest (the dependency graph would parse
 * them as real dependencies).
 *
 * Zero dependencies (node: built-ins only), so there is no YAML library. The
 * reader below accepts a named YAML subset: the constructs these files use.
 * Anything outside it is a violation naming the construct (G-S03g), never
 * skipped. File lists come from `git ls-files`, so untracked files
 * (node_modules, build output) never count, and CRLF is normalised first, so a
 * Windows checkout and the Linux CI checkout give the same result.
 *
 * How to run one locally and how to add a guard: Tools/github/README.md.
 */
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { basename, dirname, join, resolve } from "node:path";
import { describe, test } from "node:test";
import { fileURLToPath } from "node:url";
import { isDeepStrictEqual } from "node:util";

import {
  GHSA_PATTERN,
  PNPM_AUDIT_ARGS,
  checkAllowWindow,
  parseIsoDate,
  validateAllowList,
} from "./pnpm-audit-gate.mjs";

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");

const normalizeNewlines = (text) => text.replace(/\r\n/g, "\n");
const isMap = (value) => value !== null && typeof value === "object" && !Array.isArray(value);

// ---------------------------------------------------------------------------
// Bounded YAML reader
//
// Supported: block mappings indented with spaces; block sequences of scalars
// and of mappings (`- uses:`, `- name:`), also at the same indentation as
// their key; literal block scalars `|` and `|-`; plain, single-quoted and
// double-quoted scalars (double-quoted escapes: only \\ and \"); empty values
// (`workflow_dispatch:`); `#` comments on their own line or after a space
// outside quotes; `${{ }}` inside scalars; flow sequences and flow mappings on
// one line, nested (`[main]`, `{ interval: "weekly", day: "monday" }`).
// Unsupported, each a named error: anchor (&), alias (*), merge key (<<),
// tag (!), document markers (--- ...) and second documents, directives (%),
// folded scalars (>), block scalar indicators other than | and |- (|2, |+),
// complex keys (?), quoted keys, flow collections across lines, tab
// indentation, duplicate keys, multi-line plain or quoted scalars.
//
// Scalars stay strings (no YAML 1.1 booleans: `on:` is the key "on", `true`
// is "true"). Each mapping and sequence carries, under a non-enumerable
// symbol, the source line of each key or item and its same-line comment, so
// guards can read the `# vX.Y.Z` tag after a pinned `uses:` or the dated
// comment line directly above a key.
// ---------------------------------------------------------------------------

const META = Symbol("yaml-source");

class ReaderError extends Error {}

const unsupported = (construct, line) =>
  new ReaderError(`unsupported construct: ${construct} (line ${line})`);

const KEY_PATTERN = /^([A-Za-z0-9_][A-Za-z0-9_./-]*):(?= |$)/;
const isSequenceItem = (content) => content === "-" || content.startsWith("- ");

function withMeta(node, lines, comments) {
  Object.defineProperty(node, META, { value: { lines, comments }, enumerable: false });
  return node;
}

/** Source line (1-based) of a mapping key or a sequence item. */
const lineOf = (node, keyOrIndex) => node[META].lines[keyOrIndex];
/** Same-line comment after a mapping value or a sequence item, or null. */
const commentOf = (node, keyOrIndex) => node[META].comments[keyOrIndex];

function readQuoted(text, start, line) {
  const quote = text[start];
  let value = "";
  let i = start + 1;
  for (;;) {
    if (i >= text.length) throw unsupported("quoted scalar that does not close on the same line", line);
    const ch = text[i];
    if (quote === "'") {
      if (ch === "'") {
        if (text[i + 1] === "'") {
          value += "'";
          i += 2;
          continue;
        }
        return { value, end: i + 1 };
      }
    } else {
      if (ch === "\\") {
        const next = text[i + 1];
        if (next !== "\\" && next !== '"')
          throw unsupported(`escape "\\${next ?? ""}" in a double-quoted scalar (only \\\\ and \\" are supported)`, line);
        value += next;
        i += 2;
        continue;
      }
      if (ch === '"') return { value, end: i + 1 };
    }
    value += ch;
    i += 1;
  }
}

/** The rest of a line after a value: nothing, or a comment after a space. */
function trailingComment(rest, line, what) {
  if (/^[ \t]*$/.test(rest)) return null;
  const match = /^[ \t]+#(.*)$/.exec(rest);
  if (!match) throw unsupported(`text after ${what}`, line);
  return match[1].trim();
}

function checkPlainStart(value, line) {
  const first = value[0];
  if (first === "&") throw unsupported("anchor (&)", line);
  if (first === "*") throw unsupported("alias (*)", line);
  if (first === "!") throw unsupported("tag (!)", line);
  if (value === "?" || value.startsWith("? ")) throw unsupported("complex key (?)", line);
  if (value.startsWith("<<")) throw unsupported("merge key (<<)", line);
  if (first === "%" || first === "@" || first === "`") throw unsupported(`reserved indicator (${first})`, line);
  if (value === "-" || value.startsWith("- ")) throw unsupported("sequence entry after a key on the same line", line);
  if (/: /.test(value) || value.endsWith(":")) throw unsupported('plain scalar containing ": "', line);
}

function parseFlowLine(text, line) {
  let i = 0;
  const skipSpaces = () => {
    while (text[i] === " ") i += 1;
  };
  const open = () => unsupported("multi-line flow collection", line);

  function value() {
    skipSpaces();
    const ch = text[i];
    if (ch === undefined) throw open();
    if (ch === "[") {
      i += 1;
      const items = [];
      skipSpaces();
      if (text[i] === "]") {
        i += 1;
        return items;
      }
      for (;;) {
        items.push(value());
        skipSpaces();
        if (text[i] === ",") {
          i += 1;
          continue;
        }
        if (text[i] === "]") {
          i += 1;
          return items;
        }
        if (i >= text.length) throw open();
        throw unsupported(`unexpected "${text[i]}" in a flow sequence`, line);
      }
    }
    if (ch === "{") {
      i += 1;
      const map = {};
      skipSpaces();
      if (text[i] === "}") {
        i += 1;
        return map;
      }
      for (;;) {
        skipSpaces();
        if (i >= text.length) throw open();
        const match = KEY_PATTERN.exec(text.slice(i));
        if (!match) throw unsupported("flow mapping entry that is not 'key: value'", line);
        const key = match[1];
        if (Object.hasOwn(map, key) || key === "__proto__") throw unsupported(`duplicate key "${key}"`, line);
        i += match[0].length;
        map[key] = value();
        skipSpaces();
        if (text[i] === ",") {
          i += 1;
          continue;
        }
        if (text[i] === "}") {
          i += 1;
          return map;
        }
        if (i >= text.length) throw open();
        throw unsupported(`unexpected "${text[i]}" in a flow mapping`, line);
      }
    }
    if (ch === '"' || ch === "'") {
      const quoted = readQuoted(text, i, line);
      i = quoted.end;
      return quoted.value;
    }
    const start = i;
    while (i < text.length && !",[]{}".includes(text[i]) && !(text[i] === "#" && text[i - 1] === " ")) i += 1;
    const plain = text.slice(start, i).trim();
    if (plain === "") throw unsupported("empty entry in a flow collection", line);
    checkPlainStart(plain, line);
    return plain;
  }

  const result = value();
  return { value: result, comment: trailingComment(text.slice(i), line, "a flow collection") };
}

class SubsetParser {
  constructor(text) {
    const normalized = normalizeNewlines(text).replace(/^\uFEFF/, "");
    this.lines = normalized.split("\n").map((raw, index) => ({ raw, no: index + 1 }));
    this.pos = 0;
  }

  /** A structural line ({ indent, content, no }), or null for a blank or comment line. */
  structural(index) {
    const { raw, no } = this.lines[index];
    const lead = /^[ \t]*/.exec(raw)[0];
    const content = raw.slice(lead.length);
    if (content === "") return null;
    if (lead.includes("\t")) throw unsupported("tab indentation", no);
    if (content.startsWith("#")) return null;
    if (lead.length === 0 && /^(---|\.\.\.)(\s|$)/.test(content))
      throw unsupported("document marker (--- or ...) or a second document", no);
    if (lead.length === 0 && content.startsWith("%")) throw unsupported("directive (%)", no);
    return { indent: lead.length, content, no };
  }

  peek() {
    for (let i = this.pos; i < this.lines.length; i += 1) {
      const line = this.structural(i);
      if (line) {
        this.pos = i;
        return line;
      }
    }
    this.pos = this.lines.length;
    return null;
  }

  parseDocument() {
    const first = this.peek();
    if (!first) return null;
    if (first.indent !== 0) throw unsupported("document that does not start at column 0", first.no);
    const value = this.parseBlock();
    const extra = this.peek();
    if (extra) throw unsupported("content after the end of the document", extra.no);
    return value;
  }

  parseBlock() {
    const line = this.peek();
    return isSequenceItem(line.content) ? this.parseSequence(line.indent) : this.parseMapping(line.indent);
  }

  readKey(line) {
    const content = line.content;
    if (content === "?" || content.startsWith("? ")) throw unsupported("complex key (?)", line.no);
    if (content.startsWith("<<")) throw unsupported("merge key (<<)", line.no);
    if (content.startsWith("&")) throw unsupported("anchor (&)", line.no);
    if (content.startsWith("*")) throw unsupported("alias (*)", line.no);
    if (content.startsWith("!")) throw unsupported("tag (!)", line.no);
    if (content.startsWith('"') || content.startsWith("'")) throw unsupported("quoted key", line.no);
    if (content.startsWith("[") || content.startsWith("{"))
      throw unsupported("flow collection where a mapping key was expected", line.no);
    const match = KEY_PATTERN.exec(content);
    if (!match) throw unsupported("line that is not a 'key: value' pair", line.no);
    if (match[1] === "__proto__") throw unsupported('key "__proto__"', line.no);
    return match[1];
  }

  parseMapping(indent) {
    const map = withMeta({}, {}, {});
    for (;;) {
      const line = this.peek();
      if (!line || line.indent < indent) break;
      if (line.indent > indent) throw unsupported("unexpected indentation", line.no);
      if (isSequenceItem(line.content)) break;
      const key = this.readKey(line);
      if (Object.hasOwn(map, key)) throw unsupported(`duplicate key "${key}"`, line.no);
      this.pos += 1;
      map[META].lines[key] = line.no;
      const { value, comment } = this.parseValue(line.content.slice(key.length + 1), indent, line.no, true);
      map[key] = value;
      map[META].comments[key] = comment;
    }
    return map;
  }

  parseSequence(indent) {
    const items = withMeta([], [], []);
    for (;;) {
      const line = this.peek();
      if (!line || line.indent < indent) break;
      if (line.indent > indent) throw unsupported("unexpected indentation", line.no);
      if (!isSequenceItem(line.content)) break;
      const rest = line.content === "-" ? "" : line.content.slice(2);
      const inner = rest.replace(/^ +/, "");
      items[META].lines.push(line.no);
      if (inner !== "" && KEY_PATTERN.test(inner)) {
        // `- key: value`: the item is a mapping whose keys start at this column.
        const column = indent + (line.content.length - inner.length);
        this.lines[this.pos] = { raw: " ".repeat(column) + inner, no: line.no };
        items[META].comments.push(null);
        items.push(this.parseMapping(column));
      } else {
        if (isSequenceItem(inner)) throw unsupported("nested sequence on one line (- -)", line.no);
        this.pos += 1;
        const { value, comment } = this.parseValue(rest, indent, line.no, false);
        items.push(value);
        items[META].comments.push(comment);
      }
    }
    return items;
  }

  parseValue(text, parentIndent, lineNo, allowCompactSequence) {
    const trimmed = text.replace(/^ +/, "");
    if (trimmed.startsWith("\t")) throw unsupported("tab before a value", lineNo);
    if (trimmed === "" || trimmed.startsWith("#")) {
      const comment = trimmed.startsWith("#") ? trimmed.slice(1).trim() : null;
      const next = this.peek();
      if (next && next.indent > parentIndent) return { value: this.parseBlock(), comment };
      if (next && allowCompactSequence && next.indent === parentIndent && isSequenceItem(next.content))
        return { value: this.parseSequence(next.indent), comment };
      return { value: null, comment };
    }
    const first = trimmed[0];
    if (first === ">") throw unsupported("folded block scalar (>)", lineNo);
    if (first === "|") return this.parseLiteral(trimmed, parentIndent, lineNo);
    let result;
    if (first === "[" || first === "{") {
      result = parseFlowLine(trimmed, lineNo);
    } else if (first === '"' || first === "'") {
      const quoted = readQuoted(trimmed, 0, lineNo);
      result = { value: quoted.value, comment: trailingComment(trimmed.slice(quoted.end), lineNo, "a quoted scalar") };
    } else {
      const hash = trimmed.search(/[ \t]#/);
      const plain = (hash === -1 ? trimmed : trimmed.slice(0, hash)).replace(/[ \t]+$/, "");
      checkPlainStart(plain, lineNo);
      result = { value: plain, comment: hash === -1 ? null : trimmed.slice(hash).replace(/^[ \t]*#/, "").trim() };
    }
    const next = this.peek();
    if (next && next.indent > parentIndent)
      throw unsupported("continuation line (a multi-line scalar, or a block after an inline value)", next.no);
    return result;
  }

  parseLiteral(header, parentIndent, lineNo) {
    const match = /^\|(-?)(?:[ \t]+#(.*))?[ \t]*$/.exec(header);
    if (!match) {
      const indicator = header.split(/[ \t]/)[0];
      throw unsupported(`block scalar header "${indicator}" (only | and |- are supported)`, lineNo);
    }
    const strip = match[1] === "-";
    const content = [];
    let contentIndent = null;
    let i = this.pos;
    for (; i < this.lines.length; i += 1) {
      const { raw, no } = this.lines[i];
      if (/^[ \t]*$/.test(raw)) {
        content.push("");
        continue;
      }
      const spaces = /^ */.exec(raw)[0].length;
      if (contentIndent === null) {
        if (spaces <= parentIndent) break;
        if (raw[spaces] === "\t") throw unsupported("tab indentation", no);
        contentIndent = spaces;
      }
      if (spaces >= contentIndent) {
        content.push(raw.slice(contentIndent));
      } else if (spaces > parentIndent) {
        if (raw[spaces] === "\t") throw unsupported("tab indentation", no);
        throw unsupported("line less indented than the rest of its block scalar", no);
      } else {
        break;
      }
    }
    this.pos = i;
    while (content.length > 0 && content[content.length - 1] === "") content.pop();
    const value = content.join("\n") + (strip || content.length === 0 ? "" : "\n");
    return { value, comment: match[2] === undefined ? null : match[2].trim() };
  }
}

/** Parses the YAML subset; throws ReaderError naming any unsupported construct. */
function parseYamlSubset(text) {
  return new SubsetParser(text).parseDocument();
}

/** Reader for a GitHub Actions workflow: a mapping with a `jobs` mapping. */
function readWorkflow(text) {
  const doc = parseYamlSubset(text);
  if (!isMap(doc)) throw new ReaderError("not a workflow: the document is not a mapping");
  if (!isMap(doc.jobs)) throw new ReaderError("not a workflow: no jobs mapping");
  for (const [id, job] of Object.entries(doc.jobs))
    if (!isMap(job)) throw new ReaderError(`not a workflow: job "${id}" is not a mapping`);
  return doc;
}

/** Reader for .github/dependabot.yml: version plus a sequence of update blocks. */
function readDependabot(text) {
  const doc = parseYamlSubset(text);
  if (!isMap(doc)) throw new ReaderError("not a dependabot.yml: the document is not a mapping");
  if (!Array.isArray(doc.updates) || !doc.updates.every(isMap))
    throw new ReaderError("not a dependabot.yml: updates is not a sequence of mappings");
  return doc;
}

function readerViolation(guard, path, error) {
  if (error instanceof ReaderError) return `${guard}: ${path}: ${error.message}`;
  throw error;
}

const jobSteps = (job) => (Array.isArray(job?.steps) ? job.steps.filter(isMap) : []);
const runLines = (run) => (typeof run === "string" ? run.split("\n").map((line) => line.trim()) : []);
/** The `dotnet` command lines of a run value; other lines (S04's exit checks) are ignored. */
const dotnetLines = (run) => runLines(run).filter((line) => line.startsWith("dotnet "));
const restoreLines = (run) => runLines(run).filter((line) => line.startsWith("dotnet restore "));

function backendRestoreRun(ciDoc) {
  const step = jobSteps(ciDoc.jobs?.backend).find((s) => s.name === "Restore");
  return typeof step?.run === "string" ? step.run : null;
}

// ---------------------------------------------------------------------------
// Repository access (tracked files only)
// ---------------------------------------------------------------------------

function gitLsFiles() {
  const output = execFileSync("git", ["ls-files", "-z"], {
    cwd: REPO_ROOT,
    encoding: "utf8",
    maxBuffer: 64 * 1024 * 1024,
  });
  return output.split("\0").filter(Boolean);
}

const readRepoFile = (path) => readFileSync(join(REPO_ROOT, path), "utf8");

let snapshot;
function repository() {
  if (snapshot) return snapshot;
  const tracked = gitLsFiles();
  const read = (path) => ({ path, text: readRepoFile(path) });
  snapshot = {
    tracked,
    workflows: tracked.filter((p) => /^\.github\/workflows\/[^/]+\.ya?ml$/.test(p)).map(read),
    msbuild: tracked.filter((p) => /\.(csproj|props|targets)$/i.test(p)).map(read),
    ci: readRepoFile(".github/workflows/ci.yml"),
    audit: readRepoFile(".github/workflows/dependency-audit.yml"),
    dependabot: readRepoFile(".github/dependabot.yml"),
    lockfile: readRepoFile("Auth_UI/pnpm-lock.yaml"),
    workspace: readRepoFile("Auth_UI/pnpm-workspace.yaml"),
    packageJson: readRepoFile("Auth_UI/package.json"),
    npmrc: tracked.includes("Auth_UI/.npmrc") ? readRepoFile("Auth_UI/.npmrc") : null,
    allowList: readRepoFile("Tools/github/pnpm-audit-allow.json"),
  };
  return snapshot;
}

// ---------------------------------------------------------------------------
// G-S03a  .github/dependabot.yml keeps the K4 shape
// ---------------------------------------------------------------------------

const DEPENDABOT_BLOCKS = [
  { ecosystem: "nuget", key: "directories", expected: ["/Auth/*"], other: "directory" },
  { ecosystem: "npm", key: "directory", expected: "/Auth_UI", other: "directories" },
];

function walkKeys(node, visit) {
  if (Array.isArray(node)) node.forEach((item) => walkKeys(item, visit));
  else if (isMap(node))
    for (const [key, value] of Object.entries(node)) {
      visit(key);
      walkKeys(value, visit);
    }
}

function guardDependabot(text) {
  const violations = [];
  const add = (message) => violations.push(`G-S03a: ${message}`);
  let doc;
  try {
    doc = readDependabot(text);
  } catch (error) {
    return [readerViolation("G-S03a", ".github/dependabot.yml", error)];
  }
  const lines = normalizeNewlines(text).split("\n");

  if (doc.version !== "2") add(`version must be 2, found ${JSON.stringify(doc.version)}`);
  walkKeys(doc, (key) => {
    if (key === "reviewers") add('"reviewers" is not a Dependabot option any more; remove it');
    if (key === "target-branch") add('"target-branch" detaches the block\'s options from security updates; remove it');
  });

  for (const spec of DEPENDABOT_BLOCKS) {
    const blocks = doc.updates.filter((block) => block["package-ecosystem"] === spec.ecosystem);
    if (blocks.length !== 1) {
      add(`expected exactly one "${spec.ecosystem}" block, found ${blocks.length}`);
      continue;
    }
    const [block] = blocks;
    const at = `${spec.ecosystem} block`;
    if (!isDeepStrictEqual(block[spec.key], spec.expected))
      add(`${at}: ${spec.key} must be ${JSON.stringify(spec.expected)}, found ${JSON.stringify(block[spec.key])}`);
    if (spec.other in block) add(`${at}: use ${spec.key}, not ${spec.other}`);
    if (!isMap(block.schedule) || block.schedule.interval !== "weekly") add(`${at}: schedule.interval must be weekly`);
    if (!isMap(block.cooldown) || !/^[1-9]\d*$/.test(block.cooldown["default-days"] ?? ""))
      add(`${at}: cooldown.default-days must be a positive number of days`);
    const groups = isMap(block.groups) ? Object.values(block.groups).filter(isMap) : [];
    for (const appliesTo of ["version-updates", "security-updates"])
      if (!groups.some((group) => group["applies-to"] === appliesTo))
        add(`${at}: needs a group with applies-to: ${appliesTo}`);
    const prefix = isMap(block["commit-message"]) ? block["commit-message"].prefix : undefined;
    if (typeof prefix !== "string" || prefix.trim() === "") add(`${at}: commit-message.prefix is required`);
    if (!/^[0-5]$/.test(block["open-pull-requests-limit"] ?? ""))
      add(`${at}: open-pull-requests-limit must be between 0 and 5, found ${JSON.stringify(block["open-pull-requests-limit"])}`);
  }

  doc.updates.forEach((block, index) => {
    if (!("ignore" in block)) return;
    const at = `${block["package-ecosystem"] ?? `update ${index + 1}`} block`;
    const ignore = block.ignore;
    if (!Array.isArray(ignore)) {
      add(`${at}: ignore must be a list`);
      return;
    }
    ignore.forEach((entry, i) => {
      const name = isMap(entry) ? entry["dependency-name"] : undefined;
      if (typeof name !== "string" || name.trim() === "") {
        add(`${at}: ignore entry ${i + 1} has no dependency-name`);
        return;
      }
      if (name.trim() === "*")
        add(`${at}: ignore entry with dependency-name "*" would silence every update, security updates included`);
      const above = lines[lineOf(ignore, i) - 2] ?? "";
      const comment = /^\s*# ignore (\S+) (\d{4}-\d{2}-\d{2}) (\S.*)$/.exec(above);
      if (!comment || comment[1] !== name || parseIsoDate(comment[2]) === null)
        add(`${at}: ignore entry "${name}" needs "# ignore ${name} YYYY-MM-DD <reason>" on the line directly above it`);
    });
  });
  return violations;
}

// ---------------------------------------------------------------------------
// G-S03b  manifests sit where Dependabot and pnpm look for them
// ---------------------------------------------------------------------------

function workspacePackagePatterns(workspaceText) {
  const doc = parseYamlSubset(workspaceText);
  const packages = isMap(doc) ? doc.packages : undefined;
  if (!Array.isArray(packages) || !packages.every((p) => typeof p === "string"))
    throw new ReaderError("pnpm-workspace.yaml has no packages list");
  return packages;
}

function guardManifestLocations(paths, packagePatterns) {
  const violations = [];
  const add = (message) => violations.push(`G-S03b: ${message}`);
  for (const path of paths)
    if (/\.csproj$/i.test(path) && !/^Auth\/[^/]+\/[^/]+\.csproj$/.test(path))
      add(`${path} is not directly inside an Auth/<folder>/, where Dependabot's "/Auth/*" discovery reads`);

  const folders = [];
  for (const pattern of packagePatterns) {
    const match = /^([A-Za-z0-9_.-]+)\/\*$/.exec(pattern);
    if (match) folders.push(match[1]);
    else add(`pnpm-workspace.yaml package pattern "${pattern}" is not of the form <folder>/*`);
  }
  for (const path of paths) {
    if (!path.startsWith("Auth_UI/") || !/(^|\/)package\.json$/.test(path) || path === "Auth_UI/package.json") continue;
    const match = /^Auth_UI\/([^/]+)\/[^/]+\/package\.json$/.exec(path);
    if (!match || !folders.includes(match[1]))
      add(`${path} is neither the workspace root nor in a pnpm-workspace.yaml package folder`);
  }
  return violations;
}

// ---------------------------------------------------------------------------
// G-S03c  pnpm-lock.yaml is one YAML document
// ---------------------------------------------------------------------------

function guardLockfile(text) {
  const violations = [];
  const normalized = normalizeNewlines(text);
  if (normalized.startsWith("-"))
    violations.push("G-S03c: Auth_UI/pnpm-lock.yaml starts with '-' (a second YAML document; Dependabot and pnpm disagree on it)");
  const count = normalized.split("\n").filter((line) => line.startsWith("lockfileVersion:")).length;
  if (count !== 1)
    violations.push(`G-S03c: Auth_UI/pnpm-lock.yaml has ${count} lockfileVersion lines at column 0, expected exactly 1`);
  return violations;
}

// ---------------------------------------------------------------------------
// G-S03d  nothing inside Auth_UI changes what the audit checks or where
// ---------------------------------------------------------------------------

function acceptsOnlyPnpm11(range) {
  if (typeof range !== "string") return false;
  const value = range.trim();
  return (
    /^[\^~]?11(\.(\d+|x|\*)){0,2}$/.test(value) || /^>=\s*11(\.\d+){0,2}\s+<\s*12(\.0){0,2}$/.test(value)
  );
}

function guardPnpmSettings({ workspace, packageJson, npmrc }) {
  const violations = [];
  const add = (message) => violations.push(`G-S03d: ${message}`);
  try {
    const doc = parseYamlSubset(workspace);
    for (const key of isMap(doc) ? Object.keys(doc) : [])
      if (/^audit/i.test(key))
        add(`Auth_UI/pnpm-workspace.yaml sets "${key}"; an audit setting there can silence advisories (use Tools/github/pnpm-audit-allow.json)`);
  } catch (error) {
    violations.push(readerViolation("G-S03d", "Auth_UI/pnpm-workspace.yaml", error));
  }

  let pkg;
  try {
    pkg = JSON.parse(packageJson);
  } catch {
    add("Auth_UI/package.json is not valid JSON");
  }
  if (isMap(pkg)) {
    if (isMap(pkg.pnpm) && "auditConfig" in pkg.pnpm) add('Auth_UI/package.json sets "pnpm.auditConfig"');
    if ("devEngines" in pkg)
      add('Auth_UI/package.json sets "devEngines"; pnpm reads it before any command and may switch its own version');
    if ("packageManager" in pkg && !(typeof pkg.packageManager === "string" && pkg.packageManager.startsWith("pnpm@11.")))
      add(`Auth_UI/package.json "packageManager" must be absent or pnpm@11.x, found ${JSON.stringify(pkg.packageManager)}`);
    if (isMap(pkg.engines) && "pnpm" in pkg.engines && !acceptsOnlyPnpm11(pkg.engines.pnpm))
      add(`Auth_UI/package.json "engines.pnpm" must be absent or a range that accepts only pnpm 11, found ${JSON.stringify(pkg.engines.pnpm)}`);
  }

  if (npmrc !== null && /^[ \t]*(@[^:\s]+:)?registry[ \t]*=/m.test(normalizeNewlines(npmrc)))
    add("Auth_UI/.npmrc sets a registry; pnpm audit sends the lockfile there and reads advisories from it");
  return violations;
}

// ---------------------------------------------------------------------------
// G-S03e  K5 permissions allowlist
// ---------------------------------------------------------------------------

/**
 * The only job-level permissions blocks allowed anywhere (K5). Data, so later
 * cards add a row instead of rewriting the guard (S31 adds release.yml `gate`
 * and `release`).
 */
const JOB_PERMISSION_ALLOWLIST = [
  // S04: CodeQL uploads SARIF.
  { file: "codeql.yml", job: "analyze", permissions: { contents: "read", "security-events": "write" } },
];

function guardPermissions(workflows, allowlist = JOB_PERMISSION_ALLOWLIST) {
  const violations = [];
  const add = (message) => violations.push(`G-S03e: ${message}`);
  for (const { path, text } of workflows) {
    let doc;
    try {
      doc = readWorkflow(text);
    } catch (error) {
      violations.push(readerViolation("G-S03e", path, error));
      continue;
    }
    if (!isDeepStrictEqual(doc.permissions, { contents: "read" }))
      add(`${path}: top-level permissions must be exactly "contents: read", found ${JSON.stringify(doc.permissions ?? null)}`);
    for (const [id, job] of Object.entries(doc.jobs)) {
      if (!("permissions" in job)) continue;
      const row = allowlist.find((entry) => entry.file === basename(path) && entry.job === id);
      if (!row) add(`${path}: job "${id}" adds a permissions block; only the K5 allowlist may`);
      else if (!isDeepStrictEqual(job.permissions, row.permissions))
        add(`${path}: job "${id}" permissions must be exactly ${JSON.stringify(row.permissions)}`);
    }
  }
  return violations;
}

// ---------------------------------------------------------------------------
// G-S03f  job names are unique across all workflows (literal, not matrix-expanded)
// ---------------------------------------------------------------------------

function guardJobNames(workflows) {
  const places = new Map();
  for (const { path, text } of workflows) {
    let doc;
    try {
      doc = readWorkflow(text);
    } catch {
      continue; // G-S03g reports it
    }
    for (const [id, job] of Object.entries(doc.jobs)) {
      const name = typeof job.name === "string" ? job.name : id;
      places.set(name, [...(places.get(name) ?? []), `${path} (${id})`]);
    }
  }
  return [...places]
    .filter(([, where]) => where.length > 1)
    .map(([name, where]) => `G-S03f: job name "${name}" is used more than once: ${where.join(", ")}`);
}

// ---------------------------------------------------------------------------
// G-S03g  every workflow and dependabot.yml is inside the reader's subset
// ---------------------------------------------------------------------------

function guardReadable(files) {
  const violations = [];
  for (const { path, text, read } of files) {
    try {
      read(text);
    } catch (error) {
      violations.push(readerViolation("G-S03g", path, error));
    }
  }
  return violations;
}

// ---------------------------------------------------------------------------
// G-S03h  MSBuild: explicit NuGetAudit, escalation only under AuditPipeline
// ---------------------------------------------------------------------------

const PROPS_PATH = "Auth/Directory.Build.props";
const AUDIT_CODES = ["NU1900", "NU1901", "NU1902", "NU1903", "NU1904"];
const PIPELINE_CODES = ["NU1900", "NU1902", "NU1903", "NU1904"];
const CSPROJ_ONLY = "'$(msbuildprojectextension)' == '.csproj'";
const PIPELINE_ON = "'$(auditpipeline)' == 'true'";
const PIPELINE_OFF = "'$(auditpipeline)' != 'true'";
const AUDIT_CODE = /NU190\d/i;

const normalizeCondition = (condition) => condition.replace(/\s+/g, " ").trim().toLowerCase();
/** Replaces XML comments with spaces, keeping every offset and line break. */
const blankXmlComments = (xml) => xml.replace(/<!--[\s\S]*?-->/g, (comment) => comment.replace(/[^\n]/g, " "));
const codesIn = (text) => new Set((text.match(/NU\d{4}/gi) ?? []).map((code) => code.toUpperCase()));

function attributeOf(attributes, name) {
  const match = new RegExp(`\\b${name}\\s*=\\s*(?:"([^"]*)"|'([^']*)')`, "i").exec(attributes ?? "");
  return match ? (match[1] ?? match[2]) : null;
}

function xmlElements(xml, namePattern) {
  const pattern = new RegExp(`<(${namePattern})\\b([^>]*?)(?:/>|>([\\s\\S]*?)</\\1\\s*>)`, "gi");
  return [...xml.matchAll(pattern)].map((m) => ({ name: m[1], attributes: m[2], value: (m[3] ?? "").trim(), index: m.index }));
}

function propertyGroups(xml) {
  return [...xml.matchAll(/<PropertyGroup\b([^>]*)>([\s\S]*?)<\/PropertyGroup\s*>/gi)].map((m) => ({
    condition: normalizeCondition(attributeOf(m[1], "Condition") ?? ""),
    body: m[2],
    start: m.index,
    end: m.index + m[0].length,
  }));
}

const groupAt = (groups, index) => groups.find((group) => index > group.start && index < group.end) ?? null;
const conditionedOn = (group, clause) => group !== null && group.condition.includes(clause) && !/\bor\b/.test(group.condition);

function guardMsbuild(files) {
  const violations = [];
  const add = (message) => violations.push(`G-S03h: ${message}`);
  const props = files.find((file) => file.path === PROPS_PATH);
  if (!props) add(`${PROPS_PATH} is missing`);

  for (const { path, text } of files) {
    const raw = normalizeNewlines(text);
    const xml = blankXmlComments(raw);
    const groups = propertyGroups(xml);
    const isProps = path === PROPS_PATH;

    for (const element of xmlElements(xml, "NuGetAudit[A-Za-z]*")) {
      if (!isProps) add(`${path}: <${element.name}> is allowed only in ${PROPS_PATH}`);
      else if (!/^NuGetAuditSuppress$/i.test(element.name) && groupAt(groups, element.index)?.condition !== CSPROJ_ONLY)
        add(`${path}: <${element.name}> may be set only in the '.csproj' PropertyGroup`);
    }

    for (const element of xmlElements(xml, "NoWarn"))
      if (AUDIT_CODE.test(element.value)) add(`${path}: <NoWarn> hides NU190x audit warnings`);
    for (const match of xml.matchAll(/\sNoWarn\s*=\s*(?:"([^"]*)"|'([^']*)')/gi))
      if (AUDIT_CODE.test(match[1] ?? match[2])) add(`${path}: a NoWarn attribute hides NU190x audit warnings`);

    for (const element of xmlElements(xml, "(?:MSBuild)?WarningsAsErrors"))
      if (AUDIT_CODE.test(element.value) && !conditionedOn(groupAt(groups, element.index), PIPELINE_ON))
        add(`${path}: <${element.name}> escalates NU190x outside a PropertyGroup conditioned on ${PIPELINE_ON}; a new advisory would then fail every build`);

    const exceptions = [];
    for (const element of xmlElements(xml, "WarningsNotAsErrors")) {
      if (!AUDIT_CODE.test(element.value)) continue;
      if (conditionedOn(groupAt(groups, element.index), PIPELINE_OFF)) exceptions.push(codesIn(element.value));
      else
        add(`${path}: <WarningsNotAsErrors> with NU190x must sit in a PropertyGroup conditioned on ${PIPELINE_OFF}, or the Dependency audit loses its errors`);
    }

    for (const element of xmlElements(xml, "TreatWarningsAsErrors")) {
      if (element.value.toLowerCase() === "false") continue;
      if (!exceptions.some((codes) => AUDIT_CODES.every((code) => codes.has(code))))
        add(`${path}: <TreatWarningsAsErrors>${element.value}</TreatWarningsAsErrors> needs <WarningsNotAsErrors> with ${AUDIT_CODES.join(";")} in a PropertyGroup conditioned on ${PIPELINE_OFF} in the same file; otherwise every restore, the required backend job included, fails on a new advisory`);
    }

    const rawLines = raw.split("\n");
    for (const element of xmlElements(xml, "NuGetAuditSuppress")) {
      if (!isProps) continue;
      const url = (attributeOf(element.attributes, "Include") ?? "").trim();
      const advisory = /^https:\/\/github\.com\/advisories\/(GHSA-[0-9a-z]{4}-[0-9a-z]{4}-[0-9a-z]{4})$/.exec(url);
      if (!advisory) {
        add(`${path}: <NuGetAuditSuppress Include="${url}"> must name https://github.com/advisories/GHSA-xxxx-xxxx-xxxx`);
        continue;
      }
      const lineIndex = raw.slice(0, element.index).split("\n").length - 1;
      const comment = /^\s*<!--\s*allow (\S+) (\S+) until (\S+) (\S.*?)\s*-->\s*$/.exec(rawLines[lineIndex - 1] ?? "");
      if (!comment)
        add(`${path}: ${advisory[1]} needs "<!-- allow ${advisory[1]} YYYY-MM-DD until YYYY-MM-DD <reason> -->" on the line directly above it`);
      else if (comment[1] !== advisory[1])
        add(`${path}: the allow comment names ${comment[1]} but the item suppresses ${advisory[1]}`);
      else {
        const window = checkAllowWindow(comment[2], comment[3]);
        if (window) add(`${path}: ${advisory[1]}: ${window}`);
      }
    }
  }

  if (props) {
    const xml = blankXmlComments(normalizeNewlines(props.text));
    const groups = propertyGroups(xml);
    for (const [name, value] of Object.entries({ NuGetAudit: "true", NuGetAuditMode: "all", NuGetAuditLevel: "low" })) {
      const found = xmlElements(xml, name);
      if (found.length !== 1 || found[0].value.toLowerCase() !== value || groupAt(groups, found[0].index)?.condition !== CSPROJ_ONLY)
        add(`${PROPS_PATH}: <${name}>${value}</${name}> must be set exactly once, in the '.csproj' PropertyGroup`);
    }
    const escalated = groups
      .filter((group) => group.condition === `${CSPROJ_ONLY} and ${PIPELINE_ON}`)
      .flatMap((group) => xmlElements(group.body, "WarningsAsErrors").map((element) => codesIn(element.value)));
    for (const code of PIPELINE_CODES)
      if (!escalated.some((codes) => codes.has(code)))
        add(`${PROPS_PATH}: the AuditPipeline PropertyGroup must add ${code} to WarningsAsErrors`);
  }
  return violations;
}

// ---------------------------------------------------------------------------
// G-S03i  audit commands, environment and triggers
// ---------------------------------------------------------------------------

const forbiddenEnvKey = (key) =>
  /^(NoWarn|WarningsNotAsErrors|TreatWarningsAsErrors|AuditPipeline)$/i.test(key) || /^NuGetAudit/i.test(key);
const RESTORE_FLAG = /(^|\s)(?:[-/]{1,2}p(?:roperty)?(?=[:\s=]|$)|[-/]{1,2}warnaserror|[-/]err(?=[:\s]|$))/i;
const PNPM_REQUIRED_ARGS = ["audit", "--json", "--audit-level", "moderate", "--fail-if-no-match", "--ignore-pnpmfile"];
const PNPM_FORBIDDEN_ARGS = ["--ignore-registry-errors", "--fix", "--ignore", "--ignore-unfixable", "--interactive", "-i", "--registry"];
const PNPM_GATE_RUN = "node ../Tools/github/pnpm-audit-gate.mjs";
const AUDIT_PUSH_PATHS = [
  "Auth/**/*.csproj",
  "Auth/**/*.props",
  "Auth/**/*.targets",
  "Auth/Directory.Build.props",
  "Auth_UI/pnpm-lock.yaml",
  "Tools/github/pnpm-audit-gate.mjs",
  "Tools/github/pnpm-audit-allow.json",
  ".github/workflows/dependency-audit.yml",
];

function envBlocks(doc) {
  const blocks = [["workflow", doc.env]];
  for (const [id, job] of Object.entries(doc.jobs)) {
    blocks.push([`job ${id}`, job.env]);
    jobSteps(job).forEach((step, i) => blocks.push([`job ${id} step ${i + 1}`, step.env]));
  }
  return blocks.filter(([, env]) => env !== undefined);
}

function checkEnv(file, doc, add) {
  for (const [where, env] of envBlocks(doc)) {
    if (!isMap(env)) {
      add(`${file} ${where}: env must be a mapping`);
      continue;
    }
    for (const key of Object.keys(env))
      if (forbiddenEnvKey(key)) add(`${file} ${where}: env ${key} changes what the NuGet audit reports`);
  }
}

function guardAuditCommands({ ci, audit, pnpmArgs }) {
  const violations = [];
  const add = (message) => violations.push(`G-S03i: ${message}`);
  let ciDoc;
  let auditDoc;
  try {
    ciDoc = readWorkflow(ci);
  } catch (error) {
    return [readerViolation("G-S03i", ".github/workflows/ci.yml", error)];
  }
  try {
    auditDoc = readWorkflow(audit);
  } catch (error) {
    return [readerViolation("G-S03i", ".github/workflows/dependency-audit.yml", error)];
  }

  // ci.yml: the required backend job keeps NU190x as warnings.
  if (/AuditPipeline/i.test(ci)) add("ci.yml mentions AuditPipeline; only dependency-audit.yml may escalate advisories");
  checkEnv("ci.yml", ciDoc, add);
  const restores = restoreLines(backendRestoreRun(ciDoc));
  if (restores.length === 0) add("ci.yml: the backend job's Restore step has no 'dotnet restore' line");
  for (const line of restores)
    if (RESTORE_FLAG.test(line.slice("dotnet restore ".length)))
      add(`ci.yml Restore: "${line}" passes a property or a warnaserror flag`);
  const ciProjects = restores.map((line) => line.split(/\s+/)[2]);

  // dependency-audit.yml
  checkEnv("dependency-audit.yml", auditDoc, add);
  if (isMap(auditDoc.defaults) && isMap(auditDoc.defaults.run) && "shell" in auditDoc.defaults.run)
    add("dependency-audit.yml: defaults.run.shell can change how the audit exits; remove it");
  for (const id of ["nuget-audit", "pnpm-audit"]) {
    const job = auditDoc.jobs[id];
    if (!job) {
      add(`dependency-audit.yml: job ${id} is missing`);
      continue;
    }
    if ("continue-on-error" in job) add(`dependency-audit.yml ${id}: continue-on-error turns a failed audit green`);
    if (isMap(job.defaults) && isMap(job.defaults.run) && "shell" in job.defaults.run)
      add(`dependency-audit.yml ${id}: defaults.run.shell can change how the audit exits`);
    jobSteps(job).forEach((step, i) => {
      if ("continue-on-error" in step) add(`dependency-audit.yml ${id} step ${i + 1}: continue-on-error turns a failed audit green`);
      if ("shell" in step) add(`dependency-audit.yml ${id} step ${i + 1}: shell can change how the audit exits`);
    });
  }

  const nuget = auditDoc.jobs["nuget-audit"];
  if (nuget) {
    const audited = [];
    for (const step of jobSteps(nuget).filter((s) => "run" in s)) {
      const match = typeof step.run === "string" ? /^dotnet restore (\S+) --force -p:AuditPipeline=true$/.exec(step.run) : null;
      if (match) audited.push(match[1]);
      else add(`dependency-audit.yml nuget-audit: run ${JSON.stringify(step.run)} is not exactly "dotnet restore <project> --force -p:AuditPipeline=true"`);
    }
    if (!isDeepStrictEqual([...audited].sort(), [...ciProjects].sort()))
      add(`dependency-audit.yml nuget-audit restores [${audited.join(", ")}], ci.yml restores [${ciProjects.join(", ")}]; each ci.yml project must be audited exactly once`);
  }

  const pnpm = auditDoc.jobs["pnpm-audit"];
  if (pnpm) {
    const runs = jobSteps(pnpm).filter((s) => "run" in s);
    if (runs.length !== 1 || runs[0].run !== PNPM_GATE_RUN)
      add(`dependency-audit.yml pnpm-audit: exactly one run step, "${PNPM_GATE_RUN}", found ${JSON.stringify(runs.map((s) => s.run))}`);
  }
  const args = [...pnpmArgs];
  for (const required of PNPM_REQUIRED_ARGS)
    if (!args.includes(required)) add(`PNPM_AUDIT_ARGS lacks ${required}`);
  if (args[args.indexOf("--audit-level") + 1] !== "moderate") add("PNPM_AUDIT_ARGS: --audit-level must be followed by moderate");
  for (const arg of args)
    if (PNPM_FORBIDDEN_ARGS.some((flag) => arg === flag || arg.startsWith(`${flag}=`)))
      add(`PNPM_AUDIT_ARGS contains ${arg}, which turns a failed or unreadable audit green or writes configuration`);

  // Triggers (K1, K9.4).
  const on = auditDoc.on;
  if (!isMap(on) || !isDeepStrictEqual(Object.keys(on).sort(), ["push", "schedule", "workflow_dispatch"]))
    add(`dependency-audit.yml: triggers must be exactly push, schedule and workflow_dispatch, found ${JSON.stringify(isMap(on) ? Object.keys(on) : on)}`);
  else {
    const push = on.push;
    if (!isMap(push) || !isDeepStrictEqual(push.branches, ["main"])) add("dependency-audit.yml: push must run on branches [main]");
    if (isMap(push) && Object.keys(push).some((key) => key !== "branches" && key !== "paths"))
      add("dependency-audit.yml: push takes only branches and paths");
    const paths = isMap(push) && Array.isArray(push.paths) ? push.paths : [];
    for (const path of AUDIT_PUSH_PATHS)
      if (!paths.includes(path)) add(`dependency-audit.yml: push paths must include '${path}'`);
    const schedule = on.schedule;
    if (!Array.isArray(schedule) || schedule.length !== 1 || !isDeepStrictEqual(schedule[0], { cron: "41 4 * * *" }))
      add("dependency-audit.yml: schedule must be the single daily cron '41 4 * * *'");
    if (on.workflow_dispatch !== null) add("dependency-audit.yml: workflow_dispatch takes no inputs");
  }
  return violations;
}

// ---------------------------------------------------------------------------
// G-S03j  the "Dependency review" job keeps its designed inputs
// ---------------------------------------------------------------------------

const REVIEW_INPUTS = {
  "fail-on-severity": "moderate",
  "vulnerability-check": "true",
  "license-check": "false",
  "show-openssf-scorecard": "false",
  "comment-summary-in-pr": "never",
  "warn-only": "false",
};
const REVIEW_SCOPES = ["development", "runtime", "unknown"];
/** The two accepted job conditions; G4 in G-S05 accepts exactly the same two. */
const REVIEW_IF_K1 = "github.event_name == 'pull_request'";
const REVIEW_IF_FALLBACK = "github.event_name == 'pull_request' && github.actor != 'dependabot[bot]'";

function guardDependencyReview(ci) {
  const violations = [];
  const add = (message) => violations.push(`G-S03j: ${message}`);
  let doc;
  try {
    doc = readWorkflow(ci);
  } catch (error) {
    return [readerViolation("G-S03j", ".github/workflows/ci.yml", error)];
  }
  const lines = normalizeNewlines(ci).split("\n");
  const job = doc.jobs["dependency-review"];
  if (!job) return ["G-S03j: ci.yml has no dependency-review job"];

  if (job.name !== "Dependency review") add(`the job name must be "Dependency review", found ${JSON.stringify(job.name)}`);
  if ("permissions" in job) add("the job must not carry a permissions block (contents: read comes from the workflow)");
  if ("continue-on-error" in job) add("continue-on-error on the job turns a failed review green");
  if (job.if === REVIEW_IF_FALLBACK) {
    const above = /^\s*# P2-R3 fallback (\S+) (\S.*)$/.exec(lines[lineOf(job, "if") - 2] ?? "");
    if (!above || parseIsoDate(above[1]) === null)
      add('the P2-R3 fallback condition needs "# P2-R3 fallback YYYY-MM-DD <reason naming the failed M-8 run>" on the line directly above it');
  } else if (job.if !== REVIEW_IF_K1) {
    add(`the job condition must be exactly "${REVIEW_IF_K1}" (or the dated P2-R3 fallback), found ${JSON.stringify(job.if)}`);
  }

  const steps = jobSteps(job);
  steps.forEach((step, i) => {
    if ("continue-on-error" in step) add(`step ${i + 1}: continue-on-error turns a failed review green`);
  });
  const step = steps.find((s) => typeof s.uses === "string" && s.uses.startsWith("actions/dependency-review-action@"));
  if (!step) return [...violations, "G-S03j: no step uses actions/dependency-review-action"];
  if (!/^actions\/dependency-review-action@[0-9a-f]{40}$/.test(step.uses))
    add(`uses must pin a 40-character commit SHA, found "${step.uses}"`);
  const tag = commentOf(step, "uses");
  if (typeof tag !== "string" || !/^v\d+\.\d+\.\d+$/.test(tag))
    add(`uses needs its release tag as a same-line comment ("# vX.Y.Z"), found ${JSON.stringify(tag)}`);

  const inputs = isMap(step.with) ? step.with : {};
  for (const [key, expected] of Object.entries(REVIEW_INPUTS))
    if (inputs[key] !== expected) add(`with.${key} must be "${expected}", found ${JSON.stringify(inputs[key])}`);
  const scopes =
    typeof inputs["fail-on-scopes"] === "string"
      ? inputs["fail-on-scopes"].split(",").map((s) => s.trim()).filter(Boolean).sort()
      : [];
  if (!isDeepStrictEqual(scopes, REVIEW_SCOPES))
    add(`with.fail-on-scopes must be "runtime, development, unknown", found ${JSON.stringify(inputs["fail-on-scopes"])}`);
  const designed = new Set([...Object.keys(REVIEW_INPUTS), "fail-on-scopes", "allow-ghsas"]);
  for (const key of Object.keys(inputs))
    if (!designed.has(key)) add(`with.${key} is not a designed input (a config file or another input could override the designed ones)`);

  if ("allow-ghsas" in inputs) {
    const value = inputs["allow-ghsas"];
    const keyLine = lineOf(inputs, "allow-ghsas");
    if (typeof value !== "string" || value.includes("\n") || /allow-ghsas:\s*[|>]/.test(lines[keyLine - 1]))
      add("allow-ghsas must be a one-line value");
    const listed = typeof value === "string" ? value.split(",").map((s) => s.trim()).filter(Boolean) : [];
    for (const ghsa of listed) if (!GHSA_PATTERN.test(ghsa)) add(`allow-ghsas: "${ghsa}" is not a GHSA identifier`);
    const commented = [];
    for (let j = keyLine - 2; j >= 0 && /^\s*# allow\s/.test(lines[j]); j -= 1) {
      const match = /^\s*# allow (\S+) (\S+) until (\S+) (\S.*)$/.exec(lines[j]);
      if (!match || !GHSA_PATTERN.test(match[1])) {
        add(`malformed allow comment "${lines[j].trim()}"; use "# allow GHSA-xxxx-xxxx-xxxx YYYY-MM-DD until YYYY-MM-DD <reason>"`);
        continue;
      }
      const window = checkAllowWindow(match[2], match[3]);
      if (window) add(`${match[1]}: ${window}`);
      commented.push(match[1]);
    }
    for (const ghsa of new Set(listed))
      if (!commented.includes(ghsa)) add(`allow-ghsas lists ${ghsa} without a dated "# allow" comment line directly above the key`);
    for (const ghsa of new Set(commented))
      if (!listed.includes(ghsa)) add(`an "# allow ${ghsa}" comment has no matching entry in allow-ghsas`);
  }
  return violations;
}

// ---------------------------------------------------------------------------
// G-S03k  Tools/github/pnpm-audit-allow.json is well formed
// ---------------------------------------------------------------------------

function guardAllowList(text) {
  let value;
  try {
    value = JSON.parse(text.replace(/^\uFEFF/, ""));
  } catch {
    return ["G-S03k: Tools/github/pnpm-audit-allow.json is not valid JSON"];
  }
  return validateAllowList(value).map((problem) => `G-S03k: Tools/github/pnpm-audit-allow.json: ${problem}`);
}

// ===========================================================================
// Tests
// ===========================================================================

/** Replaces one exact piece of a fixture; fails if the piece is not there. */
function mutate(text, from, to) {
  assert.ok(text.includes(from), `fixture anchor not found: ${JSON.stringify(from.slice(0, 80))}`);
  return text.replace(from, to);
}

const expectViolation = (violations, pattern) =>
  assert.ok(
    violations.some((v) => pattern.test(v)),
    `expected a violation matching ${pattern}, got:\n${violations.join("\n") || "(none)"}`,
  );

const CI_RESTORE_BLOCK = [
  "        run: |",
  "          dotnet restore Auth/Auth_API.Tests/Auth_API.Tests.csproj",
  "          dotnet restore Auth/API_Gateway/API_Gateway.csproj",
  "          dotnet restore Auth/Auth_Setup/Auth_Setup.csproj",
].join("\n");
const CI_RESTORE_COMMANDS = [
  "dotnet restore Auth/Auth_API.Tests/Auth_API.Tests.csproj",
  "dotnet restore Auth/API_Gateway/API_Gateway.csproj",
  "dotnet restore Auth/Auth_Setup/Auth_Setup.csproj",
];
const EXIT_CHECK = "if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }";
/** The Restore step as S04 (K9.17) will write it: an exit check after every dotnet line. */
const CI_RESTORE_BLOCK_WITH_EXIT_CHECKS = [
  "        run: |",
  ...CI_RESTORE_COMMANDS.flatMap((command) => [`          ${command}`, `          ${EXIT_CHECK}`]),
].join("\n");

describe("U-1 bounded YAML reader", () => {
  const ci = () => normalizeNewlines(repository().ci);

  test("reads the real ci.yml: jobs, names and permissions", () => {
    const doc = readWorkflow(ci());
    const names = Object.fromEntries(Object.entries(doc.jobs).map(([id, job]) => [id, job.name]));
    assert.deepEqual(names, {
      backend: "Backend build and test",
      frontend: "Frontend lint, typecheck and test",
      secrets: "Committed key material",
      governance: "Repository governance",
      "dependency-review": "Dependency review",
    });
    assert.deepEqual(doc.permissions, { contents: "read" });
    for (const job of Object.values(doc.jobs)) assert.equal("permissions" in job, false);
    assert.deepEqual(Object.keys(doc.on), ["push", "pull_request", "workflow_dispatch"]);
  });

  test("the Restore step's dotnet lines are exactly the three restores of ci.yml, in order", () => {
    assert.deepEqual(dotnetLines(backendRestoreRun(readWorkflow(ci()))), CI_RESTORE_COMMANDS);
  });

  test("with S04's exit check after every dotnet line, the run value keeps all six lines and the dotnet lines stay the three", () => {
    const doc = readWorkflow(mutate(ci(), CI_RESTORE_BLOCK, CI_RESTORE_BLOCK_WITH_EXIT_CHECKS));
    const run = backendRestoreRun(doc);
    assert.deepEqual(runLines(run).filter(Boolean), CI_RESTORE_COMMANDS.flatMap((command) => [command, EXIT_CHECK]));
    assert.deepEqual(dotnetLines(run), CI_RESTORE_COMMANDS);
  });

  test("CRLF and LF checkouts read the same", () => {
    const lf = ci();
    assert.deepEqual(readWorkflow(lf.replace(/\n/g, "\r\n")), readWorkflow(lf));
  });

  test("reads the real dependabot.yml: directories and groups", () => {
    const doc = readDependabot(repository().dependabot);
    const byEcosystem = Object.fromEntries(doc.updates.map((block) => [block["package-ecosystem"], block]));
    assert.deepEqual(byEcosystem.nuget.directories, ["/Auth/*"]);
    assert.equal(byEcosystem.npm.directory, "/Auth_UI");
    assert.deepEqual(byEcosystem.nuget.groups, {
      "nuget-minor-and-patch": { "applies-to": "version-updates", patterns: ["*"], "update-types": ["minor", "patch"] },
      "nuget-security": { "applies-to": "security-updates", patterns: ["*"] },
    });
    assert.deepEqual(Object.keys(byEcosystem.npm.groups), ["npm-minor-and-patch", "npm-security"]);
  });

  test("scalars, comments, quoting, flow collections and block scalars", () => {
    const doc = parseYamlSubset(
      [
        "a: plain value # trailing",
        "b: 'it''s' # c",
        'c: "x \\"y\\" \\\\ z"',
        "d:",
        "e: ${{ github.ref }}",
        "f: [one, 'two', { k: v, l: [1, 2] }]",
        "g: |-",
        "  first",
        "",
        "    second # not a comment",
        "h:",
        "- compact",
        "- k: v",
        "  m: w",
        "i: C#",
      ].join("\n"),
    );
    assert.deepEqual(doc, {
      a: "plain value",
      b: "it's",
      c: 'x "y" \\ z',
      d: null,
      e: "${{ github.ref }}",
      f: ["one", "two", { k: "v", l: ["1", "2"] }],
      g: "first\n\n  second # not a comment",
      h: ["compact", { k: "v", m: "w" }],
      i: "C#",
    });
    assert.equal(commentOf(doc, "a"), "trailing");
    assert.equal(lineOf(doc, "h"), 11);
  });

  const unsupportedCases = [
    ["anchor (&)", "defaults: &defaults\n  a: 1\n"],
    ["alias (*)", "a: 1\nb: *a\n"],
    ["folded block scalar (>)", "run: >\n  folded\n"],
    ["multi-line flow collection", "jobs: { build: {\n  runs-on: x } }\n"],
    ["merge key (<<)", "a:\n  <<: x\n"],
    ["tag (!)", "a: !secret x\n"],
    ["document marker", "---\na: 1\n"],
    ["document marker", "a: 1\n---\nb: 2\n"],
    ["block scalar header \"|2\"", "run: |2\n  x\n"],
    ["block scalar header \"|+\"", "run: |+\n  x\n"],
    ["complex key (?)", "? a\n: b\n"],
    ["tab indentation", "a:\n\tb: 1\n"],
    ["quoted key", '"a": 1\n'],
    ["duplicate key", "a: 1\na: 2\n"],
    ["continuation line", "a: first\n  second\n"],
    ["quoted scalar that does not close", "a: 'open\n  close'\n"],
    ["directive (%)", "%YAML 1.2\na: 1\n"],
    ['escape "\\n"', 'a: "x\\ny"\n'],
  ];
  for (const [construct, text] of unsupportedCases) {
    test(`rejects ${construct} by name`, () => {
      assert.throws(
        () => parseYamlSubset(text),
        (error) => error instanceof ReaderError && error.message.includes(`unsupported construct: ${construct}`),
      );
    });
  }
});

describe("G-S03a dependabot.yml shape", () => {
  const real = () => normalizeNewlines(repository().dependabot);

  test("the real dependabot.yml passes", () => {
    assert.deepEqual(guardDependabot(repository().dependabot), []);
  });

  const breaks = [
    ["a reviewers line", (t) => mutate(t, '    commit-message: { prefix: "chore(deps)" }\n  - package-ecosystem: "npm"', '    reviewers: ["someone"]\n    commit-message: { prefix: "chore(deps)" }\n  - package-ecosystem: "npm"'), /"reviewers"/],
    ["no npm block", (t) => t.slice(0, t.indexOf('  - package-ecosystem: "npm"')), /exactly one "npm" block, found 0/],
    ["nuget with directory \"/\"", (t) => mutate(t, 'directories: ["/Auth/*"]', 'directory: "/"'), /nuget block: directories must be/],
    ["no security-updates group", (t) => mutate(t, '      nuget-security:        { applies-to: security-updates, patterns: ["*"] }\n', ""), /nuget block: needs a group with applies-to: security-updates/],
    ["open-pull-requests-limit 10", (t) => mutate(t, "open-pull-requests-limit: 5     #", "open-pull-requests-limit: 10    #"), /open-pull-requests-limit must be between 0 and 5/],
    ["ignore with dependency-name \"*\"", (t) => mutate(t, '    commit-message: { prefix: "chore(deps)" }\n  - package-ecosystem: "npm"', '    commit-message: { prefix: "chore(deps)" }\n    ignore:\n      # ignore * 2026-09-29 too many updates\n      - dependency-name: "*"\n  - package-ecosystem: "npm"'), /dependency-name "\*"/],
    ["ignore without its dated comment", (t) => mutate(t, '    commit-message: { prefix: "chore(deps)" }\n  - package-ecosystem: "npm"', '    commit-message: { prefix: "chore(deps)" }\n    ignore:\n      - dependency-name: "Serilog"\n  - package-ecosystem: "npm"'), /ignore entry "Serilog" needs "# ignore Serilog YYYY-MM-DD <reason>"/],
    ["target-branch", (t) => mutate(t, '    directory: "/Auth_UI"', '    directory: "/Auth_UI"\n    target-branch: "develop"'), /"target-branch"/],
  ];
  for (const [name, breakIt, pattern] of breaks) {
    test(`fixture: ${name}`, () => expectViolation(guardDependabot(breakIt(real())), pattern));
  }

  test("an ignore entry with its dated comment passes", () => {
    const fixture = mutate(real(), '    commit-message: { prefix: "chore(deps)" }\n  - package-ecosystem: "npm"', '    commit-message: { prefix: "chore(deps)" }\n    ignore:\n      # ignore Serilog 2026-09-29 major upgrade needs its own review\n      - dependency-name: "Serilog"\n  - package-ecosystem: "npm"');
    assert.deepEqual(guardDependabot(fixture), []);
  });
});

describe("G-S03b manifest locations", () => {
  test("the real tracked manifests pass", () => {
    const { tracked, workspace } = repository();
    assert.ok(tracked.filter((p) => p.endsWith(".csproj")).length > 0, "no .csproj tracked: the guard would be vacuous");
    assert.ok(tracked.filter((p) => p.startsWith("Auth_UI/") && p.endsWith("/package.json")).length > 1);
    assert.deepEqual(guardManifestLocations(tracked, workspacePackagePatterns(workspace)), []);
  });

  test("fixture: a nested .csproj and a package.json outside the workspace give two violations", () => {
    const violations = guardManifestLocations(
      ["Auth/Auth_API/Auth_API.csproj", "Auth/tests/X/X.csproj", "Auth_UI/package.json", "Auth_UI/apps/console/package.json", "Auth_UI/tools/y/package.json"],
      ["apps/*", "packages/*"],
    );
    assert.equal(violations.length, 2, violations.join("\n"));
    expectViolation(violations, /Auth\/tests\/X\/X\.csproj/);
    expectViolation(violations, /Auth_UI\/tools\/y\/package\.json/);
  });

  test("fixture: a workspace pattern the guard cannot map is itself a violation", () => {
    expectViolation(guardManifestLocations([], ["apps/**"]), /"apps\/\*\*" is not of the form/);
  });
});

describe("G-S03c pnpm-lock.yaml is one document", () => {
  test("the real lockfile passes", () => {
    assert.deepEqual(guardLockfile(repository().lockfile), []);
  });

  test("fixture: a leading --- and two lockfileVersion lines", () => {
    const violations = guardLockfile("---\r\nlockfileVersion: '9.0'\r\n---\r\nlockfileVersion: '9.0'\r\n");
    expectViolation(violations, /starts with '-'/);
    expectViolation(violations, /2 lockfileVersion lines/);
  });
});

describe("G-S03d no audit setting inside Auth_UI", () => {
  const real = () => {
    const { workspace, packageJson, npmrc } = repository();
    return { workspace, packageJson, npmrc };
  };

  test("the real pnpm-workspace.yaml, package.json and .npmrc pass", () => {
    assert.deepEqual(guardPnpmSettings(real()), []);
  });

  const withPackage = (change) => {
    const base = real();
    const pkg = JSON.parse(base.packageJson);
    change(pkg);
    return { ...base, packageJson: JSON.stringify(pkg, null, 2) };
  };
  const breaks = [
    ["audit: with level: critical", () => ({ ...real(), workspace: `${normalizeNewlines(real().workspace)}\naudit:\n  level: critical\n` }), /sets "audit"/],
    ["auditLevel: high", () => ({ ...real(), workspace: `${normalizeNewlines(real().workspace)}\nauditLevel: high\n` }), /sets "auditLevel"/],
    ["packageManager pnpm@10.0.0", () => withPackage((p) => (p.packageManager = "pnpm@10.0.0")), /"packageManager" must be absent or pnpm@11/],
    ["devEngines", () => withPackage((p) => (p.devEngines = { packageManager: { name: "pnpm", version: "11.8.0" } })), /"devEngines"/],
    ["pnpm.auditConfig", () => withPackage((p) => (p.pnpm = { auditConfig: { ignoreCves: ["CVE-2026-0001"] } })), /"pnpm\.auditConfig"/],
    ["engines.pnpm >=10", () => withPackage((p) => (p.engines = { pnpm: ">=10" })), /"engines\.pnpm" must be absent/],
    [".npmrc with registry=https://registry.invalid/", () => ({ ...real(), npmrc: "registry=https://registry.invalid/\n" }), /\.npmrc sets a registry/],
  ];
  for (const [name, fixture, pattern] of breaks) {
    test(`fixture: ${name}`, () => expectViolation(guardPnpmSettings(fixture()), pattern));
  }

  test("pnpm 11 pins and ranges pass", () => {
    for (const range of ["11", "^11.8.0", "~11.8", "11.x", ">=11.0.0 <12.0.0"])
      assert.deepEqual(guardPnpmSettings(withPackage((p) => (p.engines = { pnpm: range }))), [], range);
    assert.deepEqual(guardPnpmSettings(withPackage((p) => (p.packageManager = "pnpm@11.8.0"))), []);
  });
});

const MINIMAL_WORKFLOW = [
  "name: Fixture",
  "on:",
  "  push:",
  "permissions:",
  "  contents: read",
  "jobs:",
  "  build:",
  "    name: Build",
  "    runs-on: ubuntu-latest",
  "    steps:",
  "      - run: echo ok",
  "",
].join("\n");

describe("G-S03e K5 permissions allowlist", () => {
  test("every real workflow passes", () => {
    const { workflows } = repository();
    assert.ok(workflows.length >= 2, "fewer than two workflows found: the guard would be vacuous");
    assert.deepEqual(guardPermissions(workflows), []);
  });

  const breaks = [
    ["a job with pull-requests: write", mutate(MINIMAL_WORKFLOW, "    runs-on:", "    permissions:\n      pull-requests: write\n    runs-on:"), /job "build" adds a permissions block/],
    ["no top-level permissions", mutate(MINIMAL_WORKFLOW, "permissions:\n  contents: read\n", ""), /top-level permissions must be exactly/],
    ["write-all", mutate(MINIMAL_WORKFLOW, "permissions:\n  contents: read\n", "permissions: write-all\n"), /top-level permissions must be exactly/],
    ["contents: write", mutate(MINIMAL_WORKFLOW, "  contents: read\n", "  contents: write\n"), /top-level permissions must be exactly/],
  ];
  for (const [name, text, pattern] of breaks) {
    test(`fixture: ${name}`, () => expectViolation(guardPermissions([{ path: ".github/workflows/fixture.yml", text }]), pattern));
  }

  test("the allowlisted job passes only with exactly its permissions", () => {
    const analyze = mutate(MINIMAL_WORKFLOW, "  build:\n", "  analyze:\n").replace("    runs-on:", "    permissions:\n      contents: read\n      security-events: write\n    runs-on:");
    assert.deepEqual(guardPermissions([{ path: ".github/workflows/codeql.yml", text: analyze }]), []);
    const wider = mutate(analyze, "      security-events: write\n", "      security-events: write\n      actions: write\n");
    expectViolation(guardPermissions([{ path: ".github/workflows/codeql.yml", text: wider }]), /permissions must be exactly/);
    expectViolation(guardPermissions([{ path: ".github/workflows/other.yml", text: analyze }]), /adds a permissions block/);
  });
});

describe("G-S03f unique job names", () => {
  test("the real workflows pass", () => {
    assert.deepEqual(guardJobNames(repository().workflows), []);
  });

  test("fixture: two files in a temporary folder, each with \"Backend build and test\"", () => {
    const folder = mkdtempSync(join(tmpdir(), "governance-"));
    try {
      const job = mutate(MINIMAL_WORKFLOW, "    name: Build", "    name: Backend build and test");
      writeFileSync(join(folder, "one.yml"), job);
      writeFileSync(join(folder, "two.yml"), job);
      const workflows = readdirSync(folder).map((file) => ({ path: file, text: readFileSync(join(folder, file), "utf8") }));
      const violations = guardJobNames(workflows);
      assert.equal(violations.length, 1);
      expectViolation(violations, /"Backend build and test" is used more than once: one\.yml \(build\), two\.yml \(build\)/);
    } finally {
      rmSync(folder, { recursive: true, force: true });
    }
  });
});

describe("G-S03g every workflow and dependabot.yml is readable", () => {
  test("the real files are inside the supported subset", () => {
    const { workflows, dependabot } = repository();
    const files = [
      ...workflows.map((w) => ({ ...w, read: readWorkflow })),
      { path: ".github/dependabot.yml", text: dependabot, read: readDependabot },
    ];
    assert.ok(files.length >= 3);
    assert.deepEqual(guardReadable(files), []);
  });

  test("fixture: an anchor &defaults is reported, not skipped", () => {
    const text = mutate(MINIMAL_WORKFLOW, "  build:\n", "  build: &defaults\n");
    expectViolation(guardReadable([{ path: "fixture.yml", text, read: readWorkflow }]), /G-S03g: fixture\.yml: unsupported construct: anchor \(&\)/);
  });
});

describe("G-S03h MSBuild audit properties and suppressions", () => {
  const props = () => normalizeNewlines(repository().msbuild.find((f) => f.path === PROPS_PATH).text);
  const csproj = (body) => `<Project Sdk="Microsoft.NET.Sdk">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n${body}\n  </PropertyGroup>\n</Project>\n`;
  const withFiles = (...extra) => [{ path: PROPS_PATH, text: props() }, ...extra];
  const ITEM_GROUP_END = "  </PropertyGroup>\n\n</Project>";
  const suppress = (comment) =>
    `  </PropertyGroup>\n\n  <ItemGroup>\n${comment}\n    <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-5crp-9r3c-p9vr" />\n  </ItemGroup>\n\n</Project>`;

  test("the real .csproj/.props/.targets files pass", () => {
    const { msbuild } = repository();
    assert.ok(msbuild.some((f) => f.path === PROPS_PATH) && msbuild.length > 5, "MSBuild files not found: the guard would be vacuous");
    assert.deepEqual(guardMsbuild(msbuild), []);
  });

  const breaks = [
    ["a props file with NuGetAuditMode direct", () => withFiles({ path: "Auth/Extra.targets", text: csproj("    <NuGetAuditMode>direct</NuGetAuditMode>") }), /Extra\.targets: <NuGetAuditMode> is allowed only in/],
    ["NuGetAuditMode direct overriding inside Directory.Build.props", () => [{ path: PROPS_PATH, text: mutate(props(), ITEM_GROUP_END, "  </PropertyGroup>\n\n  <PropertyGroup>\n    <NuGetAuditMode>direct</NuGetAuditMode>\n  </PropertyGroup>\n\n</Project>") }], /NuGetAuditMode>all<\/NuGetAuditMode> must be set exactly once/],
    ["NoWarn with NU1903", () => withFiles({ path: "Auth/X/X.csproj", text: csproj("    <NoWarn>$(NoWarn);NU1903</NoWarn>") }), /X\.csproj: <NoWarn> hides NU190x/],
    ["a NoWarn attribute on a PackageReference", () => withFiles({ path: "Auth/X/X.csproj", text: '<Project>\n  <ItemGroup>\n    <PackageReference Include="A" Version="1.0.0" NoWarn="NU1902" />\n  </ItemGroup>\n</Project>\n' }), /NoWarn attribute hides NU190x/],
    ["TreatWarningsAsErrors true without WarningsNotAsErrors", () => withFiles({ path: "Auth/X/X.csproj", text: csproj("    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>") }), /X\.csproj: <TreatWarningsAsErrors>true<\/TreatWarningsAsErrors> needs <WarningsNotAsErrors>/],
    ["WarningsNotAsErrors for the codes without the AuditPipeline condition", () => withFiles({ path: "Auth/X/X.csproj", text: csproj("    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>\n    <WarningsNotAsErrors>NU1900;NU1901;NU1902;NU1903;NU1904</WarningsNotAsErrors>") }), /<WarningsNotAsErrors> with NU190x must sit in a PropertyGroup conditioned on/],
    ["NU1903 escalated outside the AuditPipeline group", () => withFiles({ path: "Auth/X/X.csproj", text: csproj("    <WarningsAsErrors>$(WarningsAsErrors);NU1903</WarningsAsErrors>") }), /escalates NU190x outside a PropertyGroup conditioned on/],
    ["the AuditPipeline group without NU1900", () => [{ path: PROPS_PATH, text: mutate(props(), ";NU1900;NU1902", ";NU1902") }], /must add NU1900 to WarningsAsErrors/],
    ["NuGetAuditSuppress without its comment", () => [{ path: PROPS_PATH, text: mutate(props(), ITEM_GROUP_END, suppress("")) }], /GHSA-5crp-9r3c-p9vr needs "<!-- allow GHSA-5crp-9r3c-p9vr/],
    ["NuGetAuditSuppress whose comment names another GHSA", () => [{ path: PROPS_PATH, text: mutate(props(), ITEM_GROUP_END, suppress("    <!-- allow GHSA-aaaa-bbbb-cccc 2026-09-29 until 2026-12-01 accepted in review -->")) }], /names GHSA-aaaa-bbbb-cccc but the item suppresses GHSA-5crp-9r3c-p9vr/],
    ["NuGetAuditSuppress expiring after 120 days", () => [{ path: PROPS_PATH, text: mutate(props(), ITEM_GROUP_END, suppress("    <!-- allow GHSA-5crp-9r3c-p9vr 2026-09-29 until 2027-01-27 accepted in review -->")) }], /more than 90 days after/],
    ["NuGetAuditSuppress outside Directory.Build.props", () => withFiles({ path: "Auth/X/X.csproj", text: '<Project>\n  <ItemGroup>\n    <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-5crp-9r3c-p9vr" />\n  </ItemGroup>\n</Project>\n' }), /X\.csproj: <NuGetAuditSuppress> is allowed only in/],
  ];
  for (const [name, fixture, pattern] of breaks) {
    test(`fixture: ${name}`, () => expectViolation(guardMsbuild(fixture()), pattern));
  }

  test("TreatWarningsAsErrors with the conditional exception passes, and so does a dated suppression", () => {
    const exception = `<Project>\n  <PropertyGroup>\n    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>\n  </PropertyGroup>\n  <PropertyGroup Condition="'$(AuditPipeline)' != 'true'">\n    <WarningsNotAsErrors>$(WarningsNotAsErrors);NU1900;NU1901;NU1902;NU1903;NU1904</WarningsNotAsErrors>\n  </PropertyGroup>\n</Project>\n`;
    assert.deepEqual(guardMsbuild(withFiles({ path: "Auth/X/X.csproj", text: exception })), []);
    const dated = mutate(props(), ITEM_GROUP_END, suppress("    <!-- allow GHSA-5crp-9r3c-p9vr 2026-09-29 until 2026-12-28 no fixed version; accepted in review -->"));
    assert.deepEqual(guardMsbuild([{ path: PROPS_PATH, text: dated }]), []);
    assert.deepEqual(guardMsbuild([{ path: PROPS_PATH, text: dated.replace(/\n/g, "\r\n") }]), []);
  });
});

describe("G-S03i audit commands, environment and triggers", () => {
  const real = () => ({
    ci: normalizeNewlines(repository().ci),
    audit: normalizeNewlines(repository().audit),
    pnpmArgs: PNPM_AUDIT_ARGS,
  });
  const NUGET_THIRD = [
    "      - name: Audit Auth_Setup graph",
    "        if: ${{ !cancelled() }}",
    "        run: dotnet restore Auth/Auth_Setup/Auth_Setup.csproj --force -p:AuditPipeline=true",
    "",
  ].join("\n");

  test("the real ci.yml, dependency-audit.yml and PNPM_AUDIT_ARGS pass", () => {
    assert.deepEqual(guardAuditCommands(real()), []);
  });

  const breaks = [
    ["nuget-audit restores only two projects", (f) => ({ ...f, audit: mutate(f.audit, NUGET_THIRD, "") }), /each ci\.yml project must be audited exactly once/],
    ["a run line with /p:NuGetAudit=false", (f) => ({ ...f, audit: mutate(f.audit, "API_Gateway.csproj --force -p:AuditPipeline=true", "API_Gateway.csproj --force -p:AuditPipeline=true /p:NuGetAudit=false") }), /is not exactly "dotnet restore <project> --force -p:AuditPipeline=true"/],
    ["env NuGetAudit: false on the job", (f) => ({ ...f, audit: mutate(f.audit, "    timeout-minutes: 15\n", "    timeout-minutes: 15\n    env:\n      NuGetAudit: false\n") }), /job nuget-audit: env NuGetAudit/],
    ["ci.yml passes AuditPipeline", (f) => ({ ...f, ci: mutate(f.ci, "          dotnet restore Auth/Auth_Setup/Auth_Setup.csproj\n", "          dotnet restore Auth/Auth_Setup/Auth_Setup.csproj -p:AuditPipeline=true\n") }), /ci\.yml mentions AuditPipeline/],
    ["ci.yml restore with -warnaserror", (f) => ({ ...f, ci: mutate(f.ci, "          dotnet restore Auth/Auth_Setup/Auth_Setup.csproj\n", "          dotnet restore Auth/Auth_Setup/Auth_Setup.csproj -warnaserror\n") }), /passes a property or a warnaserror flag/],
    ["arguments with --ignore-registry-errors", (f) => ({ ...f, pnpmArgs: [...f.pnpmArgs, "--ignore-registry-errors"] }), /contains --ignore-registry-errors/],
    ["arguments with --fix", (f) => ({ ...f, pnpmArgs: [...f.pnpmArgs, "--fix"] }), /contains --fix/],
    ["arguments without --fail-if-no-match", (f) => ({ ...f, pnpmArgs: f.pnpmArgs.filter((a) => a !== "--fail-if-no-match") }), /lacks --fail-if-no-match/],
    ["push paths without Auth/Directory.Build.props", (f) => ({ ...f, audit: mutate(f.audit, "      - 'Auth/Directory.Build.props'\n", "") }), /push paths must include 'Auth\/Directory\.Build\.props'/],
    ["a pull_request_target trigger", (f) => ({ ...f, audit: mutate(f.audit, "  workflow_dispatch:\n", "  workflow_dispatch:\n  pull_request_target:\n") }), /triggers must be exactly push, schedule and workflow_dispatch/],
    ["continue-on-error on an audit step", (f) => ({ ...f, audit: mutate(f.audit, "      - name: Audit the lockfile\n", "      - name: Audit the lockfile\n        continue-on-error: true\n") }), /continue-on-error turns a failed audit green/],
    ["the pnpm step not calling the gate", (f) => ({ ...f, audit: mutate(f.audit, "        run: node ../Tools/github/pnpm-audit-gate.mjs", "        run: pnpm audit --audit-level moderate") }), /exactly one run step/],
  ];
  for (const [name, breakIt, pattern] of breaks) {
    test(`fixture: ${name}`, () => expectViolation(guardAuditCommands(breakIt(real())), pattern));
  }

  test("S04's exit check after every dotnet line passes, and still catches a missing project", () => {
    const withChecks = { ...real(), ci: mutate(real().ci, CI_RESTORE_BLOCK, CI_RESTORE_BLOCK_WITH_EXIT_CHECKS) };
    assert.deepEqual(guardAuditCommands(withChecks), []);
    expectViolation(
      guardAuditCommands({ ...withChecks, audit: mutate(withChecks.audit, NUGET_THIRD, "") }),
      /each ci\.yml project must be audited exactly once/,
    );
  });
});

describe("G-S03j Dependency review inputs", () => {
  const real = () => normalizeNewlines(repository().ci);
  const IF_LINE = "    if: github.event_name == 'pull_request'\n";
  const WARN_ONLY = "          warn-only: false\n";

  test("the real ci.yml passes", () => {
    assert.deepEqual(guardDependencyReview(repository().ci), []);
  });

  const breaks = [
    ["warn-only: true", (t) => mutate(t, WARN_ONLY, "          warn-only: true\n"), /with\.warn-only must be "false"/],
    ["fail-on-scopes: runtime", (t) => mutate(t, "fail-on-scopes: runtime, development, unknown", "fail-on-scopes: runtime"), /with\.fail-on-scopes must be/],
    ["continue-on-error: true on the job", (t) => mutate(t, "    timeout-minutes: 10\n    # On push", "    timeout-minutes: 10\n    continue-on-error: true\n    # On push"), /continue-on-error on the job/],
    ["an if excluding renovate[bot]", (t) => mutate(t, IF_LINE, "    if: github.event_name == 'pull_request' && github.actor != 'renovate[bot]'\n"), /the job condition must be exactly/],
    ["the P2-R3 fallback without its dated line", (t) => mutate(t, IF_LINE, "    if: github.event_name == 'pull_request' && github.actor != 'dependabot[bot]'\n"), /P2-R3 fallback condition needs/],
    ["allow-ghsas with a GHSA that has no comment", (t) => mutate(t, WARN_ONLY, `${WARN_ONLY}          allow-ghsas: GHSA-5crp-9r3c-p9vr\n`), /lists GHSA-5crp-9r3c-p9vr without a dated "# allow" comment/],
    ["uses: @v5 instead of a SHA", (t) => mutate(t, "actions/dependency-review-action@a1d282b36b6f3519aa1f3fc636f609c47dddb294 # v5.0.0", "actions/dependency-review-action@v5"), /must pin a 40-character commit SHA/],
    ["a config-file input", (t) => mutate(t, WARN_ONLY, `${WARN_ONLY}          config-file: ./.github/dependency-review.yml\n`), /with\.config-file is not a designed input/],
    ["a permissions block on the job", (t) => mutate(t, "    timeout-minutes: 10\n    # On push", "    timeout-minutes: 10\n    permissions:\n      contents: read\n    # On push"), /must not carry a permissions block/],
  ];
  for (const [name, breakIt, pattern] of breaks) {
    test(`fixture: ${name}`, () => expectViolation(guardDependencyReview(breakIt(real())), pattern));
  }

  test("the K1 condition, the dated P2-R3 fallback and a Dependabot-moved SHA all pass", () => {
    assert.deepEqual(guardDependencyReview(real()), []);
    const fallback = mutate(real(), IF_LINE, "    # P2-R3 fallback 2026-10-06 M-8 run 1234567890 failed with 403 on a dependabot[bot] PR\n    if: github.event_name == 'pull_request' && github.actor != 'dependabot[bot]'\n");
    assert.deepEqual(guardDependencyReview(fallback), []);
    const moved = mutate(real(), "a1d282b36b6f3519aa1f3fc636f609c47dddb294 # v5.0.0", `${"0123456789abcdef".repeat(2)}01234567 # v5.1.0`);
    assert.deepEqual(guardDependencyReview(moved), []);
  });

  test("a dated allow-ghsas passes; a mismatch or an over-long window fails", () => {
    const allow = (lines, value) => mutate(real(), WARN_ONLY, `${WARN_ONLY}${lines.map((l) => `          ${l}\n`).join("")}          allow-ghsas: ${value}\n`);
    const ok = allow(["# allow GHSA-5crp-9r3c-p9vr 2026-09-29 until 2026-12-28 no fixed version; accepted in review"], "GHSA-5crp-9r3c-p9vr");
    assert.deepEqual(guardDependencyReview(ok), []);
    expectViolation(
      guardDependencyReview(allow(["# allow GHSA-5crp-9r3c-p9vr 2026-09-29 until 2026-12-28 accepted"], "GHSA-5crp-9r3c-p9vr, GHSA-aaaa-bbbb-cccc")),
      /lists GHSA-aaaa-bbbb-cccc without a dated/,
    );
    expectViolation(
      guardDependencyReview(allow(["# allow GHSA-5crp-9r3c-p9vr 2026-09-29 until 2027-01-27 accepted"], "GHSA-5crp-9r3c-p9vr")),
      /more than 90 days after/,
    );
  });
});

describe("G-S03k pnpm allow list", () => {
  const entry = { ghsa: "GHSA-h67p-54hq-rp68", date: "2026-09-29", expires: "2026-12-28", reason: "dev-only; no fixed version reachable" };

  test("the real Tools/github/pnpm-audit-allow.json passes", () => {
    assert.deepEqual(guardAllowList(repository().allowList), []);
  });

  const breaks = [
    ["an entry without reason", [{ ...entry, reason: undefined }], /"reason" is missing or empty/],
    ["a date not in YYYY-MM-DD", [{ ...entry, date: "29/09/2026" }], /is not a YYYY-MM-DD calendar date/],
    ["an expiry 120 days after the date", [{ ...entry, expires: "2027-01-27" }], /more than 90 days after/],
    ["a duplicated GHSA", [entry, { ...entry }], /listed more than once/],
    ["not an array", { entries: [entry] }, /not a JSON array/],
  ];
  for (const [name, value, pattern] of breaks) {
    test(`fixture: ${name}`, () => expectViolation(guardAllowList(JSON.stringify(value)), pattern));
  }

  test("a 90-day window passes and a 91-day window fails", () => {
    assert.deepEqual(guardAllowList(JSON.stringify([{ ...entry, expires: "2026-12-28" }])), []);
    expectViolation(guardAllowList(JSON.stringify([{ ...entry, expires: "2026-12-29" }])), /more than 90 days/);
  });
});
