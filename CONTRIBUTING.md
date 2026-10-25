# Contributing to ThIDE

Contributions are welcome — just open a pull request or push a branch.

## Building and testing

```sh
dotnet build ThIDE.sln -m:1     # -m:1: parallel MSBuild can run out of memory
dotnet test  ThIDE.sln -m:1     # unit tests; the end-to-end UI tests skip themselves here
tests/run-ui-tests.sh           # end-to-end UI tests against the real app
```

The UI tests, their prerequisites and the `uidrive` driver are described in
[docs/dev/ui-testing.md](docs/dev/ui-testing.md). Give new interactive controls an
`AutomationProperties.AutomationId` (`<Area>.<Thing>`), and icon-only ones a localized
`AutomationProperties.Name`.

## Documentation

`docs/` exists in English and in Romanian (`docs/ro/`, the same tree file for file). When you change
a page, change its Romanian twin too — see
[About this guide → Translations](docs/user-guide/about-this-guide.md#translations).

## Licensing of contributions

> By contributing, you agree your contributions are licensed under the project's
> licenses (MPL-2.0 for the libraries, GPL-3.0-or-later for the app).

In practice this is "inbound = outbound": a change under `src/**` is contributed
under **MPL-2.0**, and a change under `ThIDE/**` under
**GPL-3.0-or-later**. See [LICENSING.md](LICENSING.md) for the full picture.
