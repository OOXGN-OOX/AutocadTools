# Development and release cycle

User preference: collect fixes into a development cycle. The current target is version 0.1.0 on the existing `build-0.1.0` branch. The user has explicitly authorized commits and pushes to this development branch. Keep `main` unchanged. Do not automatically tag, publish a release, or package the full plugin after each task.

- Use the existing development branch. Do not create replacement branches. Avoid assistant/product branding in branch names, commit metadata, documentation and user-facing messages. Preserve the repository author identity for commits.

- Keep the main README concise: product purpose, features, installation, commands and documentation links. Put test details, development procedures and validation reports in `docs/` and `tests/`.

- Use `dev.cmd Check` for structure and script syntax.
- Use `dev.cmd Test -Suite <suite>` for relevant tests. Tests compile their own small projects; this is allowed during development.
- Use `dev.cmd Test -Suite MultiCopy -AutoCAD` for the multi-copy Core Console checks. `-Suite All -AutoCAD` includes axonometry.
- Build the main Debug DLL only when needed for a real AutoCAD check or to validate integration: `dev.cmd Build -AutoCADVersion 2026`. Avoid repeated builds with unchanged sources.
- Only when the user explicitly says the cycle is finished or requests a release, run `release.cmd -Version X.Y.Z -NotesPath <notes-file>`. Select a version supplied by the user; do not silently advance it.
- Add `-Publish` only when the user authorizes publishing that finished cycle. It creates a completion commit in addition to the development history, an annotated tag and an atomic push. GitHub builds and publishes the distribution once for the version tag.
- Local release preparation builds the installer without committing or publishing. An unchanged successful installer build can reuse its verified artifacts.
- Do not spawn sub-agents unless the user explicitly asks for delegation.
