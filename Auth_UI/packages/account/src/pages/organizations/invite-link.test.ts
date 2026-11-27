import { describe, expect, it } from "vitest"

import {
  defaultMembershipRoleId,
  enabledApplications,
  inviteDialogMode,
  matchInviteLink,
  readInviteLink,
  withoutInviteLink,
} from "./invite-link"

const read = (query: string) => readInviteLink(new URLSearchParams(query))

describe("readInviteLink", () => {
  it("is null unless the link asks to open the dialog", () => {
    expect(read("")).toBeNull()
    expect(read("role=org-member")).toBeNull()
    expect(read("invite=true&role=org-member")).toBeNull()
    expect(read("invite=0&role=org-member")).toBeNull()
  })

  it("reads the three codes, trimmed", () => {
    expect(
      read(
        "invite=1&role=%20org-member%20&application=CRM&applicationRole=sales_rep"
      )
    ).toEqual({
      role: "org-member",
      application: "CRM",
      applicationRole: "sales_rep",
    })
  })

  it("opens an empty dialog when invite=1 comes alone", () => {
    expect(read("invite=1")).toEqual({
      role: undefined,
      application: undefined,
      applicationRole: undefined,
    })
  })

  it("drops a value that is not shaped like a code", () => {
    expect(
      read("invite=1&role=<script>&application=a%20b&applicationRole=x")
    ).toEqual({
      role: undefined,
      application: undefined,
      applicationRole: undefined,
    })
    expect(read(`invite=1&role=${"a".repeat(101)}`)?.role).toBeUndefined()
    expect(read(`invite=1&role=${"a".repeat(100)}`)?.role).toBe("a".repeat(100))
  })

  it("ignores an application role that comes without its application", () => {
    expect(
      read("invite=1&applicationRole=sales_rep")?.applicationRole
    ).toBeUndefined()
  })

  it("never reads an address, whatever the link carries", () => {
    expect(read("invite=1&email=someone%40example.test")).not.toHaveProperty(
      "email"
    )
  })
})

describe("withoutInviteLink", () => {
  it("removes the link's parameters and keeps the invitations tab and everything else", () => {
    const next = withoutInviteLink(
      new URLSearchParams(
        "invite=1&role=org-member&application=CRM&applicationRole=r&members.page=2"
      ),
      "tab",
      "invitations"
    )
    expect(Object.fromEntries(next)).toEqual({
      "members.page": "2",
      tab: "invitations",
    })
  })
})

describe("matchInviteLink", () => {
  const roles = [
    { id: "admin-id", code: "org-admin" },
    { id: "member-id", code: "org-member" },
  ]
  const apps = [{ applicationId: "crm-id", applicationCode: "CRM" }]
  const appRoles = [{ id: "rep-id", code: "sales_rep" }]

  it("selects the ids of matching codes, ignoring letter case", () => {
    expect(
      matchInviteLink(
        {
          role: "ORG-MEMBER",
          application: "crm",
          applicationRole: "SALES_REP",
        },
        roles,
        apps,
        appRoles
      )
    ).toEqual({
      roleId: "member-id",
      applicationId: "crm-id",
      appRoleId: "rep-id",
    })
  })

  it("leaves a field empty when its code is not in the server's list", () => {
    // org-owner is never offered; an application the organization has not enabled
    // is not in the list; a role the caller may not assign is not either.
    expect(
      matchInviteLink({ role: "org-owner" }, roles, apps, appRoles).roleId
    ).toBeUndefined()
    expect(
      matchInviteLink({ application: "BILLING" }, roles, apps, appRoles)
    ).toEqual({
      roleId: undefined,
      applicationId: undefined,
      appRoleId: undefined,
    })
    expect(
      matchInviteLink(
        { application: "CRM", applicationRole: "sales_manager" },
        roles,
        apps,
        appRoles
      )
    ).toEqual({
      roleId: undefined,
      applicationId: "crm-id",
      appRoleId: undefined,
    })
  })

  it("never picks an application role without its application", () => {
    expect(
      matchInviteLink({ applicationRole: "sales_rep" }, roles, apps, appRoles)
        .appRoleId
    ).toBeUndefined()
  })
})

