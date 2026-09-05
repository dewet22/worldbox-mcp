# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| 0.x     | ✅ (during pre-1.0 development) |
| 1.x     | ✅ (latest minor) |
| < 1.0   | ❌ once 1.0 is released |

## Reporting a vulnerability

**Please do not open a public GitHub issue for security vulnerabilities.**

Report privately through one of:

1. **GitHub Security Advisories** (preferred):
   <https://github.com/fullya99/worldbox-mcp/security/advisories/new>
2. **Email**: replace with your maintainer email before v1.0 (`<security@…>`).

Include:
- A clear description of the issue and its impact.
- Steps to reproduce (proof of concept welcome).
- Affected version(s) of `worldbox-mcp` and the mod, plus the WorldBox version.
- Any suggested mitigation.

### What to expect

| Stage | Target |
|---|---|
| Acknowledgement | within 72 hours |
| Initial assessment | within 7 days |
| Coordinated disclosure window | up to 90 days |
| Public credit (if desired) | in CHANGELOG + advisory |

## Scope

In scope:
- The MCP server (`worldbox-mcp` Python package).
- The BepInEx mod (`WorldBoxBridge`).
- Build/release pipeline (GitHub Actions).

Out of scope:
- Vulnerabilities in WorldBox itself (report to the game's developer).
- Vulnerabilities in BepInEx (report to <https://github.com/BepInEx/BepInEx>).
- Vulnerabilities in MCP clients (Claude Code, Cursor, etc.), report upstream.

## Threat model

`worldbox-mcp` is designed for **local-only** use:

- The mod's HTTP listener binds **only** to `127.0.0.1`. Binding to `0.0.0.0` is refused at startup.
- Authentication uses a bearer credential, accepted via either `Authorization: Bearer <token>` (the preferred header, v0.3+) or the legacy `X-WB-Token: <token>` (v0.1 / v0.2 single-tenant clients). Constant-time comparison on the wire.
- In legacy mode, the credential is a per-install random token stored in `BepInEx/config/WorldBoxBridge.cfg`. In multi-agent mode (v0.3+), each agent has its own bearer token defined in `BepInEx/config/WorldBoxBridge.agents.json`, alongside that agent's role, permissions, and optional kingdom claim.
- Tokens are never logged, transmitted over the network beyond loopback, or committed to git. The Python MCP server reads the same config files to obtain its token.

If your threat model includes a hostile process running as your user on the same machine, treat tokens as shared secrets and protect file ACLs accordingly. In multi-agent mode, a compromised agent token only grants the permissions associated with that agent's role, a stolen FactionPlayer token can't `generate_world` (god-only) or screenshot the whole map under fog-of-war.
