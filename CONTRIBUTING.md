# Contributing

Thanks for contributing to oh-my-pi for Visual Studio.

## Development setup

See the development instructions in the README for the required Visual Studio workload, .NET SDK and Node.js setup.

The project targets Visual Studio 2022 17.14+ and Visual Studio 2026. The repository uses the .NET SDK pinned in `global.json`.

## Workflow

- Create a feature branch from `develop`.
- Keep changes focused and avoid unrelated refactoring.
- Add or update tests for behavioral changes.
- Run the relevant test projects before opening a pull request.
- Open a pull request against `develop` and describe the user-visible impact and important implementation details.

## Testing

Tests that exercise a real OMP runtime can require a local OMP installation and may incur provider/model costs. Do not add tests that make real model requests unless they are explicitly designed for that purpose.

## Security

Do not commit credentials, API keys, tokens, personal data, or other secrets. See [SECURITY.md](SECURITY.md) for reporting security vulnerabilities.

## License

By contributing, you agree that your contributions are provided under the repository's [MIT License](LICENSE).