describe("enabledApplications", () => {
  it("keeps active applications that have an id", () => {
    expect(
      enabledApplications([
        { applicationId: "a", isActive: true },
        { applicationId: "b", isActive: false },
        { applicationId: null, isActive: true },
      ])
    ).toEqual([{ applicationId: "a", isActive: true }])
    expect(enabledApplications(undefined)).toEqual([])
  })
})

describe("defaultMembershipRoleId", () => {
  // The member role is not first: the default is found by its code.
  const membership = [
    { id: "admin-id", code: "org-admin" },
    { id: "member-id", code: "org-member" },
  ]

  it("is the member role, by its code, when nobody chose a role", () => {
    expect(defaultMembershipRoleId(null, membership)).toBe("member-id")
    expect(defaultMembershipRoleId({ application: "CRM" }, membership)).toBe(
      "member-id"
    )
  })

  it("is nothing when the link named a role, matched or not", () => {
    expect(
      defaultMembershipRoleId({ role: "org-admin" }, membership)
    ).toBeUndefined()
    expect(
      defaultMembershipRoleId({ role: "unknown" }, membership)
    ).toBeUndefined()
  })

  it("is nothing when the list has no member role or has not arrived", () => {
    expect(
      defaultMembershipRoleId(null, [{ id: "admin-id", code: "org-admin" }])
    ).toBeUndefined()
    expect(defaultMembershipRoleId(null, undefined)).toBeUndefined()
  })
})

describe("inviteDialogMode", () => {
  const membership = [
    { id: "admin-id", code: "org-admin" },
    { id: "member-id", code: "org-member" },
  ]
  const button = {
    link: null,
    membershipRoles: membership,
    roleId: "member-id",
    applicationId: undefined,
    appRoleId: undefined,
    pending: false,
    failed: false,
  }
  const fullLink = {
    ...button,
    link: { role: "org-member", application: "CRM", applicationRole: "rep" },
    applicationId: "crm-id",
    appRoleId: "rep-id",
  }

  it("is quick from the button once the member role is chosen", () => {
    expect(inviteDialogMode(button)).toBe("quick")
  })

  it("is quick from a link whose every choice matched", () => {
    expect(inviteDialogMode(fullLink)).toBe("quick")
    expect(inviteDialogMode({ ...button, link: { role: "org-member" } })).toBe(
      "quick"
    )
  })

  it("is pending while a list it needs is loading", () => {
    expect(
      inviteDialogMode({
        ...button,
        membershipRoles: undefined,
        roleId: undefined,
        pending: true,
      })
    ).toBe("pending")
  })

  it("is full when a list failed, even while another loads", () => {
    expect(inviteDialogMode({ ...button, failed: true })).toBe("full")
    expect(inviteDialogMode({ ...button, failed: true, pending: true })).toBe(
      "full"
    )
  })

  it("is full when the role is above member, so the inviter sees what is given", () => {
    expect(inviteDialogMode({ ...fullLink, roleId: "admin-id" })).toBe("full")
  })

  it("is full when no role is chosen or the list has no member role", () => {
    expect(inviteDialogMode({ ...button, roleId: undefined })).toBe("full")
    expect(
      inviteDialogMode({
        ...button,
        membershipRoles: [{ id: "admin-id", code: "org-admin" }],
        roleId: "admin-id",
      })
    ).toBe("full")
  })

  it("is full when the application or its role in a link found no match", () => {
    expect(
      inviteDialogMode({
        ...fullLink,
        applicationId: undefined,
        appRoleId: undefined,
      })
    ).toBe("full")
    expect(inviteDialogMode({ ...fullLink, appRoleId: undefined })).toBe("full")
  })
})
