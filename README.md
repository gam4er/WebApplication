# VirtualPathProvider Lab

This lab demonstrates the dynamic generation of virtual pages using ASP.NET, IIS and the CLR, and
shows how to detect that activity via IIS ETW tracing. It also hosts a second tab — a deliberately
vulnerable **Deserialization Lab** used to validate the sibling
[`../CVEonDeserializationFinder`](../CVEonDeserializationFinder) detector.

## How to run

1. Bring the site up on IIS full at `http://localhost:8088` (see the sibling repo's
   `scripts/install-iis.ps1` + `scripts/configure-iis-site.ps1`). Alternatively open the solution
   in Visual Studio and press F5 for IIS Express.
2. On the landing page (`Default.aspx`) pick tab **1. VPP Lab** and press
   **Register VPP (and activate)**.
3. Follow the link shown in the status block (`/vpp/googlecheck.aspx?token=labtoken-change-me`).
4. The virtual page issues an outbound HTTP probe to `google.com` and returns the result as
   plain text.
5. **Deactivate (AND unregister)** wipes the disk compilation cache (`Temporary ASP.NET Files`),
   resets the VPP state and cycles the AppDomain via `HttpRuntime.UnloadAppDomain()`.
6. **Flush disk compilation cache** removes the compiled files from `HttpRuntime.CodegenDir`
   without recycling — useful for diagnostics.

## Features demonstrated

### IIS / ASP.NET pipeline

| Feature                      | Where used                                                                                                                                                                                                                                                                                                         |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Default Document**         | `Web.config` → `<defaultDocument>` routes the root `/` request to `Default.aspx`.                                                                                                                                                                                                                                  |
| **Virtual paths**            | `LabVirtualPathProvider` intercepts requests to `~/googlecheck.aspx` even though no such file exists on disk. IIS hands the request to ASP.NET, which then hands it to the registered `VirtualPathProvider`.                                                                                                       |
| **Dynamic page compilation** | ASP.NET JIT-compiles the `.aspx` markup returned by `LabVirtualFile.Open()` as if it were on disk.                                                                                                                                                                                                                 |
| **Provider chaining**        | `LabVirtualPathProvider` delegates to `Previous` for every path other than its target, preserving the default behaviour.                                                                                                                                                                                           |
| **`GetCacheDependency`**     | The override returns `null` for the virtual path so that `FileChangesMonitor` does not try to watch a non-existent physical directory.                                                                                                                                                                             |
| **`MemoryBuildResultCache`** | In-memory cache of compiled pages; wiped by an AppDomain restart.                                                                                                                                                                                                                                                  |
| **`DiskBuildResultCache`**   | On full IIS, compiled `.aspx` output is persisted under `Temporary ASP.NET Files` (`HttpRuntime.CodegenDir`). This cache **survives** an AppDomain recycle and even an app-pool recycle — that is why the virtual page kept serving after deactivation. Full removal requires deleting files under that directory. |

### CLR / .NET Framework

| Feature                                              | Where used                                                                                                                                     |
| ---------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| **`HostingEnvironment.RegisterVirtualPathProvider`** | Registers the custom provider at runtime on button click — no AppDomain restart required. There is no public API to unregister.                |
| **`HttpRuntime.UnloadAppDomain`**                    | The only reliable way to “unregister” the VPP is to recycle the AppDomain, which clears `MemoryBuildResultCache` and the whole provider chain. |
| **`HttpRuntime.CodegenDir`**                         | Path to the current app's `Temporary ASP.NET Files` folder. Used to programmatically wipe the disk compilation cache.                          |
| **`volatile` fields**                                | `LabVppState.Registered` / `Active` are `volatile` so that state transitions are visible across ASP.NET thread-pool threads.                   |
| **`VirtualFile` / `VirtualPathProvider`**            | Abstract classes from `System.Web.Hosting` that let you substitute the filesystem seen by ASP.NET.                                             |
| **`HttpWebRequest`**                                 | The virtual page issues a synchronous HTTP call to `google.com/generate_204` as a connectivity probe.                                          |

### C#

| Feature                          | Where used                                                                                                                                           |
| -------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Expression-bodied members**    | `private VirtualPathProvider PrevOrNull => Previous;` — property with an expression body.                                                            |
| **Inline ASP.NET markup**        | The virtual page's `.aspx` content is assembled as a verbatim string (`@"..."`) and returned via `MemoryStream`.                                     |
| **`sealed` classes**             | `LabVirtualPathProvider` and `LabVirtualFile` are `sealed`, preventing unintended inheritance and allowing the JIT to devirtualise calls.            |
| **`static readonly` vs `const`** | The token is stored as `static readonly string`, not `const`, so it can be swapped via reflection in tests without recompiling dependent assemblies. |

## Detection via ETW (Event Tracing for Windows)

