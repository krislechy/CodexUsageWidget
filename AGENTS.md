# Project instructions

- Keep project documentation in English.
- Build and verify locally unless the user requests another action.
- Do not perform GitHub operations automatically. Remote reads, pushes, releases, pull requests, issues and repository changes require an explicit user command.
- A request to implement a feature or produce a local build does not authorize GitHub activity.
- Do not test or capture every interface language. Routine verification uses English only; locale-wide checks require an explicit user request.
- Release both Portable and Lite variants. Use `scripts/release.ps1` for routine verification, English screenshots, packaging, checksums, Git commit/tag/push and GitHub release creation. GitHub steps run only with an explicitly authorized `-Publish` command.
