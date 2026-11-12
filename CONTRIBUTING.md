> **Retired.** This project is retired and the repository is archived; no contributions are accepted.
> See the [README](README.md) for what to use instead.

# Contributing to Arenula

## Setup

```bash
git clone https://github.com/Nyx000/Arenula-MCP.git
cd Arenula-MCP
npm run setup    # installs deps and builds api/ and docs/
```

The Editor plugin is a C# project — open it in the s&box editor, not Node.js.

## Project Structure

```
arenula-mcp/
  api/          Node.js — offline API type reference (MIT)
  docs/         Node.js — narrative docs fetcher (MIT)
  editor/       C# — s&box editor plugin (GPL-3.0)
```

## Building

```bash
npm run build              # builds both api/ and docs/
npm run build --prefix api # build just api
npm run build --prefix docs # build just docs
npm run check-contracts    # editor: tool schemas vs handlers (also runs in CI)
```

### Adding a parameter to an editor tool

Declare it in `ToolRegistry.cs` **and** read it in the handler. Doing only one half is
silent both ways: every tool sets `additionalProperties: false`, so a parameter the
handler reads but the schema omits is invisible to the model and rejected by a validating
client, while one the schema declares but the handler ignores advertises a no-op. Neither
is a compile error. `npm run check-contracts` is what catches it — run it before pushing.

## Code Style

- TypeScript with strict mode
- No semicolons (follow existing style)
- Conventional commits: `feat:`, `fix:`, `docs:`, `chore:`

## Pull Requests

- One PR per feature or fix
- Include which server(s) are affected
- Make sure `npm run build` passes
- Follow existing patterns in the codebase

## License

By contributing, you agree that your contributions will be licensed under the same license as the component you're modifying (MIT for api/docs/schemagen, GPL-3.0 for editor). See [`LICENSE`](LICENSE).
