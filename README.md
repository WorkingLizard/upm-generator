# Unity Package Creator

A Unity Editor tool for packaging scripts and assets into a UPM-compatible package structure, with support for creating new packages and updating existing ones.

---

## Installation

1. Copy `PackageCreatorWindow.cs` into any `Editor` folder inside your Unity project's `Assets` directory.
2. Unity will compile it automatically. Open the tool via **Tools → Package Creator**.

No additional dependencies required.

---

## Setup

### Source Folder
Point the tool at the folder inside your project that contains the scripts and assets you want to package. You can either drag the folder into the **Folder in Assets** object field or type the path directly (e.g. `Assets/MyFeature`).

The tool will automatically scan the source folder for `.asmdef` files when the path is set.

### Output Folder
The folder where the package will be written. This is an absolute path outside your project — typically a separate repository or a local UPM package cache directory.

If **Create Package Subfolder** is enabled, the tool will create a subfolder named after the package (e.g. `OutputFolder/com.company.mypackage`). Disable this if you want files written directly into the output folder.

---

## Modes

### Create New
Builds the package from scratch. The output directory is created (or wiped and recreated if it already exists). All `.meta` files are generated fresh.

### Update Existing
Copies updated source files into an existing package without destroying its structure. Point the output folder directly at the package root (the folder containing `package.json`). Use **Load from package.json** to pull the existing package metadata into the fields automatically.

The **Override Existing .meta Files** toggle controls whether existing `.meta` files in the destination are replaced. Leave this off to preserve stable GUIDs — turn it on if metas in the destination have become stale or were previously generated incorrectly.

---

## Features

### Deterministic GUID Generation
`.meta` files are generated with GUIDs derived deterministically from the file path via MD5. This means re-running the tool on the same source produces the same GUIDs, avoiding unnecessary git churn.

Metas are only written to disk if their content has changed, so file modification timestamps are not touched unnecessarily.

### .meta Preservation
When **Override Existing .meta Files** is off, any `.meta` that already exists at the destination is left completely untouched. This is important for preserving GUIDs that other assets (ScriptableObjects, prefabs, scene references) depend on.

If your source folder already contains `.meta` files with correct GUIDs, the safest workflow is to keep them there and run in Update mode with override off — the tool will copy them as-is.

### Editor Folder Detection
Files inside any folder named `Editor` (at any depth in the source) are automatically routed to the package's `Editor` directory. Everything else goes to `Runtime`. The `Editor` path segment is stripped from the destination path to keep the structure clean.

If the source folder itself sits inside an `Editor` ancestor, all files are treated as editor-only.

### Assembly Definition Generation
The tool generates `.asmdef` files for both the `Runtime` and `Editor` directories automatically, named after the package (e.g. `com.company.mypackage.Runtime` and `com.company.mypackage.Editor`). The Editor assembly always includes a reference to the Runtime assembly.

If your source folder contains existing `.asmdef` files, they are listed in the **Assembly Connections** section where you can assign each one as a reference to the Runtime assembly, the Editor assembly, or both.

> **Note:** If your source asmdefs use `GUID:`-prefixed references (e.g. `"GUID:df380645f10b7bc4b97d4f5eb6303d95"`), replace them with name-based references before packaging. GUID references are project-local and will break in a distributed package. You can find the assembly name by locating the `.asmdef` file whose `.meta` contains that GUID — for built-in Unity packages, check `Library/PackageCache`.

### .git Skipping
Any `.git` folder in the source or output directory is ignored entirely — no files are copied from it and no `.meta` is generated for it.

### Settings Cache
Settings are saved automatically after every successful Create or Update and can be restored with **Load Cached Settings**. The cache is stored in `ProjectSettings/UPMGeneratorCache/settings.json`, which means it can be committed to source control and shared across a team.

---

## Package Structure

The tool produces a standard UPM layout:

```
com.company.mypackage/
├── package.json
├── package.json.meta
├── Runtime/
│   ├── com.company.mypackage.Runtime.asmdef
│   └── ... (your runtime scripts and assets)
└── Editor/
    ├── com.company.mypackage.Editor.asmdef
    └── ... (your editor scripts)
```

---

## Installing the Generated Package

To use the package in a Unity project, add it to that project's `Packages/manifest.json`:

**From a local path:**
```json
{
  "dependencies": {
    "com.company.mypackage": "file:/absolute/path/to/package"
  }
}
```

**From a Git URL:**
```json
{
  "dependencies": {
    "com.company.mypackage": "https://github.com/you/your-repo.git"
  }
}
```

Or use the Package Manager window: **Window → Package Manager → + → Add package from disk / Add package from git URL**.

---

## License

MIT
