import { test } from "@playwright/test"

import {
  expectARefusedCookieInTheSameSessionToBeNamed,
  expectARestartToLandOnSignIn,
  expectTheBodyCarriesTheChoice,
  expectTheBox,
} from "../stay-signed-in-flow"

/** OI-154 on the accounts app's sign-in page, the one relying parties send people to. */

test("the sign-in page offers Stay signed in, unticked, with its explanation", async ({ page }) => {
  await expectTheBox(page)
})

for (const tick of [false, true]) {
  test(`the sign-in sends staySignedIn: ${tick}`, async ({ page }) => {
    await expectTheBodyCarriesTheChoice(page, tick)
  })
}

test("after a browser restart on an unticked sign-in, the app lands on its sign-in page: no loop, no error, no cookie note", async ({
  page,
  browser,
  baseURL,
}) => {
  await expectARestartToLandOnSignIn(page, browser, baseURL!)
})

test("in the same browser session, a refresh that finds no cookie still names the browser's refusal", async ({
  page,
}) => {
  await expectARefusedCookieInTheSameSessionToBeNamed(page)
})
