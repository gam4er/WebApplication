# AGENTS.md — automation contract for WebApplication

This document is the **binding contract** for any automated agent, LLM-based assistant, or human
committer working in this repository. Follow it verbatim.

## 1. Absolute rules (never violate)

### 1.1 README mirror rule

`README.md` (English) and `README_RU.md` (Russian) **MUST** stay structurally identical:

- Same section order.
- Same heading levels.
- Same table columns.
- Same code-block languages.
- Same relative link targets (never localise a URL fragment).
- Only the natural-language content differs.

**Every edit to one file MUST be mirrored in the other in the same commit.** An agent that emits
a diff that touches only one of these files without touching the other is producing a broken
patch — abort and re-plan.

### 1.2 The web app is intentionally vulnerable

The Deserialization Lab in `Default.aspx` feeds arbitrary user-supplied bytes into
`BinaryFormatter`, `XmlSerializer(List<ExpandedWrapper<LosFormatter, ObjectDataProvider>>)`,
`LosFormatter` and `ObjectStateFormatter`. This is **by design** — the whole purpose is to
generate observable AMSI events for the sibling `CVEonDeserializationFinder` project. Never:

- expose the site on an interface other than `localhost`;
- add authentication and pretend it is safe;
- refactor the sinks into "safe wrappers";
- remove the "Warning" banner from the top of `Default.aspx`;
- change the default IIS binding from `http://localhost:8088` without also updating both
  READMEs _and_ `configure-iis-site.ps1` in the sibling repo.

### 1.3 Preserve the VPP lab

The VPP tab (view 1 of the `MultiView`) is a working demo of `HostingEnvironment.RegisterVirtualPathProvider`
plus its ETW detection story. Do not refactor `LabVirtualPathProvider.cs` or delete the ETW
capture artefacts — they are the offline evidence base referenced from both READMEs.

## 2. Companion-repo contract

The primary consumer of this lab is `../CVEonDeserializationFinder`. Coordinated changes:

1. Sequence: change _this_ repo first (the lab / the sinks), then
   `CVEonDeserializationFinder` (rules / detector) to reflect any new payload shape.
2. Both repos ship `README.md` + `README_RU.md` + `AGENTS.md`; the mirror rule applies to each
   pair inside its own repo.
3. Cross-links use relative paths (`../CVEonDeserializationFinder`), never absolute.

## 3. Build

Old-style MSBuild ASP.NET Web Application project on .NET Framework 4.7.2. Use MSBuild 17.x,
never `dotnet build`.

```powershell
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\amd64\MSBuild.exe'
& $msbuild WebApplication\WebApplication.csproj -t:Restore -verbosity:minimal
& $msbuild WebApplication\WebApplication.csproj -p:Configuration=Debug -verbosity:minimal
```

Output: `WebApplication\bin\WebApplication.dll`.

## 4. IIS binding

The site MUST be bound to `http://localhost:8088` only. Port `8080` on this workstation is
owned by `gontlm-proxy.exe` (a corporate NTLM proxy) and MUST NOT be reused.

The AppPool identity is `LocalSystem` for lab convenience (WMI child-process enumeration
requires elevated tokens; ApplicationPoolIdentity will not work for the `Win32_Process`
`CommandLine` field). Do not change the identity without updating the child-process detector
in `DeserializationLab.cs`.

## 5. Dangerous references

`WebApplication.csproj` intentionally pulls in:

- `System.Data.Services` + `System.Data.Services.Client` — required for `ExpandedWrapper<,>`.
- `PresentationCore` + `PresentationFramework` — required for `ObjectDataProvider`.
- `WindowsBase` — WPF transitive dependency.
- `System.Runtime.Serialization` — `BinaryFormatter`.
- `System.Management` — WMI child-process enumeration.

None of these are "safe defaults" for a web app in production. Their presence is what makes
the lab useful. Do not remove them.

## 6. Testing convention

- **No unit tests.** Integration is validated end-to-end by:
  1. Running IIS + the site.
  2. Pasting `PayloadGenerator malicious` output into the Deserialization Lab textbox.
  3. Observing the `ping.exe` child appear in the UI + a matching hit in the sibling repo's
     `hits.log`.
- Before merging into `master`, at least one benign + one malicious round-trip should be
  performed against a freshly built provider DLL.

## 7. Git conventions

- Default branch: `master`. Feature work on short-lived topic branches with PRs.
- Force-push and history rewrites on `master` are forbidden.
- Commits touching both READMEs at once are the norm; commits touching only one are almost
  always a mistake — the reviewer must reject.

## 8. Lessons learned (do not repeat)

- The `<compilation>` element in `Web.config` must list `System.Data.Services`,
  `PresentationFramework`, etc. explicitly, otherwise the inline `<script runat="server">` in
  `Default.aspx` fails to resolve types even though the csproj lists the references.
- WMI's `Win32_Process CommandLine` field is empty for children of a
  low-privilege ApplicationPoolIdentity worker. LocalSystem AppPool identity is a lab-only
  workaround; on production hardening you would instead pipe the enumeration through a helper
  service.
- `ExpandedWrapper<LosFormatter, ObjectDataProvider>` requires the closed generic to be
  visible at _serialization_ time as well as deserialization time — that is what makes the
  ToolShell temp assembly get emitted and consequently seen by AMSI.
