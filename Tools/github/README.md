# Tools/github

Repository governance for this public repository: the guards that keep CI, Dependabot,
MSBuild and pnpm settings from silently weakening, and the rules for triaging what the
dependency checks report.

Each section below is owned by the card that created it. Later cards (S04, X06, S05, S06)
append their own sections; they do not rewrite these.

| File | What it is |
|------|------------|
| `governance.test.mjs` | The governance harness: a bounded YAML reader and the guards G-S03a…k. |
| `pnpm-audit-gate.mjs` | The pnpm half of the "Dependency audit" workflow: `pnpm audit` plus the allow list. |
| `pnpm-audit-gate.test.mjs` | Unit tests of the gate's decision (U-2). |
| `pnpm-audit-allow.json` | Dated, expiring pnpm advisory suppressions. Ships empty (`[]`). |

The checks these support:

| Check | Where | Required? | Judges |
|-------|-------|-----------|--------|
| Repository governance | `ci.yml`, job `governance` | yes, once S05 applies the ruleset | the text of this repository's settings files |
| Dependency review | `ci.yml`, job `dependency-review` | yes, once S05 applies the ruleset | the dependency versions a pull request **adds** (moderate and above, every scope); see the NuGet snapshot note below |
| NuGet audit (default branch) | `dependency-audit.yml` | no | every NuGet package on `main`, daily and on push |
| pnpm audit (default branch) | `dependency-audit.yml` | no | every package in `Auth_UI/pnpm-lock.yaml`, daily and on push |

The required gate is differential on purpose: an advisory published tomorrow against a
version nobody touched must not block unrelated pull requests. The absolute audits exist to
detect exactly that case, so they are never required: red there means "triage", not "blocked".

**NuGet snapshot note.** npm versions come from `pnpm-lock.yaml` on both sides of the
comparison. NuGet packages, transitive ones included, come from the snapshots that automatic
dependency submission uploads, and it uploads one for pull-request heads too (observed on the
pull request that added this file). When the head has a snapshot and the base has none, the
job log says "The number of snapshots compared for the base SHA (0) and the head SHA (1) do
not match", and every NuGet package counts as added. Then an advisory on a NuGet package the
pull request did not touch can fail it. The way back to green is the usual one: upgrade, or a
dated `allow-ghsas` entry (Suppression, below).

## Running the governance harness (S03)

From the repository root, with Node 24 or later (no install; `node:` built-ins only):

```bash
node --test "Tools/github/*.test.mjs"
```

This is exactly what the "Repository governance" job runs. The guards read the files that
`git ls-files` lists, so a new file counts only once it is tracked (`git add -N <path>` is
enough locally), and they normalise CRLF, so a Windows checkout and CI agree.

Every guard is a pure function that returns violation messages, each starting with its name
(`G-S03a: …`). A red run names the guard and the file. Fix the file; if the guard itself is
wrong, fix the guard in the same pull request so both changes are visible in the diff.

### The YAML reader

There is no YAML library, so `governance.test.mjs` reads a named subset: block mappings and
sequences, literal `|` and `|-` blocks, plain and quoted scalars, comments, `${{ }}`, and
one-line flow collections. Anything else (anchors, aliases, merge keys, tags, `---`, folded
`>`, `|2`, `?` keys, multi-line flow collections, tab indentation, duplicate keys) is a
violation that names the construct (G-S03g), never a silent skip. If a workflow needs a
construct outside the subset, extend the reader in the same pull request, with a test.

### Adding a guard

1. Write a pure function `guardSomething(input) → string[]` in `governance.test.mjs`, messages
   prefixed with the guard's id.
2. Add a `describe("G-XXXx …")` block with two kinds of test:
   - the real tracked files give `[]`, with an assertion that the input is not empty (a guard
     over zero files proves nothing);
   - a **negative control** for every rule: a fixture that breaks the rule and must produce
     that rule's message (`expectViolation`).