`VirtualPathProvider` usage can be detected on the IIS side via the ETW provider
**`Microsoft-Windows-IIS-Configuration`** (`{dc0b8e51-4863-407a-bc3c-1b479b2978ac}`). Trace
artifacts are shipped inside this project to illustrate the process.

### Key ETW events

| Event ID              | Level   | Channel  | Description                                                            | VPP indicator                                                                                    |
| --------------------- | ------- | -------- | ---------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| **25**                | Verbose | Analytic | Virtual path `{ConfigPath}` mapped to physical path `{PhysicalPath}`.  | Physical path points at a file that **does not exist** on disk (`googlecheck.aspx`).             |
| **47**                | Verbose | Analytic | Configuration folder `{ConfigPath}` mapped to directory `{Directory}`. | IIS looks for a `web.config` next to the non-existent file.                                      |
| **28**                | Info    | Debug    | Impersonation of access token `{ImpersonationTokenHandle}`.            | Accompanies the virtual-path access — records the security context.                              |
| **13**                | Verbose | Analytic | Parsing of configuration file `{PhysicalPath}`.                        | Shows the config chain IIS walked to reach the virtual path.                                     |
| **17 / 18 / 19 / 21** | Verbose | Debug    | File change monitors: create, wait, notify, delete.                    | IIS creates a `FileChangeNotificationMonitor` for directories, including paths of virtual files. |
| **9**                 | Verbose | Debug    | Configuration cache flush for `{ConfigPath}` and children.             | Fires on `HttpRuntime.UnloadAppDomain()` — indicator of VPP deactivation.                        |

### Sample detection (from `iis_etw_events.ndjson`)

When the virtual page `googlecheck.aspx` is hit, `w3wp` (PID 53664) emits a characteristic pair:

1. **EventID(25)** — virtual-to-physical mapping:
   ```
   ConfigPath: MACHINE/WEBROOT/APPHOST/DEFAULT WEB SITE/VppLab/googlecheck.aspx
   PhysicalPath: C:\inetpub\VppLab\googlecheck.aspx
   ```
2. **EventID(47)** — `web.config` lookup for that path:
   ```
   ConfigPath: MACHINE/WEBROOT/APPHOST/DEFAULT WEB SITE/VppLab/googlecheck.aspx
   Directory: \\?\C:\inetpub\VppLab\googlecheck.aspx
   ```

If the `PhysicalPath` from EventID(25) **does not correspond to a real file on disk**, that is a
direct signal of a custom `VirtualPathProvider` at work.

### How to capture the trace

```powershell
# Start ETW session
logman create trace IIS-VPP-Detect -p "Microsoft-Windows-IIS-Configuration" 0xFFFFFFFF 0xFF -o iis_config.etl -ets

# ... reproduce a call to the virtual page ...

# Stop
logman stop IIS-VPP-Detect -ets
```

