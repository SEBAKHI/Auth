import "./embed.css"

import { AccountBarPanel } from "./account-bar-panel"
import { bootEmbed } from "./embed-boot"

void bootEmbed(() => <AccountBarPanel />)
