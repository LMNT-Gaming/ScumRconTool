# GitHub updates (Windows x64)

Starting with 1.3.2, **Advanced → Updates → Install update & restart** downloads the latest stable release from `LMNT-Gaming/ScumRconTool`. Checking at startup never installs an update without consent. Existing installations using the old official `https://lmnt-gaming.net/rrrt/latest.json` feed automatically use GitHub; private feeds remain configurable.

The updater checks the GitHub asset SHA-256 digest, download size and assembly version before installation. A separate hidden PowerShell helper waits up to two minutes for Red Raven to exit; it never kills the process. Replaced program files are backed up before copying. On a copy/start failure it attempts rollback and displays the backup/log location. Installation must be in a writable folder; the updater does not request administrator elevation.

Settings, credentials, scripts, lootpacks, databases and logs are not replaced. The entire `Data` folder is preserved, including custom scripts and compatibility files. AppData settings/state are untouched. Updates replace binaries, runtime dependencies, supplied icons/map and the shipped item catalogue. Obsolete binaries are not deleted. Backups and staging packages remain under `%TEMP%\RedRaven-Update-*` (`result.log`, `backup`) for recovery; no automatic cleanup is performed.

## Publishing an update

1. Set all four version fields in `ScumRconTool.Wpf.csproj` to the same value (e.g. `1.3.2`), commit and push.
2. Create and push the matching tag, e.g. `v1.3.2`.
3. The `Build Windows release` GitHub workflow produces a self-contained Windows x64 ZIP and creates a **draft** release. Review its notes/package, then publish the release in GitHub.
4. Use exactly `RedRavenRconTool-1.3.2-win-x64.zip` as the asset name (substitute version). GitHub must provide the asset `digest` in `sha256:<64 hex characters>` format. Drafts and prereleases are not installed.

Commits alone do not update users. Version 1.3.1 and older require one last manual installation of 1.3.2; thereafter this updater is available. The workflow/release is not published merely by editing the local project.

Custom latest.json feeds need `version`, HTTPS `downloadUrl` and `sha256`; optional `size` and `patchNotesUrl` are supported. Only configure sources you trust. Checksums verify the downloaded bytes, not the trustworthiness of a custom publisher.

## Offline checks

`dotnet run --project tests/Updates/Updates.Tests.csproj`

Tests validate release parsing, checksum/source requirements, archive path traversal protections, protected data paths, helper syntax and actual file replacement/backups in isolated test directories. They never start Red Raven or connect to a live server.
