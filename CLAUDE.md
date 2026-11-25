# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this is

`dxfWorker` is a small Windows Forms desktop tool, recovered by decompiling an original `dxfWorker.dll` (.NET 6) and then extended. It prepares DXF files for laser cutting (CypCut): it copies a template folder of DXF parts once per order name, then stamps the order name as a blue text label onto each part drawing.

All user-facing text (UI labels, error messages, README) is in **Russian**. Keep new UI strings and messages in Russian to match.

## Build and run

- Target: `net10.0-windows`, WinForms, `Nullable` and `ImplicitUsings` enabled (`dxfWorker.csproj`).
- Only dependency: `netDxf`, referenced as a local DLL from `lib/netDxf.dll` (not NuGet).

```powershell
dotnet build
```

```powershell
dotnet run
```

There are no tests and no CI. Check changes by building and running the app against a scratch folder tree (see layout below). Never point it at real production data.

## Source layout

Everything is in three files at the root, in namespace `dxfWorker`:

- `Program.cs`: entry point (`[STAThread]`, `Application.Run(new Form1())`).
- `Form1.cs`: the whole UI and the orchestration. Controls are built in code: field initializers and `MakeButton`. There is **no** `Form1.Designer.cs` or `.resx`, so edit layout directly in code with absolute `Location`/`Size`.
- `markDXFBase.cs`: the DXF processing (`markDXFBase.Convert`).

## Processing flow

Folder conventions, given a chosen root and a part code (`KL`, `UN`, `U`, `T`, `PL`, or a custom code typed into the textbox):

- Source: `<root>\<code>\<code> 0\` (top-level `*.dxf`, plus any subfolders)
- Per order name: `<root>\<code>\<name>\` (a full recursive copy of the source)
- Output: `<root>\<code>\<name>\CypCut_<name>\<stem>_(<name>).dxf`

`Form1.StartProcessAsync`:
1. Validates the root, the code and the names. Names are split on `;`, trimmed, de-duplicated case-insensitively, and checked by `IsSimpleFolderName`.
2. Refuses to run if **any** destination folder already exists. Never overwrite anything (`File.Copy(..., overwrite: false)`).
3. On `Task.Run`, for each name: `CopyFolder`, then `markDXFBase.Convert(new[] { destination }, callback)`, then checks that the converted DXF count equals the source's top-level DXF count.
4. Optionally deletes the source's top-level DXFs, only after every name succeeded (checkbox, off by default).
5. Reports progress through `Progress<ProgressUpdate>`. Total work is `names × (all copied files + top-level DXFs)`. `SetBusy` disables `inputControls` while running.

`markDXFBase.Convert(string[] args, Action<string>? onFileProcessed)`:
- `args[0]` is the folder and the optional `args[1]` is the DXF version (`2007`/`2010`/`2013`/`2018`; defaults to AutoCAD 2004). This args-array signature comes from the original CLI-style code.
- The label text is the folder name, taken from the parent directory of the first DXF file.
- It computes a bounding box from `Lines` and `Arcs` only. Polylines, splines, circles and so on are ignored. Files with no lines or arcs are skipped without being saved, which then makes the count check in `Form1` fail.
- Text: layer `TextOrder`, `AciColor.Blue`, style `iso.shx`, height 12. It is rotated 90° if the part is taller than it is wide, and placed near the centroid with heuristic offsets.
- `onFileProcessed` must be called exactly once per input file, including skipped files, because the progress maths depends on it.

## Licensing (branch `locked-version`)

- `License.cs` checks signed activation codes (ECDSA P-256). The public key is embedded in the app. The private key lives only in `tools/KeyGen/private.key`, which is git-ignored and must never be committed or given to customers.
- `Program.cs` shows `ActivationForm` until the license is valid, then re-checks every 10 s while running.
- The app stores the code in `%APPDATA%\dxfWorker\license.dat`. Clock rollback is detected through `state.dat`.
- Make codes with `dotnet run --project tools/KeyGen -- gen "Customer" 1y [MACHINE-ID]`. Use `1m` for a 1-minute test code. The main csproj excludes `tools\**` from its build.

## Repo notes

- There is no `.gitignore`: `bin/`, `obj/` and `.vs/` are committed, as is `bin/Debug/first version.rar` (an archive of the original version). Don't add new build output in commits unless asked. Ask before adding a `.gitignore` or removing the tracked build files.
- The commit history is short. Messages are informal.
- `README.md` (Russian) is the user-facing usage guide. Update it when behaviour that users see changes.
