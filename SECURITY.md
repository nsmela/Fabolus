# Security policy

## Reporting a vulnerability

Please do not report security vulnerabilities in public issues.

Report them privately through GitHub instead: on the repository's **Security** tab, choose
**Report a vulnerability**. Include what you found, how to reproduce it, and which version
you were using. You should get an acknowledgement within a week.

Things worth reporting include a crafted mesh or project file that crashes Fabolus or makes
it run code, a way to make it read or write files outside what the user chose, and anything
that would let a mesh or project file leak data.

## Supported versions

Only the latest release receives fixes. Fabolus is maintained by one person in their spare
time, so there is no fixed timeline for a fix, but security reports are dealt with first.

## Patient data

Never attach real patient data, including meshes exported from a treatment planning system,
to an issue, a pull request or a security report. If a problem only reproduces with a
patient's mesh, say so in the report and we will find a way to work on it that does not
involve sharing it.
