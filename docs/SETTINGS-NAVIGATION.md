# Settings navigation

The UI separates initial server setup, feature configuration and optional website integrations. Existing settings keys and stored values are unchanged; no migration or reset is required.

## Initial setup

**Setup / Einrichtung** has four subtabs:

- Getting started: three setup steps with direct navigation to the relevant pages.
- Server access: RCON credentials and ggCON HTTP API access. The HTTP password is now masked.
- File access (SFTP): credentials and log-directory configuration; fallback filename patterns are collapsed by default.
- Automatic startup: the services to start when Red Raven launches. Configure and test their prerequisites first.

HTTP uses the configured base URL, or falls back to the server host and HTTP port. A blank HTTP password falls back to the RCON password. SFTP is still needed for database/file features; moving the controls does not replace SFTP with HTTP.

## Feature configuration

- Discord: Bot credentials, Server status, Chat & logs. The main bot posts all messages. The optional second bot only supplies player-count presence.
- Chat commands: Commands, Welcome messages, Votes. Votes are no longer mixed into the command-list header or global settings. The chat monitor must run for votes and built-in commands.
- Challenges: keep database scans, channels, language and local LAN challenge administration in the existing challenge Settings subtab.
- Setting randomizer: dice sets are separate from path, schedule, Discord-status publication and settings validation.
- Economy: Overview is separate from calculation/upload Settings.
- Vehicle insurance: insurance pricing and inactivity warnings stay in its Settings subtab.
- Script zone: central timing, startup and engine polling are grouped in the collapsed scheduler section.

Hover help is provided in German and English for settings fields, including formats, units, fallback behavior and dependencies.

## Optional integrations

**Advanced / Erweitert** contains:

- Website connections (optional): challenge progress, economy market data, insurance and order-worker endpoints/tokens. Shop configuration is only shown here, not in regular Setup or feature settings.
- Optional LMNT directory: opt-in consent, published fields and endpoint configuration.
- Updates: startup update checks, custom manifest URL and manual update/download actions.

These are optional and not prerequisites for normal operation. Existing enabled integrations continue to work; moving them does not silently disable them. Website endpoints must be deployed separately and use the same secret tokens as the tool.

## Verification

Build the WPF project normally. The additional offline layout tests verify menu separation, settings-property paths, translated help, masked HTTP password, stable named navigation tabs and preservation of the dice-set editor:

```powershell
dotnet run --project tests/SettingsLayout/SettingsLayout.Tests.csproj -- .
```

Optional `--render` creates layout previews in `.codex_tmp/settings-preview`. These use fresh example settings, not `MainViewModel`, user accounts or running services. They do not connect to SCUM, Discord or a website.
