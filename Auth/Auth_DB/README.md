# Auth_DB

The SQL Server database project for the Auth system. Building it produces a DACPAC: the
table, index and stored-procedure definitions under `dbo/`, plus a post-deployment script
that seeds reference data.

## What a publish does

1. **Schema.** SqlPackage (or Visual Studio's Publish) compares the DACPAC with the target
   database and applies the difference.
2. **Seed.** `dbo/PostDeployment/Script.PostDeployment.sql` runs and includes every file in
   `dbo/Scripts/SeedData/` with `:r`. Together they insert the built-in roles, the permission
   catalogue and its role grants, the system and admin users, the notification types, the
   email layout and templates, and the privacy policy.

3. **Upgrades.** The post-deployment script's last step `:r`-includes the dated scripts in
   `dbo/Scripts/Upgrades/`, which move an existing database to the state a fresh one already
   has (see [Changing seeded data](#changing-seeded-data)). Each is idempotent: on a fresh
   database, and on every publish after its first, it changes nothing.

Every seed insert is guarded by `IF NOT EXISTS`. Publishing again adds whatever is missing
and overwrites nothing, so an administrator's edits to a template or a role survive every
publish.

The seed is the **current** state, not a history of it. A fresh database ends up in the
final state after its first publish; the upgrade step runs there too, and changes nothing.

After the first publish, the seeded `admin` account has **no password**. Run the
`Auth_Setup` console app, give it the password you chose, and execute the `UPDATE`
statement it prints.

## Changing seeded data

Because seeds never overwrite, editing a seed file changes **new** databases only. When an
existing deployment also needs the change:

1. Change the seed file, so fresh databases get the new state.
2. Add a dated, idempotent script under `dbo/Scripts/Upgrades/` that moves an existing
   database to the same state, and either `:r`-include it from the post-deployment script or
   document it as a manual step.
3. Once every known deployment has run it, delete the script. Its history stays in git.

The same rule covers an object the project stops defining. Publishing never drops it from
an existing database: `DropObjectsNotInSource` is False by default and in the deployment
guide's publish settings, so the upgrade script drops it explicitly.

Upgrade scripts are `:r`-included, so each one is pure ASCII with no BOM, sets
`QUOTED_IDENTIFIER ON`, and ends with its own `GO`.

Current upgrade scripts:

| Script | What it does | Delete when |
|---|---|---|
| `2026-10-12_DropDeadLockProcedures.sql` | Drops `sp_CheckAccountLockout`, `sp_RecordLoginAttempt` and `sp_ValidateCredentials`, which nothing called and two of which wrote the account-lock columns by rules the API no longer follows (X12) | Every known deployment and every development database has been published with it |
| `2026-10-23_QualifyEnabledRecoveryCodes.sql` | Sets `TwoFactorAuth.RecoveryCodesQualified = 1` on every row with an enabled authenticator app: the new column defaults to 0 (fail closed), and those codes have always counted as a second factor (S24) | Every known deployment and every development database has been published with it |

## Databases created before the seed consolidation

Until commit `8ae40fbe`, this project carried upgrade and repair scripts for older
databases (`dbo/Scripts/Upgrades/`, plus two one-off scripts in `dbo/Scripts/`). They were
removed once every known deployment had run them.

If your database was created from an earlier version of this repository, first bring it up
to date with those scripts. This command puts them back in your working tree, exactly as
they were at that commit:

```bash
git restore --source 8ae40fbe -- Auth/Auth_DB/dbo/Scripts
```

Each script's header says whether it runs during the publish or by hand, and in what
order. The simplest route is to publish the DACPAC built at `8ae40fbe`, which runs the
publish-time scripts, then run the manual ones. After that, publish this version as usual.

Two differences remain on an upgraded database. Neither changes what anyone can do:

- **Deactivated permissions.** It keeps some permission rows with `IsActive = 0`: the
  `auth:*` family, `profile:read`, `profile:update`, `org:read`, `org:delete`,
  `org:permissions:grant` and `org:permissions:revoke`. Every effective-permission query
  ignores inactive rows, so they grant nothing.
- **Old parent links.** The permissions seeded by Step 7 and by seeds 13, 15 and 17 keep
  `auth:*` as their `ParentId`; a fresh database hangs them from `*` instead. `ParentId`
  only arranges the catalogue: grants match by code prefix.
