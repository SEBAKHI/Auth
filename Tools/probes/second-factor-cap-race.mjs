// X12 PR B, B1: the second-factor cap holds under a real concurrent burst on SQL Server.
//
// A throwaway database on (localdb)\MSSQLLocalDB, created and dropped here (never the dev
// database; no owner step). The statement is read from the C# source
// (TwoFactorStateStore.ReserveAttemptSql), the table DDL from Auth_DB. Barrier: one session
// holds an exclusive app lock in a transaction; every racer waits on a shared app lock, so
// all of them are in flight together, then the gate commits.
//
// Expected with the cap: A 1 granted (count 100); B 5 granted (96..100), final 100;
// C 0 granted, final 100. With --no-cap (the deliberate break): B 12 granted, final 107;
// C 1 granted, final 101.
//
// usage: node Tools/probes/second-factor-cap-race.mjs [<repository root>] [--no-cap]
import { spawn, spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const root = process.argv.slice(2).find((arg) => !arg.startsWith("--")) ?? path.resolve(here, "..", "..");
const noCap = process.argv.includes("--no-cap");
const server = "(localdb)\\MSSQLLocalDB";
const db = `X12CapRace_${process.pid}`;
const work = fs.mkdtempSync(path.join(os.tmpdir(), "x12-cap-race-"));

// sqlcmd -E with -i misreads the empty SQLCMDUSER/SQLCMDPASSWORD variables some shells export.
const sqlEnv = { ...process.env };
delete sqlEnv.SQLCMDUSER;
delete sqlEnv.SQLCMDPASSWORD;
delete sqlEnv.SQLCMDSERVER;

const source = fs.readFileSync(path.join(root, "Auth/Auth.Infrastructure/Persistence/TwoFactorStateStore.cs"), "utf8");
let reserve = source.match(/internal const string ReserveAttemptSql = @"([\s\S]*?)";/)[1];
if (noCap) {
  const cap = /\s+AND \(@Hard IS NULL OR \[FailedAttempts\] < @Hard\)/;
  if (!cap.test(reserve)) throw new Error("cap condition not found");
  reserve = reserve.replace(cap, "");
}
const tableDdl = fs.readFileSync(path.join(root, "Auth/Auth_DB/dbo/Tables/Security/TwoFactorAuth.sql"), "utf8").split(/^\s*GO\s*$/m)[0];

function sqlFile(name, text) {
  const file = path.join(work, name);
  fs.writeFileSync(file, text);
  return file;
}

function run(database, text, name) {
  const r = spawnSync("sqlcmd", ["-S", server, "-d", database, "-E", "-b", "-I", "-h", "-1", "-W", "-i", sqlFile(name, text)], { encoding: "utf8", env: sqlEnv });
  if (r.status !== 0) throw new Error(`${name}: ${r.stdout}\n${r.stderr}`);
  return r.stdout;
}

const userId = "2f0a5c4e-8b1d-4c39-9a57-1e6d3b2c7f10";

function scenario(label, { count, lockedUntil, maxAttempts, hard, racers }) {
  run("master", `CREATE DATABASE [${db}];`, "create.sql");
  run(db, `CREATE TABLE [dbo].[Users] ([Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);\nGO\n${tableDdl}\nGO\n` +
    `INSERT INTO [dbo].[Users] ([Id]) VALUES ('${userId}');\n` +
    `INSERT INTO [dbo].[TwoFactorAuth] ([UserId], [SecretKey], [IsEnabled], [FailedAttempts], [LockedUntil]) VALUES ('${userId}', N'v2:x', 1, ${count}, ${lockedUntil});`,
    "schema.sql");

  const racer = `SET NOCOUNT ON;
DECLARE @UserId UNIQUEIDENTIFIER = '${userId}', @MaxAttempts INT = ${maxAttempts}, @LockoutMinutes INT = 15, @Hard INT = ${hard ?? "NULL"};
DECLARE @r INT;
EXEC @r = sp_getapplock @Resource = 'gate', @LockMode = 'Shared', @LockOwner = 'Session', @LockTimeout = 60000;
EXEC sp_releaseapplock @Resource = 'gate', @LockOwner = 'Session';
DECLARE @out TABLE ([FailedAttempts] INT, [LockedUntil] DATETIME2);
BEGIN TRY
${reserve.replace("OUTPUT inserted.[FailedAttempts], inserted.[LockedUntil]", "OUTPUT inserted.[FailedAttempts], inserted.[LockedUntil] INTO @out")};
  SELECT CONCAT('granted=', @@ROWCOUNT, ' count=', ISNULL((SELECT TOP 1 CAST([FailedAttempts] AS NVARCHAR(10)) FROM @out), '-'));
END TRY
BEGIN CATCH
  SELECT CONCAT('error=', ERROR_NUMBER());
END CATCH`;
  const racerFile = sqlFile("racer.sql", racer);

  // The gate: an exclusive app lock held open in a transaction, fed through stdin.
  const gate = spawn("sqlcmd", ["-S", server, "-d", db, "-E", "-I"], { stdio: ["pipe", "pipe", "pipe"], env: sqlEnv });
  gate.stdin.write("BEGIN TRAN;\nEXEC sp_getapplock @Resource = 'gate', @LockMode = 'Exclusive', @LockOwner = 'Transaction';\nGO\n");

  return new Promise((resolve) => {
    setTimeout(() => {
      const outputs = [];
      let done = 0;
      for (let i = 0; i < racers; i++) {
        const p = spawn("sqlcmd", ["-S", server, "-d", db, "-E", "-I", "-h", "-1", "-W", "-i", racerFile], { stdio: ["ignore", "pipe", "pipe"], env: sqlEnv });
        let out = "";
        p.stdout.on("data", (d) => (out += d));
        p.on("close", () => {
          outputs.push(out.trim());
          if (++done === racers) {
            finish();
          }
        });
      }

      // Wait until every racer is blocked on the gate, then release them together.
      const poll = setInterval(() => {
        const waiting = Number(run(db, "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = 'APPLICATION' AND request_status = 'WAIT';", "poll.sql").trim());
        if (waiting >= racers) {
          clearInterval(poll);
          console.log(`${label}: held together ${waiting}/${racers}`);
          gate.stdin.write("COMMIT;\nGO\nexit\n");
        }
      }, 200);

      function finish() {
        const final = run(db, `SET NOCOUNT ON; SELECT CONCAT('final count=', [FailedAttempts]) FROM [dbo].[TwoFactorAuth];`, "final.sql").trim();
        const granted = outputs.filter((o) => o.includes("granted=1")).length;
        const refused = outputs.filter((o) => o.includes("granted=0")).length;
        const errors = outputs.filter((o) => o.includes("error=")).length;
        console.log(`${label}: granted=${granted} refused=${refused} errors=${errors}; ${final}; winners: ${outputs.filter((o) => o.includes("granted=1")).join(" | ")}`);
        resolve({ granted, refused, errors, final });
      }
    }, 1500);
  });
}

const drop = () => run("master", `ALTER DATABASE [${db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${db}];`, "drop.sql");

async function main() {
  const results = [];
  try {
    // A: today's parameters, one attempt below the limit: exactly one is let through, and its count is the limit.
    results.push(["A real 5/15, n=99, hard=100", await scenario("A", { count: 99, lockedUntil: "NULL", maxAttempts: 5, hard: noCap ? null : 100, racers: 12 })]);
    drop();
    // B: the timed lock neutralized (@MaxAttempts 1000), so only the cap can stop the burst.
    results.push(["B cap alone, n=95, hard=100", await scenario("B", { count: 95, lockedUntil: "NULL", maxAttempts: 1000, hard: noCap ? null : 100, racers: 12 })]);
    drop();
    // C: at the limit with the timed lock long lapsed: nothing is let through, ever.
    results.push(["C n=100, lapsed lock, hard=100", await scenario("C", { count: 100, lockedUntil: "DATEADD(DAY, -1, SYSUTCDATETIME())", maxAttempts: 5, hard: noCap ? null : 100, racers: 6 })]);
  } finally {
    try { run("master", `IF DB_ID(N'${db}') IS NOT NULL BEGIN ALTER DATABASE [${db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${db}]; END`, "drop.sql"); } catch {}
    fs.rmSync(work, { recursive: true, force: true });
  }
  console.log(JSON.stringify(results));
}

main();
