# Changelog

All notable changes to DeskSnapshot will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project intends to follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Named display layout profiles with stable monitor-topology matching, preview, safe restore suggestions, and remembered manual monitor mappings.
- Manual, scheduled, event-triggered, tray, and pre-restore backups.
- Timeline-based backup history with parent/child safety backups and batch deletion.
- Large visual layout preview with direct restore action.
- Current-desktop comparison with moved, added, missing, and unchanged icon visualization.
- Display-environment difference detection, duplicate-name warnings, and a changes-only filter.
- MSTest coverage for comparison, backup relationships, retention, and JSON storage recovery.
- Monitor model, device identity, and optional monitor-aware restore.
- Configurable automatic-backup retention.
- Startup registration and notification-area background mode.
- Simplified Chinese, English, and system-language selection.
- Portable and MSIX packaging scripts with GitHub Actions builds.

### Changed

- Refined responsive WinUI 3 navigation, settings, About page, and selection states.
- Improved DPI-aware virtual desktop capture and preview sizing.

### Fixed

- Prevented restore operations from being recorded again as layout-change backups.
- Hardened unpackaged startup, resource loading, JSON writes, and Explorer messaging.

[Unreleased]: https://github.com/FeranyDev/DeskSnapshot/commits/main
