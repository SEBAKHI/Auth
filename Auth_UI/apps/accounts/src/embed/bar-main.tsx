import "./embed.css"

import { AccountBar } from "./account-bar"
import { bootEmbed } from "./embed-boot"

void bootEmbed(() => <AccountBar />)