3. Keep fixtures as strings in the test file, or temporary files written at run time. Never
   commit a fixture named like a manifest (`package.json`, `*.csproj`, `pnpm-lock.yaml`,
   `action.yml`): the dependency graph and CodeQL would treat it as real.
4. Prove the negative control can fail: disable the rule for a moment and watch its fixture
   test turn red.

## Triage: alerts, Dependabot pull requests and a red audit (S03)

**A red "Dependency review" on a pull request.** The pull request adds a version with a known
advisory. Upgrade to the first fixed version. Only if the risk is accepted, add `allow-ghsas`
in the same pull request (see Suppression). A 403 or 404 in its log instead means the
dependency graph is off: enable it (Settings › Security and quality › Advanced Security ›
Dependency graph) and re-run the job.

**A red scheduled or push run of "Dependency audit".** It never blocks a merge. It means an
advisory now matches a version on `main`.

1. Read the job log: NuGet prints `error NU1902`–`NU1904` with the project and package;
   the pnpm gate prints `::error::` lines with the GHSA, package and version. `NU1900` or a
   pnpm registry error means the vulnerability data could not be read: re-run; if it persists,
   treat it as an outage, not a finding.
2. Fix it: a Dependabot security update (once S06 enables them), a version bump, or an
   override (pnpm `overrides`, or a direct `PackageReference` for a transitive NuGet package).
3. Only if there is no fix, or the risk is accepted: a dated suppression (next section), and
   dismiss the matching Dependabot alert with a reason.
4. At every such triage, also look for suppressions whose expiry has passed (NuGet and
   `allow-ghsas` expiries are not enforced by machine) and renew or remove them.

**Dependabot pull requests.** Expect up to five grouped version-update pull requests per
ecosystem each Monday; major upgrades arrive one per pull request. Review each like any other
pull request and merge it yourself. A Dependabot pull request that fails CI: fix it on its
branch, close it, or comment `@dependabot recreate` (commits you push stop its automatic
rebases).

**Pressure valve.** If version-update pull requests cannot be kept up with, set
`open-pull-requests-limit: 0` on the `nuget` or `npm` block of `.github/dependabot.yml` (G-S03a
allows 0–5 there). That stops version updates for that ecosystem only; security updates
continue. Put it back to 5 when the backlog is gone.

**Periodic check: automatic dependency submission is still on.** It is the only way transitive
NuGet packages reach the dependency graph, and no API reports whether it is enabled, so
switching it off would silently end alerts for transitive NuGet packages. Monthly, and after
any change to repository settings: open Insights › Dependency graph and confirm that a NuGet
package that appears in no `.csproj` (a transitive one) is listed. If not, re-enable it
(Settings › Security and quality › Advanced Security › Dependency graph › Automatic dependency
submission) and push any manifest change to `main`.

## Suppression: one format, three places (S03)

A suppression is the last resort, for an advisory with no fix or a risk accepted in review.
Every suppression names the GHSA, the date, an expiry at most 90 days after the date, and a
written reason, in one of three places, and shows up in a pull request diff:

| Check | Where | Form |
|-------|-------|------|
| NuGet audit | `Auth/Directory.Build.props`, an `ItemGroup` | the comment line `<!-- allow GHSA-xxxx-xxxx-xxxx YYYY-MM-DD until YYYY-MM-DD <reason> -->` directly above `<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-xxxx-xxxx-xxxx" />` (G-S03h) |
| Dependency review | `ci.yml`, job `dependency-review`, `with:` | one comment line per GHSA directly above the key, `# allow GHSA-xxxx-xxxx-xxxx YYYY-MM-DD until YYYY-MM-DD <reason>`, then `allow-ghsas: GHSA-…, GHSA-…` on one line; the two GHSA sets must match (G-S03j) |
| pnpm audit | `Tools/github/pnpm-audit-allow.json` | `{ "ghsa": "GHSA-…", "date": "YYYY-MM-DD", "expires": "YYYY-MM-DD", "reason": "…" }` (G-S03k) |

