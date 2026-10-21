/**
 * Refuses key material in tracked settings files that the text-pattern step in
 * .github/workflows/ci.yml ("Refuse populated crown-jewel settings values")
 * cannot see.
 *
 * That step matches `"Name": "value"` on one line, which catches a scalar secret
 * but not a keyring: `"Keyring": { "Hmac": { "Keys": { "2": { "Material": … } } } }`
 * spreads over many lines and its key names are numbers. So this reads every
 * tracked appsettings*.json as JSON and fails when:
 *   - anything under Keyring:Hmac or Keyring:Rsa has a value (S11, S12): those
 *     sections hold only key material and its state, and live in the encrypted
 *     secrets file, never in configuration;
 *   - Jwt:PrivateKeyPath, or any PrivateKeyPath, has a value: a path to a private
 *     key in a tracked file points every deployment at a key on disk.
 * Configuration keys are case-insensitive, so property names are compared that way.
 *
 * Usage:
 *   node Tools/check-secret-json.mjs              # the repository's tracked files
 *   node Tools/check-secret-json.mjs --self-test  # proves the check fails on a bad fixture
 *
 * No dependencies: node's own modules and git only. Exit 0 clean, 1 on a finding.
 */
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

const KEYRING_SECTIONS = [["keyring", "hmac"], ["keyring", "rsa"]];

/** Every leaf under `node` that holds a value, as [path, value] pairs. */
function populatedLeaves(node, path) {
  if (node === null || node === undefined) return [];
  if (typeof node === "object") {
    return Object.entries(node).flatMap(([key, child]) => populatedLeaves(child, `${path}:${key}`));
  }
  if (typeof node === "string") return node.trim() === "" ? [] : [[path, node]];
  if (typeof node === "boolean") return node ? [[path, node]] : [];
  return [[path, node]];
}

/** The child of `node` named `name`, ignoring case, or undefined. */
function child(node, name) {
  if (node === null || typeof node !== "object" || Array.isArray(node)) return undefined;
  const key = Object.keys(node).find((k) => k.toLowerCase() === name);
  return key === undefined ? undefined : node[key];
}

/** Every value of a property named `name` (any case) anywhere in the tree, with its path. */
function propertiesNamed(node, name, path = "") {
  if (node === null || typeof node !== "object") return [];
  return Object.entries(node).flatMap(([key, value]) => {
    const here = path ? `${path}:${key}` : key;
    const own = key.toLowerCase() === name ? [[here, value]] : [];
    return [...own, ...propertiesNamed(value, name, here)];
  });
}

/** The findings for one parsed settings document. Paths only, never values. */
export function findings(document) {
  const found = [];
  for (const [section, ring] of KEYRING_SECTIONS) {
    const node = child(child(document, section), ring);
    for (const [path] of populatedLeaves(node, `${section}:${ring}`)) {
      found.push(`${path} has a value; keyring material belongs in the encrypted secrets file`);
    }
  }
  for (const [path, value] of propertiesNamed(document, "privatekeypath")) {
    if (typeof value === "string" ? value.trim() !== "" : value !== null && value !== undefined) {
      found.push(`${path} has a value; a private key is never referenced from a tracked settings file`);
    }
  }
  return found;
}

function trackedSettingsFiles() {
  const output = execFileSync("git", ["ls-files", "-z", "--", "*appsettings*.json"], {
    cwd: repositoryRoot,
    encoding: "utf8",
  });
  return output.split("\0").filter(Boolean);
}

function checkRepository() {
  const files = trackedSettingsFiles();
  if (files.length === 0) {
    // A check that finds nothing to read would pass forever without checking anything.
    console.error("check-secret-json: no tracked appsettings*.json files found; refusing to report clean");
    return 1;
  }

  let failures = 0;
  for (const file of files) {
    let document;
    try {
      document = JSON.parse(readFileSync(join(repositoryRoot, file), "utf8"));
    } catch (error) {
      console.error(`::error file=${file}::not valid JSON (${error.message}); it cannot be checked for key material`);
      failures++;
      continue;
    }
    for (const finding of findings(document)) {
      console.error(`::error file=${file}::${finding}`);
      failures++;
    }
  }

  console.log(`check-secret-json: ${files.length} tracked settings file(s) read, ${failures} finding(s)`);
  return failures === 0 ? 0 : 1;
}

/** The check must fail on each kind of bad file, and pass on the shipped shapes. */
function selfTest() {
  const bad = [
    ["a staged HMAC key", { Keyring: { Hmac: { Keys: { 2: { Material: "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=" } } } } }],
    ["a current key id", { keyring: { hmac: { CurrentKeyId: 2 } } }],
    ["a compromised flag", { Keyring: { Hmac: { Keys: { 1: { Compromised: true } } } } }],
    ["an RSA private key", { Keyring: { Rsa: { Keys: { abc: { PrivateKeyPem: "-----BEGIN PRIVATE KEY-----" } } } } }],
    ["a private key path", { Jwt: { PrivateKeyPath: "C:\\keys\\jwt.pem" } }],
  ];
  const good = [
    ["the shipped Jwt section", { Jwt: { PrivateKeyPath: "", PrivateKeyPem: "" } }],
    ["an empty keyring", { Keyring: { Hmac: { Keys: {} }, Rsa: {} } }],
    ["the keyring setting", { Keyring: { IssueUnderPurposeKeys: false, Hmac: { Keys: { 1: { Material: "" } } } } }],
  ];

  const problems = [];
  for (const [name, document] of bad) {
    if (findings(document).length === 0) problems.push(`self-test: "${name}" was not refused`);
  }
  for (const [name, document] of good) {
    const found = findings(document);
    if (found.length > 0) problems.push(`self-test: "${name}" was refused: ${found.join("; ")}`);
  }

  if (problems.length > 0) {
    for (const problem of problems) console.error(`::error::${problem}`);
    return 1;
  }
  console.log(`check-secret-json self-test: ${bad.length} bad fixture(s) refused, ${good.length} good fixture(s) accepted`);
  return 0;
}

process.exitCode = process.argv.includes("--self-test") ? selfTest() : checkRepository();