For `.etl` → NDJSON conversion, this project used
[PerfView](https://github.com/microsoft/perfview) / TraceEvent.

### Trace files in the project

| File                                      | Description                                                                                                                                                              |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `iis_etw_events.ndjson`                   | Captured ETW events in NDJSON format (one JSON object per line). Contains events from `MSNT_SystemTrace` and `Microsoft-Windows-IIS-Configuration`.                      |
| `Microsoft-Windows-IIS-Configuration.csv` | Reference table of every Event ID exposed by the provider: ID, level, channel, message template, fields.                                                                 |
| `Microsoft-Windows-IIS-Configuration.xml` | ETW manifest of the provider in `instrumentationManifest` form. Describes keywords (`Read`/`Write`), value maps (`ErrorType`, `ChangeListenerType`) and event templates. |

## Project layout

```
WebApplication/
├── Default.aspx                                — landing page: 2 tabs (VPP + Deserialization)
├── LabVirtualPathProvider.cs                   — VPP, VirtualFile and shared state (LabVppState)
├── DeserializationLab.cs                       — 4 sinks, ProcessScanner, embedded benign payloads
├── Global.asax / Global.asax.cs                — application entry point
├── Web.config                                  — IIS and ASP.NET configuration
├── iis_etw_events.ndjson                       — captured ETW events (NDJSON)
├── Microsoft-Windows-IIS-Configuration.csv     — IIS-Configuration provider Event ID reference
├── Microsoft-Windows-IIS-Configuration.xml     — provider ETW manifest
├── README.md                                   — this file
├── README_RU.md                                — Russian mirror
└── AGENTS.md                                   — contract for automated agents
```

## Deserialization Lab (tab 2)

The second tab of `Default.aspx` is a deliberately vulnerable playground used to validate the
[CVEonDeserializationFinder](../CVEonDeserializationFinder) detector. It is served **only on
`http://localhost:8088`** (port `8080` on this workstation is owned by `gontlm-proxy.exe`).

**Warning.** The app intentionally accepts arbitrary payloads and feeds them into dangerous
deserializers. Do not deploy it anywhere reachable from an untrusted network.

### How to bring the site up (elevated)

From the sibling `CVEonDeserializationFinder` repo:

```powershell
scripts\install-iis.ps1            # enable full IIS with ASP.NET 4.5
scripts\configure-iis-site.ps1     # create AppPool + site CVEDeserializationLab on :8088
```

`configure-iis-site.ps1` provisions AppPool `CVEDeserializationLab` (.NET 4.0 Integrated,
LocalSystem — for WMI-query convenience) and points the physical path at
`E:\Documents\GitHub\WebApplication\WebApplication`.

### What the tab looks like

1. **Sink chooser** — a `RadioButtonList` with four handlers:
   - `BinaryFormatter` — `System.Runtime.Serialization.Formatters.Binary`
   - `XmlSerializer` — ToolShell / CVE-2025-53770 shape:
     `List<ExpandedWrapper<LosFormatter, ObjectDataProvider>>`
   - `LosFormatter` — `System.Web.UI.LosFormatter`
   - `ObjectStateFormatter` — the default ASP.NET ViewState formatter
2. **Base64 payload** — `<asp:TextBox TextMode="MultiLine">`. Paste the output of
   `ysoserial.exe -o base64` or `PayloadGenerator benign --sink <..>`.
3. **Load benign example** button — populates the textbox with a safe base64 hard-coded into
   `DeserializationLab.cs`.
4. **Deserialize** button — decodes the base64, feeds the bytes into the selected sink, catches
   any exception, and records the result in a rolling **Recent attempts** window (rendered as a
   table on the same page).
5. **Child-process table** (`ManagementObjectSearcher` running `Win32_Process WHERE
ParentProcessId=<my pid>`) — refreshed on button press. Any row whose name is in the built-in
   suspicious-command allow-list (`ping.exe`, `cmd.exe`, `powershell.exe`, `mshta.exe`,
   `certutil.exe`, ...) is highlighted red and the banner reads **SUSPICIOUS CHILD PROCESS
   DETECTED**. That is the canonical proof of successful exploitation.

### Sink → gadget mappings for validation

From the sibling `CVEonDeserializationFinder` repo:

```powershell
$pg = 'src\CVEonDeserializationFinder.PayloadGenerator\bin\Debug\CVEonDeserializationFinder.PayloadGenerator.exe'

# Benign (same as the "Load benign example" button in the WebApp).
& $pg demo

# Malicious — authorised lab environments only.
& $pg malicious --sink bf  --cmd "ping ya.ru -n 10"    # BinaryFormatter + TypeConfuseDelegate
& $pg malicious --sink xml --cmd "ping ya.ru -n 10"    # XmlSerializer/Xaml + ObjectDataProvider
& $pg malicious --sink los --cmd "ping ya.ru -n 10"    # LosFormatter + ActivitySurrogateSelector
& $pg malicious --sink osf --cmd "ping ya.ru -n 10"    # ObjectStateFormatter + ObjectDataProvider
```

### Expected end-to-end outcome

1. The AMSI provider is registered (`scripts\install-elevated.ps1`).
2. Paste a payload, press **Deserialize**.
3. **`%ProgramData%\CVEonDeserializationFinder\hits.log`** gets a `"level":"hit"` line with the
   matched rule (e.g. `GADGET-ExpandedWrapper-LosFormatter-ObjectDataProvider` for the `xml`
   sink).
4. **`%ProgramData%\CVEonDeserializationFinder\dumps\<yyyyMMdd-HHmmss.fff>_<pid>_w3wp\`** gets a
   fresh folder containing the raw assembly bytes and a JSON sidecar.
5. **The child-process table** in the web UI shows a fresh `ping.exe` whose parent PID equals
   the current `w3wp` — highlighted red.
6. The **Recent attempts** log records an "exception" (the payload usually throws _after_ the
   gadget code runs — that is expected).

### `WebApplication.csproj` dependencies

On top of the stock references, the lab adds:

- `System.Runtime.Serialization` — `BinaryFormatter`.
- `System.Data.Services` + `System.Data.Services.Client` — `ExpandedWrapper<,>`.
- `WindowsBase` + `PresentationCore` + `PresentationFramework` — `ObjectDataProvider`.
- `System.Management` — WMI query for child processes.

The corresponding assemblies are also listed in `Web.config` under
`<system.web><compilation><assemblies>` so the inline `<script runat="server">` inside
`Default.aspx` can resolve them.

## Related documents

- [`README_RU.md`](README_RU.md) — Russian version of this document; structure-locked to it.
- [`AGENTS.md`](AGENTS.md) — automated-agent contract for this repository.
- [`../CVEonDeserializationFinder/README.md`](../CVEonDeserializationFinder/README.md) —
  the detector (companion repo).
