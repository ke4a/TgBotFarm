# Feature Specification: Admin UI Language Selection

**Feature Branch**: `[master]`

**Created**: 2026-11-11

**Status**: Draft

**Input**: User description: "Let operators switch the admin UI interface language. Keep admin UI translations in per-language files using the same file format as the bot translation files, served through the standard localization mechanism the UI framework provides. Store the chosen language in a browser cookie. On the first request, use the browser's language if a translation exists for it, then store it in the cookie. If there is no translation for the browser's language, use English."

**Context**: The admin UI (sign-in and first-run setup pages, the main dashboard, bot dashboard pages, and their dialogs) is English-only today. Bots already ship per-language translation files for the messages they send in Telegram, and that system picks a language per chat. The admin UI's language instead belongs to the person using the browser and applies to the whole interface, including date and number formats. This feature adds a separate set of admin UI translation files in the same file format, a language picker, and a browser-level language preference. The supported languages are the ones the bots already use: English (en-US), Russian (ru-RU) and Ukrainian (uk-UA).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Operator switches the interface language (Priority: P1)

An operator signed in to the admin UI opens the language picker in the top bar, chooses Russian, and the interface reloads in Russian: navigation, page titles, buttons, cards, dialogs, empty-state texts, notifications and validation messages. Dates and numbers use Russian formatting. The next time the operator opens the admin UI in the same browser, it is still in Russian.

**Why this priority**: This is the core capability. Without a way to choose, nothing else in the feature is reachable.

**Independent Test**: Sign in, switch to each supported language in turn, and confirm that every page and dialog shows text in that language and that the choice survives closing and reopening the browser.

**Acceptance Scenarios**:

1. **Given** the admin UI is shown in English, **When** the operator selects Russian in the language picker, **Then** the current page reloads in Russian and the operator stays on the same page.
2. **Given** the operator selected Ukrainian, **When** they close the browser and open the admin UI again, **Then** it is shown in Ukrainian without any further action.
3. **Given** the interface is in Russian, **When** the operator opens any dashboard page or dialog (send message, log viewer, backups, chats, webhook, stats), **Then** all interface text, including the UI component library's built-in texts (paging, date pickers, "no records" messages), is in Russian.
4. **Given** the interface is in Russian, **When** the operator views dates and numbers (log timestamps, backup dates, statistics), **Then** they use Russian formatting.
5. **Given** the language picker is open, **When** the operator looks at the options, **Then** each language is listed by its own name (English, Русский, Українська) and the current language is marked.

---

### User Story 2 - First visit uses the browser's language (Priority: P2)

A new operator opens the admin UI for the first time in a browser set to Ukrainian. The sign-in page is already in Ukrainian, and that language is remembered from then on. An operator whose browser prefers a language with no translation, such as German, sees the interface in English.

**Why this priority**: It removes the need to find the picker for most operators, but the feature still works without it.

**Independent Test**: With no stored preference, open the admin UI from browsers set to Ukrainian, to a regional variant of a supported language, and to an unsupported language, and confirm the shown language and that it is stored for the next visit.

**Acceptance Scenarios**:

1. **Given** no stored language preference and a browser preferring Ukrainian, **When** the operator opens the sign-in page, **Then** it is shown in Ukrainian and the preference is stored.
2. **Given** no stored preference and a browser preferring a regional variant with no exact translation (for example Russian as used in Belarus), **When** the operator opens the admin UI, **Then** it is shown in the supported translation for the same base language (Russian).
3. **Given** no stored preference and a browser listing German first and Russian second, **When** the operator opens the admin UI, **Then** it is shown in Russian.
4. **Given** no stored preference and a browser preferring only unsupported languages, **When** the operator opens the admin UI, **Then** it is shown in English and English is stored as the preference.
5. **Given** a stored preference of English, **When** the operator later changes the browser language to Russian, **Then** the admin UI stays in English until the operator changes it with the picker.

---

### User Story 3 - Sign-in and setup pages are translated and offer the picker (Priority: P3)

