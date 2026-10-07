# Security Policy

## Supported versions

Security fixes are provided for the latest published release. Older releases may remain available for reference but are not guaranteed to receive security fixes.

## Reporting a vulnerability

Please do **not** report security vulnerabilities through public GitHub issues.

Use GitHub's private vulnerability reporting / Security Advisories for this repository when available. Include enough detail to reproduce the issue, the affected version or commit, and any relevant logs or proof of concept. Please avoid including real credentials, API keys, personal data, or other sensitive information in the report.

If private vulnerability reporting is not yet available for the repository, contact the repository maintainers privately through GitHub before disclosing the issue publicly.

## Security model

`ohmypi-vs` is a Visual Studio frontend for the oh-my-pi (OMP) runtime. OMP remains responsible for providers, credentials, models, agents, tools, approval policy, and session persistence. The extension does not store provider credentials.

The extension can expose Visual Studio capabilities to an OMP agent through `vs_*` host tools. Tool calls are subject to OMP's approval policy, and path arguments are restricted to the active workspace. Security reports involving agent control, workspace boundaries, command execution, debugger access, RPC handling, secret input handling, or process lifecycle should be treated as security-sensitive.

## Disclosure

Please allow the maintainers reasonable time to investigate and prepare a fix before making a vulnerability public. Coordinated disclosure is preferred.
