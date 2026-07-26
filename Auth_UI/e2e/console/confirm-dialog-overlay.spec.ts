import { expect, test, type Page } from "@playwright/test"

/**
 * Regression guard for the stranded-overlay bug: after a mutation success
 * closes a ConfirmDialog (Radix AlertDialog), the overlay must leave the DOM
 * immediately and pointer events must keep working — an exit animation used
 * to let Radix Presence strand an invisible full-screen overlay that
 * swallowed every click until reload.
 *
 * Needs the API running plus seeded admin credentials:
 *   E2E_EMAIL=admin@company.com E2E_PASSWORD=... pnpm e2e --project=console
 *
 * The signed-in console renders in the admin's preferred language, so every
 * text selector accepts both the English and Arabic strings.
 */
const email = process.env.E2E_EMAIL
const password = process.env.E2E_PASSWORD

const L = {
  newUser: /New user|مستخدم جديد/,
  email: /^(Email|البريد الإلكتروني)$/,
  password: /^(Password|كلمة المرور)$/,
  firstName: /First name|الاسم الأول/,
  lastName: /Last name|اسم العائلة/,
  create: /^(Create|إنشاء)$/,
  createdToast: /User created|تم إنشاء المستخدم/,
  searchPlaceholder: /Search by name or email|ابحث بالاسم أو البريد/,
  actions: /^(Actions|إجراءات)$/,
  delete: /^(Delete|حذف)$/,
  deletedToast: /User deleted|تم حذف المستخدم\./,
  showDeleted: /Show deleted|عرض المحذوفين/,
  hardDelete: /^(Delete permanently|حذف نهائي)$/,
  hardDeletedToast: /User permanently deleted|تم حذف المستخدم نهائيًا/,
}

const overlays = (page: Page) =>
  page.locator(
    '[data-slot="alert-dialog-overlay"], [data-slot="dialog-overlay"]'
  )

test.describe("confirm dialog overlay cleanup", () => {
  test.skip(
    !email || !password,
    "requires a running API and E2E_EMAIL/E2E_PASSWORD"
  )

  test("success-close removes the overlay and keeps the page clickable", async ({
    page,
  }) => {
    const testEmail = `e2e-overlay-${Date.now()}@example.com`

    await page.goto("/login")
    await page.locator('input[type="email"]').fill(email!)
    await page.locator('input[type="password"]').fill(password!)
    await page.locator('button[type="submit"]').click()
    // Signed-in shell: the sidebar's users link is language-independent.
    await expect(
      page.locator('[data-sidebar="menu-button"][href="/users"]')
    ).toBeVisible()

    await page.goto("/users")

    // Create a throwaway user (exercises the Dialog overlay's success-close).
    await page.getByRole("button", { name: L.newUser }).click()
    await page.getByLabel(L.email).fill(testEmail)
    await page.getByLabel(L.password).fill("E2e-Overlay-1!")
    await page.getByLabel(L.firstName).fill("E2E")
    await page.getByLabel(L.lastName).fill("Overlay")
    await page.getByRole("button", { name: L.create }).click()
    await expect(page.getByText(L.createdToast)).toBeVisible()
    await expect(overlays(page)).toHaveCount(0)

    // Soft-delete it via ConfirmDialog — the original bug's exact path.
    const search = page.getByPlaceholder(L.searchPlaceholder)
    await search.fill(testEmail)
    const row = page.locator("tbody tr", { hasText: testEmail })
    await row.getByRole("button", { name: L.actions }).click()
    await page.getByRole("menuitem", { name: L.delete }).click()
    await page.getByRole("button", { name: L.delete }).click()
    await expect(page.getByText(L.deletedToast)).toBeVisible()

    // The invariant: overlay gone immediately, background interactive again.
    await expect(overlays(page)).toHaveCount(0)
    await search.click()
    await expect(search).toBeFocused()

    // Cleanup: permanently remove the throwaway user (a second success-close).
    await page.getByLabel(L.showDeleted).check()
    const deletedRow = page.locator("tbody tr", { hasText: testEmail })
    await deletedRow.getByRole("button", { name: L.actions }).click()
    await page.getByRole("menuitem", { name: L.hardDelete }).click()
    await page.locator("#hard-delete-confirm").fill(testEmail)
    await page.getByRole("button", { name: L.hardDelete }).click()
    await expect(page.getByText(L.hardDeletedToast)).toBeVisible()
    await expect(overlays(page)).toHaveCount(0)
  })
})
