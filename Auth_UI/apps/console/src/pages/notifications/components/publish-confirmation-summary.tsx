import { useTranslation } from "react-i18next"

import {
  Item,
  ItemContent,
  ItemDescription,
  ItemGroup,
  ItemTitle,
} from "@authsystem/ui/item"

/** The immutable target an administrator is about to publish or unpublish. */
export function PublishConfirmationSummary({
  item,
  revision,
  scope,
}: {
  item: string
  revision: string
  scope: string
}) {
  const { t } = useTranslation()

  return (
    <ItemGroup className="gap-2">
      <Item variant="muted" size="sm">
        <ItemContent>
          <ItemTitle>{t("notifications.confirmationItem")}</ItemTitle>
          <ItemDescription className="line-clamp-none break-words">
            <bdi>{item}</bdi>
          </ItemDescription>
        </ItemContent>
      </Item>
      <Item variant="muted" size="sm">
        <ItemContent>
          <ItemTitle>{t("notifications.confirmationVersion")}</ItemTitle>
          <ItemDescription className="line-clamp-none break-words">
            <bdi>{revision}</bdi>
          </ItemDescription>
        </ItemContent>
      </Item>
      <Item variant="muted" size="sm">
        <ItemContent>
          <ItemTitle>{t("notifications.confirmationScope")}</ItemTitle>
          <ItemDescription className="line-clamp-none break-words">
            <bdi>{scope}</bdi>
          </ItemDescription>
        </ItemContent>
      </Item>
    </ItemGroup>
  )
}
