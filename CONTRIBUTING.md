# Contributing

Thanks for considering a contribution.

## Before opening an issue

- Search existing issues first.
- Include Windows version, Node.js version, .NET version, and MCP Xellen version.
- Describe expected and actual behavior.
- Include sanitized logs only. Remove tokens, hostnames, usernames, private paths, and other identifying details.

## Pull requests

1. Fork the repository.
2. Create a focused branch.
3. Keep changes small and explain why they are needed.
4. Run a Release build:
   ```powershell
   dotnet publish .\src\MCP-Xellen\MCP-Xellen.csproj -c Release -r win-x64 --self-contained false
   ```
5. Verify that no secrets, local configuration, runtime folders, or generated binaries are included.
6. Describe manual test steps and any limitations.

Do not submit tokens, private Funnel URLs, local logs, or a copy of a user's Desktop Commander data.
