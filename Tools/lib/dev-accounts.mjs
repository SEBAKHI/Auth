/**
 * Accounts for the development tools under Tools/, on an API whose email sending is off.
 *
 * The probes and scripts read the one-time codes and links they need from the API's
 * Serilog file, which the API writes them to only while Email:Enabled is off. Since
 * OI-215 a sign-up start is the exception: registration/start refuses every address
 * while email is off (403 User.EmailCodeSignInUnavailable), because nothing could
 * deliver its code. So a tool gets an account in one of two ways:
 *
 *   - confirmedAccountSql: a confirmed account with no password, written directly to a
 *     local database the tool already writes to. It then takes its first password the
 *     way any account without one can ("forgot password", the link from the log, the
 *     reset) and signs in. Nothing about email sending changes, so this also works for
 *     an API run with AUTH_DISABLE_DB_SETTINGS=true.
 *   - withEmailSendingOn: for a tool that needs a real pending registration and its
 *     code, it switches email sending on for one step through the System settings API
 *     (an administrator holding system-settings:manage), then puts the Email section
 *     back exactly as it was. In Development the start writes the code it sends to the
 *     log ("Development only - OTP for …"). The message itself goes wherever the
 *     development SMTP settings point, or fails to send: nothing reads it.
 *
 * Node built-ins only; localhost APIs and local SQL Servers only, as each tool checks.
 */

/**
 * The T-SQL that creates a confirmed account with no password for `email` and selects
 * its id. Only the columns without a default are written, plus the confirmation and a
 * name; the row is the one a self-registration completes, without its audit entry.
 * QUOTED_IDENTIFIER must be on (sqlcmd -I): the Users table has filtered indexes.
 * @param {string} email an address the tool generated (letters, digits, - . @ only)
 * @param {{ firstName?: string, lastName?: string }} [name]
 */
export function confirmedAccountSql(email, { firstName = "Probe", lastName = "User" } = {}) {
  for (const value of [email, firstName, lastName]) {
    if (!/^[A-Za-z0-9.@+-]+$/.test(value)) throw new Error(`unsafe value for the account row: ${value}`);
  }
  return [
    "DECLARE @Id UNIQUEIDENTIFIER = NEWID();",
    "INSERT INTO dbo.Users (Id, Username, Email, NormalizedEmail, FirstName, LastName, IsEmailConfirmed, CreatedBy)",
    `VALUES (@Id, N'${email}', N'${email}', UPPER(N'${email}'), N'${firstName}', N'${lastName}', 1, @Id);`,
    "SELECT CONVERT(varchar(36), @Id);",
  ].join("\n");
}

const EMAIL_SECTION = "Email";

async function settingsCall(api, token, method, path, body) {
  const response = await fetch(`${api}${path}`, {
    method,
    headers: { "content-type": "application/json", authorization: `Bearer ${token}` },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* non-JSON body */ }
  return { status: response.status, json, text };
}

async function emailSection(api, token) {
  const r = await settingsCall(api, token, "GET", "/api/v1/admin/system-settings");
  if (r.status !== 200) throw new Error(`reading the System settings: ${r.status} ${r.text.slice(0, 200)}`);
  const section = r.json?.sections?.find((s) => String(s.key).toLowerCase() === EMAIL_SECTION.toLowerCase());
  if (!section) throw new Error("the System settings answer has no Email section");
  return section;
}

// The saved overrides of the section, as the save expects them: the save replaces the
// whole row, so a field left out falls back to the files.
const overridesOf = (section) =>
  Object.fromEntries(
    section.fields
      .filter((field) => field.overrideValue !== null && field.overrideValue !== undefined)
      .map((field) => [field.path, field.overrideValue]),
  );

async function saveEmailSection(api, token, overrides, rowVersion) {
  const r = await settingsCall(api, token, "PUT", `/api/v1/admin/system-settings/${EMAIL_SECTION}`, {
    overrides,
    rowVersion: rowVersion ?? null,
  });
  if (r.status !== 200) throw new Error(`saving the Email section: ${r.status} ${r.text.slice(0, 300)}`);
}

/**
 * Runs `step` with email sending on, then restores the Email section: the same saved
 * overrides, or no saved row at all when there was none. Does nothing around `step`
 * when email sending is already on. A failed restore throws, naming what to undo.
 * @template T
 * @param {{ api: string, token: string }} admin the API's base URL and an administrator's access token
 * @param {() => Promise<T>} step
 * @returns {Promise<T>}
 */
export async function withEmailSendingOn({ api, token }, step) {
  const before = await emailSection(api, token);
  if (before.fields.find((field) => field.path === "Enabled")?.effectiveValue === true) return step();

  const saved = overridesOf(before);
  const hadRow = before.rowVersion !== null && before.rowVersion !== undefined;
  await saveEmailSection(api, token, { ...saved, Enabled: true }, before.rowVersion);

  let outcome;
  try {
    outcome = { value: await step() };
  } catch (error) {
    outcome = { error };
  }

  try {
    if (hadRow) {
      await saveEmailSection(api, token, saved, (await emailSection(api, token)).rowVersion);
    } else {
      const r = await settingsCall(api, token, "POST", `/api/v1/admin/system-settings/${EMAIL_SECTION}/reset`);
      if (r.status < 200 || r.status >= 300) throw new Error(`resetting the Email section: ${r.status} ${r.text.slice(0, 200)}`);
    }
  } catch (restore) {
    throw new Error(
      `email sending was switched on for one step and could not be switched back (${restore.message}); ` +
        "turn Email › Enabled off again in Platform settings" +
        (outcome.error ? `; the step itself failed too: ${outcome.error.message}` : ""),
    );
  }

  if (outcome.error) throw outcome.error;
  return outcome.value;
}
