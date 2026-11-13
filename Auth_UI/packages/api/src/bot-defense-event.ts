/**
 * Raised when a request comes back refused by the bot check (S13, REVIEW 167 F6):
 * `BotDefense.TokenMissing`, `BotDefense.TokenRejected` or `BotDefense.Unavailable`.
 * A page that could not read the check's configuration shows nothing and lets
 * its requests go (at Off nothing is checked); once the server says it checks,
 * the page reads the configuration again and shows the check.
 */
export const BOT_DEFENSE_REFUSED_EVENT = "authsystem:bot-defense-refused"
