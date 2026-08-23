/**
 * The dashboard tabs, as loaders rather than as components.
 *
 * Kept apart from the components they feed so a test can resolve each one and
 * prove the export it names still exists. A renamed export would otherwise fail
 * at runtime, on a tab, in front of someone - and only on the tab nobody opened
 * while reviewing.
 */
export const TAB_LOADERS = {
  overview: () =>
    import("./overview-tab").then((m) => ({ default: m.OverviewTab })),
  security: () =>
    import("./security-tab").then((m) => ({ default: m.SecurityTab })),
  people: () => import("./people-tab").then((m) => ({ default: m.PeopleTab })),
  apps: () => import("./apps-tab").then((m) => ({ default: m.AppsTab })),
  audit: () => import("./audit-tab").then((m) => ({ default: m.AuditTab })),
}
