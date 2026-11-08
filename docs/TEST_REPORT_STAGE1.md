# CIPHER Stage 1 test report

## Verified Windows CI run

- Date: 2026-11-08
- Commit tested: `54965bfef94fa255220c40e9e06e1c217efe0a18`
- GitHub Actions run: https://github.com/unknown007dotcom/CIPHER/actions/runs/42480109478
- Runner: GitHub-hosted `windows-latest`
- Result: **Success**

The workflow completed all of these steps:

1. Restore the .NET 10 solution.
2. Build the solution in Release configuration.
3. Run the automated test suite.
4. Validate `setup-dev.bat`.
5. Validate `build.bat Release`.
6. Validate `test.bat Release`.
7. Validate that `package.bat Release` refuses to create an installer before the Stage 10
   offline dependencies and WiX project are ready.
8. Publish and upload a framework-dependent Windows x64 validation build.
9. Upload the test-result artifact.

## Automated test coverage

The test project contains 22 executed test cases:

- 10 deterministic parser cases;
- 5 action-policy cases;
- 4 task-engine cases;
- 3 wake-phrase cases.

Covered behavior includes application/text/key/file command parsing, unknown-command handling,
protected-path blocking, arbitrary-shell blocking, confirmation requirements, denied-confirmation
cancellation, and verified task completion.

## Important limitations

This CI result proves that the solution restores, compiles, publishes, and passes its automated
core tests on Windows. It does **not** prove interactive desktop behavior.

The following still require a real interactive Windows 10/11 x64 session and have not been
reported as passed:

- launching and visually inspecting the WPF Dynamic Island;
- the Stage 3 Notepad launch/type/switch/return/read-back milestone;
- voice recognition or speech output;
- browser automation;
- offline MSI installation, repair, and uninstallation;
- performance and resource-usage benchmarks.

The uploaded `CIPHER-validation-build` artifact is a framework-dependent validation publish. It
is not `CIPHER.msi`, not a self-contained release, and must not be distributed as the final
offline installer.