An operator who is not signed in, or who is setting up the first account, sees the sign-in or setup page in their language and can switch the language there before signing in.

**Why this priority**: These pages are seen rarely, but without translation they would be the only English pages for operators who chose another language.

**Independent Test**: Signed out, switch language on the sign-in page, then sign in, and confirm both the sign-in page and the dashboard use the chosen language.

**Acceptance Scenarios**:

1. **Given** the operator is signed out with Russian stored, **When** they open the sign-in page, **Then** labels, buttons and error messages (such as invalid credentials) are in Russian.
2. **Given** the operator is on the sign-in page, **When** they choose Ukrainian in the picker, **Then** the page reloads in Ukrainian and the choice applies to the dashboard after sign-in.
3. **Given** the first-run setup page is shown, **When** the operator views it, **Then** its text and validation messages are in the stored or detected language.

---

### User Story 4 - Bot dashboard pages provide their own translations (Priority: P4)

A bot that adds its own page to the admin UI ships admin translations for that page alongside the bot, in the same file format. When the operator chooses a language, the bot's page uses the bot's translations for it. A bot that ships no translation for that language shows its page in English while the rest of the interface stays in the chosen language.

**Why this priority**: Bot pages are part of the admin UI, but each bot owns its texts, so this builds on the shared mechanism from Stories 1–3.

**Independent Test**: With TestBot providing translations and a bot without a Ukrainian translation, switch to Ukrainian and confirm that the TestBot page is in Ukrainian, the other bot's page is in English, and the shared parts of both pages are in Ukrainian.

**Acceptance Scenarios**:

1. **Given** TestBot ships admin translations for all supported languages, **When** the operator views the TestBot page in Russian, **Then** the bot-specific texts are in Russian.
2. **Given** a bot ships only English admin translations, **When** the operator views that bot's page in Russian, **Then** the bot-specific texts are in English and the shared navigation and cards are in Russian.
3. **Given** a new bot is created from the bot template, **When** its developer looks at the template, **Then** it already contains admin translation files to extend.

---

### Edge Cases

