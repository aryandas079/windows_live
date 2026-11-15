# Contributing

Thanks for helping improve Windows Live. Contributions can include bug reports, documentation, accessibility improvements, and code changes.

## Before opening an issue

- Search existing issues for the same problem.
- Include the Windows version/build and steps to reproduce.
- Describe what happened and what you expected to happen.
- For UI problems, include a screenshot if it does not expose private information.
- Redact personal data, file paths, account names, and tokens from logs before sharing them.

## Before opening a pull request

1. Keep the change focused and explain the user-visible reason for it.
2. Update the README or other documentation when behavior, setup, or requirements change.
3. Build the project locally:

   ```powershell
   dotnet restore .\WindowsLive.csproj
   dotnet build .\WindowsLive.csproj -c Release
   ```

4. Manually check the affected interaction in Windows when practical. There is currently no automated test project in this repository; do not claim tests passed unless you ran them.
5. In the pull request description, summarize the change and list the verification you performed.

## Code and review guidance

- Follow the existing C# and XAML conventions and preserve nullable annotations.
- Prefer small, clear changes that do not block the UI thread.
- Treat Windows API and UI Automation results as optional or transient; handle unavailable devices, permissions, and app sessions gracefully.
- Avoid adding dependencies unless they are needed and maintained.
- Do not commit build output, local logs, personal configuration, or secrets. The repository's `.gitignore` excludes common generated files and logs.
- Be open to review feedback and keep discussion respectful, as described in the [Code of Conduct](CODE_OF_CONDUCT.md).

## Security issues

Please do not report a vulnerability in a public issue. Use the private reporting route in [SECURITY.md](SECURITY.md).