Never suppress anywhere else: no `NoWarn` for NU190x, no audit keys in
`Auth_UI/pnpm-workspace.yaml` or `package.json`, no `pnpm audit --ignore`, no audit or
warning properties in workflow `env:` or on `dotnet` command lines, no second
`Directory.Build.*`, response file (`.rsp`), `.user` file or `<auditSources>` in a
`nuget.config` (the guards reject all of them).

What the guards check is static: format, matching GHSA, and the 90-day window. They never
read today's date, so a required check cannot turn red just because time passed.

**Expiry.** The pnpm gate enforces it: an expired entry suppresses nothing, the gate names it,
and "pnpm audit" is red again until the entry is renewed or the package upgraded. For NuGet
and `allow-ghsas` the expiry is written but not enforced by machine; the triage step above
catches it. **Renewing** an entry is a new decision: a pull request that sets a new date, a
new expiry (at most 90 days later) and a reason that says why the risk is still accepted.

A suppression hides the advisory for every package that shares it, not only the one you
looked at. Also dismiss the matching Dependabot alert with a reason: the alert and the
checks are separate.

## Pull requests from forks (S03)

The repository is public, so pull requests from forks are expected. Two facts shape the rule:

- "Dependency review" may fail with 403 on a fork's pull request (the comparison API refuses
  forks), and after S05 that blocks the merge.
- A pull request opened **from a branch inside this repository** runs the workflow files that
  branch carries, with the permissions those files declare. Pushing a fork's commit to an
  internal branch as it is would give that code whatever a changed workflow asks for, before
  any guard runs. Today the repository has no Actions secret, so the exposure is the job
  token; the rule holds regardless of what secrets exist later.

**The rule: read the fork's whole diff before anything reaches a branch here, and classify
every file against this positive allowlist of inert file types.**

- `.cs` files under `Auth/`, except under `Auth/Auth_DB/`;
- `.ts`, `.tsx` and `.css` files under `Auth_UI/apps/*/src/` and `Auth_UI/packages/*/src/`;
- `.md` files.

None of these sets job permissions or triggers, and none is imported by MSBuild or pnpm as a
build tool: no `.csproj`, `.props` or `.targets` in this repository runs code from a `.cs`
file during the build (no analyzer from the repository, no `UsingTask`, no `Exec`).

**Every file in the allowlist:** the fork's commit may be pushed as it is to a branch here and
opened as a pull request. Its code then runs tests and lint in `ci.yml` jobs that hold
`contents: read` only.

**Any file outside the allowlist** (anything under `.github/`, any `*.csproj`, `*.props`,
`*.targets`, `*.user`, `*.rsp`, `Directory.Build.*`, `nuget.config`, `global.json`,
`dotnet-tools.json`, any `package.json`, `Auth_UI/pnpm-lock.yaml`,
`Auth_UI/pnpm-workspace.yaml`, `.npmrc`, `.pnpmfile*`, anything under `Tools/`, any binary,
any file whose name starts with a dot, any file type new to the repository): do **not** push
the fork's commit. Instead:

1. Create a branch from `main`.
2. Copy over by hand only the allowlisted files, after reading them.
3. Re-author the dependency change yourself, in your own commit, by explicit paths: the
   `PackageReference` line in the `.csproj`, or the dependency field in `package.json`.
   Regenerate the lockfile locally with `pnpm install --lockfile-only`; never copy
   `pnpm-lock.yaml` from the fork.
4. Anything else outside the allowlist is not carried over. If it is needed, write it
   yourself in a separate pull request, reviewed like any of yours.
5. Close the fork's pull request with a link to the new one, and credit its author in the
   commit message.

A deny list is not enough: MSBuild also imports files no reasonable deny pattern names, such
as `X.csproj.user` next to a project. The package being added is itself a trust decision,
exactly as if you had added it.