- A stored preference names a language that is not supported (unknown value, malformed value, or a translation that has since been removed): it is treated as absent; the language is detected again from the browser as on a first visit and the stored value is replaced.
- A translation file lacks a key that English has: that text is shown in English, never as a blank or as the raw key.
- A key is missing from the English file as well: the key name is shown, so the gap is visible during development rather than hidden.
- The browser sends no language preference at all: English is used and stored.
- Cookies are blocked in the browser: the interface is still usable and shown in the language detected from the browser on each request; the picker has no lasting effect, because the choice cannot be stored.
- Switching the language reloads the page: unsent text in an open dialog is lost. The picker is in the top bar, outside dialogs, so it is not reachable while a dialog is open.
- Content that is not interface text is not translated: bot names, chat titles, user names, message contents, log lines, and bot translation values shown or edited in the UI stay as they are.
- Two operators on different browsers use different languages at the same time: each sees their own choice, and neither affects the other or any bot.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The admin UI MUST support English (en-US), Russian (ru-RU) and Ukrainian (uk-UA) as interface languages, with English as the default.
- **FR-002**: The admin UI MUST provide a language picker in the top bar of the dashboard and on the sign-in and setup pages, listing each supported language by its own name and marking the current one.
- **FR-003**: Choosing a language MUST store it in a browser cookie and reload the current page in that language, keeping the operator on the same page.
- **FR-004**: The stored language MUST persist across browser sessions for one year from the operator's last visit. Its expiry MUST be renewed whenever the operator opens the admin UI and whenever they choose a language, so it expires only after a year without visits.
- **FR-005**: When no valid stored language exists, the system MUST pick the first language in the browser's preference list that has a translation, matching first on the exact language and region and then on the base language alone, and MUST store the result in the cookie.
- **FR-006**: When none of the browser's preferred languages has a translation, or the browser states no preference, the system MUST use English and store it in the cookie.
- **FR-007**: Once a language is stored, it MUST take precedence over the browser's language preference.
- **FR-008**: A stored value that does not name a supported language MUST be ignored and replaced as described in FR-005 and FR-006.
- **FR-009**: All interface text in the admin UI MUST come from per-language translation files: page and card titles, navigation, buttons, labels, tooltips, dialog texts, empty states, confirmations, notifications, and validation and error messages on the dashboard, sign-in and setup pages.
- **FR-010**: Admin UI translation files MUST use the same per-language file format as the bot translation files (one file per language holding key–value pairs) and MUST be kept separate from the bot translation files, so that neither system loads the other's files.
- **FR-011**: The UI component library's built-in texts (paging, date and time pickers, table and list empty states) MUST follow the chosen language.
- **FR-012**: Dates, times and numbers shown in the admin UI MUST use the chosen language's formatting.
- **FR-013**: The page's declared document language MUST match the chosen language.
- **FR-014**: When a translation lacks a key, the English text for that key MUST be shown.
- **FR-015**: Each bot MUST be able to ship admin translations for its own dashboard page alongside the bot, in the same file format; the bot template MUST include such files for every supported language.
- **FR-016**: Automated tests MUST verify that, for the shared admin UI and for each bot that ships admin translations, every language file defines the same keys with non-empty values.
- **FR-017**: The language preference and language detection MUST affect only the admin UI for that browser. Bot webhook requests, dashboard API requests and health endpoints MUST run in the server's default culture, as they do without this feature, and the feature MUST NOT change the languages bots use in Telegram chats.
- **FR-018**: Bot translation values, bot names, chat and user data, message contents and log lines MUST be shown as stored, without translation.
- **FR-019**: The language cookie MUST be readable by the sign-in and setup pages before authentication and MUST NOT contain anything other than the language code.
- **FR-020**: The language cookie MUST be set by the server, not by page scripts, so browser limits on script-set cookie lifetimes do not apply. It MUST apply to the whole admin UI, MUST NOT be readable by page scripts, MUST NOT be sent with cross-site requests other than top-level navigation, and MUST be sent only over secure connections when the admin UI is served over HTTPS.

### Key Entities

- **Interface language**: A supported language for the admin UI, identified by its language-and-region code (for example uk-UA), with a display name in that language.
- **Language preference**: The language stored in the operator's browser. It holds only the language code, expires one year after the operator's last visit, and is set either by detection on first visit or by the picker.
- **Admin translation set**: For each supported language, the key–value pairs for the shared admin UI. Each bot that has a dashboard page has its own admin translation set alongside the bot, separate from the bot's Telegram translations.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In each supported language, 100% of the interface text on every admin page and dialog is shown in that language, with no English left except where a bot ships no translation for its own page.
- **SC-002**: An operator can change the interface language in at most two clicks from any dashboard page.
- **SC-003**: An operator whose browser prefers a supported language sees the admin UI in that language on the first visit with no action.
- **SC-004**: After choosing a language once, an operator sees it on 100% of later visits from the same browser, as long as each visit comes within a year of the previous one, until they change it or clear their cookies.
- **SC-005**: Adding a missing key to one language file without adding it to the others causes the automated test suite to fail.
- **SC-006**: No interface text is ever shown blank or as a raw key in a supported language when the English file has that key.

## Assumptions

- The preference is per browser, not per admin account. An operator who uses several browsers or devices chooses the language in each one.
- The language cookie is a functional cookie needed to show the interface and does not require a consent prompt.
- Reloading the page to apply a new language is acceptable; the language does not change live inside an open page.
- Supported languages are fixed by the shipped translation files. Adding a language means adding files for the shared admin UI and, optionally, for each bot; it needs no operator configuration.
- Health check pages and the third-party health dashboard are out of scope and stay in English.
- Server-side log messages and error texts written to logs stay in English.
- Shared admin translations are delivered with the host application, and each bot's admin translations are delivered with that bot, in the same way bot translation files are delivered today.
