using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PackageCreator.Editor
{
    public class PackageCreatorWindow : EditorWindow
    {
        // ─── Mode ───────────────────────────────────────────────────────
        private enum PackageMode { CreateNew, UpdateExisting }
        private PackageMode _mode = PackageMode.CreateNew;

        // ─── Package Identity ───────────────────────────────────────────
        private string _packageName = "com.company.mypackage";
        private string _displayName = "My Package";
        private string _version = "1.0.0";
        private string _description = "A custom Unity package.";
        private string _unityVersion = "2021.3";
        private string _unityRelease = "0f1";

        private string _authorName = "";
        private string _authorEmail = "";
        private string _authorUrl = "";

        private string _license = "MIT";
        private string _documentationUrl = "";
        private string _changelogUrl = "";
        private string _licensesUrl = "";
        private string _keywords = "";
        private string _dependencies = "";

        private string _sourceFolder = "Assets/";
        private string _outputFolder = "";
        private DefaultAsset _sourceFolderAsset;

        // ─── Update Mode Options ────────────────────────────────────────
        private bool _overrideMetaFiles = false;

        private Vector2 _scroll;
        private bool _foldoutAuthor = true;
        private bool _foldoutOptional = false;

        // ─── Assembly Connections ────────────────────────────────────────
        private bool _foldoutAssemblyConnections = true;
        private List<AsmdefEntry> _discoveredAsmdefs = new List<AsmdefEntry>();
        private string _lastScannedSourceFolder = "";

        [Serializable]
        private class AsmdefEntry
        {
            public string name;
            public string relativePath;
            public bool assignToRuntime;
            public bool assignToEditor;
        }

        // ─── Cache ──────────────────────────────────────────────────────
        private const string CacheFolderName = "UPMGeneratorCache";
        private const string CacheFileName = "settings.json";

        private static string CacheFolderPath =>
            Path.Combine(GetEditorDefaultSettingsPath(), CacheFolderName);

        private static string CacheFilePath =>
            Path.Combine(CacheFolderPath, CacheFileName);

        [Serializable]
        private class CachedSettings
        {
            public string packageName;
            public string displayName;
            public string version;
            public string description;
            public string unityVersion;
            public string unityRelease;
            public string authorName;
            public string authorEmail;
            public string authorUrl;
            public string license;
            public string documentationUrl;
            public string changelogUrl;
            public string licensesUrl;
            public string keywords;
            public string dependencies;
            public string sourceFolder;
            public string outputFolder;
            public List<CachedAsmdefEntry> asmdefEntries;
        }

        [Serializable]
        private class CachedAsmdefEntry
        {
            public string name;
            public string relativePath;
            public bool assignToRuntime;
            public bool assignToEditor;
        }

        // ─── Window ─────────────────────────────────────────────────────

        [MenuItem("Tools/Package Creator")]
        public static void Open()
        {
            var win = GetWindow<PackageCreatorWindow>("Package Creator");
            win.minSize = new Vector2(460, 580);
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Unity Package Creator", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // ─── Mode Toggle ────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_mode == PackageMode.CreateNew, "Create New", EditorStyles.miniButtonLeft))
                _mode = PackageMode.CreateNew;
            if (GUILayout.Toggle(_mode == PackageMode.UpdateExisting, "Update Existing", EditorStyles.miniButtonRight))
                _mode = PackageMode.UpdateExisting;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);

            // ─── Cache buttons ──────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Load Cached Settings", GUILayout.Height(24)))
                LoadCachedSettings();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);

            // ─── Source Folder ───────────────────────────────────────
            EditorGUILayout.LabelField("Source Folder", EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            _sourceFolderAsset = (DefaultAsset)EditorGUILayout.ObjectField(
                "Folder in Assets", _sourceFolderAsset, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck() && _sourceFolderAsset != null)
            {
                _sourceFolder = AssetDatabase.GetAssetPath(_sourceFolderAsset);
                RefreshAsmdefList();
            }

            EditorGUI.BeginChangeCheck();
            _sourceFolder = EditorGUILayout.TextField("Path", _sourceFolder);
            if (EditorGUI.EndChangeCheck())
                RefreshAsmdefList();

            EditorGUILayout.Space(8);

            // ─── Output Folder ──────────────────────────────────────
            string outputLabel = _mode == PackageMode.CreateNew
                ? "Output Folder (package subfolder will be created)"
                : "Existing Package Folder (contains package.json)";
            EditorGUILayout.LabelField(outputLabel, EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            _outputFolder = EditorGUILayout.TextField(_outputFolder);
            if (GUILayout.Button("Browse…", GUILayout.Width(80)))
            {
                string picked = EditorUtility.OpenFolderPanel("Choose output directory", _outputFolder, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    _outputFolder = picked;

                    // In Update mode, auto-load package.json if found
                    if (_mode == PackageMode.UpdateExisting)
                        TryLoadPackageJsonFromFolder(_outputFolder);
                }
            }
            EditorGUILayout.EndHorizontal();

            // ─── Load from package.json button (Update mode) ────────
            if (_mode == PackageMode.UpdateExisting)
            {
                EditorGUILayout.Space(2);
                if (GUILayout.Button("Load from package.json", GUILayout.Height(22)))
                {
                    if (!string.IsNullOrWhiteSpace(_outputFolder))
                        TryLoadPackageJsonFromFolder(_outputFolder);
                    else
                        EditorUtility.DisplayDialog("No Folder", "Set the existing package folder first.", "OK");
                }

                _overrideMetaFiles = EditorGUILayout.Toggle("Override Existing .meta Files", _overrideMetaFiles);
            }

            EditorGUILayout.Space(8);

            // ─── Package Details ────────────────────────────────────
            EditorGUILayout.LabelField("Package Details", EditorStyles.miniBoldLabel);
            _packageName = EditorGUILayout.TextField("Name (com.x.y)", _packageName);
            _displayName = EditorGUILayout.TextField("Display Name", _displayName);
            _version = EditorGUILayout.TextField("Version", _version);
            _description = EditorGUILayout.TextField("Description", _description);
            _unityVersion = EditorGUILayout.TextField("Min Unity Ver.", _unityVersion);
            _unityRelease = EditorGUILayout.TextField("Unity Release", _unityRelease);
            _license = EditorGUILayout.TextField("License", _license);
            EditorGUILayout.Space(4);

            // ─── Author ─────────────────────────────────────────────
            _foldoutAuthor = EditorGUILayout.Foldout(_foldoutAuthor, "Author", true);
            if (_foldoutAuthor)
            {
                EditorGUI.indentLevel++;
                _authorName = EditorGUILayout.TextField("Name", _authorName);
                _authorEmail = EditorGUILayout.TextField("Email", _authorEmail);
                _authorUrl = EditorGUILayout.TextField("URL", _authorUrl);
                EditorGUI.indentLevel--;
            }

            // ─── Optional ───────────────────────────────────────────
            _foldoutOptional = EditorGUILayout.Foldout(_foldoutOptional, "Optional", true);
            if (_foldoutOptional)
            {
                EditorGUI.indentLevel++;
                _documentationUrl = EditorGUILayout.TextField("Docs URL", _documentationUrl);
                _changelogUrl = EditorGUILayout.TextField("Changelog URL", _changelogUrl);
                _licensesUrl = EditorGUILayout.TextField("Licenses URL", _licensesUrl);
                _keywords = EditorGUILayout.TextField("Keywords (csv)", _keywords);
                EditorGUILayout.LabelField("Dependencies  (one per line:  com.unity.pkg:1.0.0)");
                _dependencies = EditorGUILayout.TextArea(_dependencies, GUILayout.Height(48));
                EditorGUI.indentLevel--;
            }

            // ─── Assembly Connections ────────────────────────────────
            _foldoutAssemblyConnections = EditorGUILayout.Foldout(_foldoutAssemblyConnections, "Assembly Connections", true);
            if (_foldoutAssemblyConnections)
            {
                EditorGUI.indentLevel++;
                DrawAssemblyConnectionsUI();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(12);

            // ─── Create / Update Button ─────────────────────────────
            GUI.enabled = IsValid();
            string buttonLabel = _mode == PackageMode.CreateNew ? "Create Package" : "Update Package";
            if (GUILayout.Button(buttonLabel, GUILayout.Height(32)))
            {
                if (_mode == PackageMode.CreateNew)
                    CreatePackage();
                else
                    UpdatePackage();
            }
            GUI.enabled = true;

            if (!IsValid())
                EditorGUILayout.HelpBox(GetValidationMessage(), MessageType.Warning);

            EditorGUILayout.EndScrollView();
        }

        // ─── Load package.json into fields ──────────────────────────────

        private void TryLoadPackageJsonFromFolder(string folder)
        {
            string pkgJsonPath = Path.Combine(folder, "package.json");
            if (!File.Exists(pkgJsonPath))
            {
                EditorUtility.DisplayDialog("Not Found",
                    $"No package.json found in:\n{folder}", "OK");
                return;
            }

            try
            {
                string json = File.ReadAllText(pkgJsonPath);

                _packageName = ExtractJsonStringField(json, "name") ?? _packageName;
                _displayName = ExtractJsonStringField(json, "displayName") ?? _displayName;
                _version = ExtractJsonStringField(json, "version") ?? _version;
                _description = ExtractJsonStringField(json, "description") ?? _description;
                _unityVersion = ExtractJsonStringField(json, "unity") ?? _unityVersion;
                _unityRelease = ExtractJsonStringField(json, "unityRelease") ?? _unityRelease;
                _license = ExtractJsonStringField(json, "license") ?? _license;
                _documentationUrl = ExtractJsonStringField(json, "documentationUrl") ?? "";
                _changelogUrl = ExtractJsonStringField(json, "changelogUrl") ?? "";
                _licensesUrl = ExtractJsonStringField(json, "licensesUrl") ?? "";

                // Author block
                _authorName = ExtractNestedJsonStringField(json, "author", "name") ?? "";
                _authorEmail = ExtractNestedJsonStringField(json, "author", "email") ?? "";
                _authorUrl = ExtractNestedJsonStringField(json, "author", "url") ?? "";

                // Keywords
                _keywords = ExtractJsonArrayAsCSV(json, "keywords") ?? "";

                // Dependencies
                _dependencies = ExtractJsonObjectAsLines(json, "dependencies") ?? "";

                Debug.Log($"[PackageCreator] Loaded package.json from {pkgJsonPath}");
                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageCreator] Failed to parse package.json: {ex.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to parse package.json:\n{ex.Message}", "OK");
            }
        }

        // ─── Assembly Connection Scanning & UI ──────────────────────────

        private void RefreshAsmdefList()
        {
            string absSource = GetAbsoluteSourcePath();
            if (!Directory.Exists(absSource))
            {
                _discoveredAsmdefs.Clear();
                _lastScannedSourceFolder = "";
                return;
            }

            if (absSource == _lastScannedSourceFolder)
                return;

            _lastScannedSourceFolder = absSource;

            var previousSelections = _discoveredAsmdefs.ToDictionary(
                a => a.name,
                a => (runtime: a.assignToRuntime, editor: a.assignToEditor));

            _discoveredAsmdefs.Clear();

            string[] asmdefFiles = Directory.GetFiles(absSource, "*.asmdef", SearchOption.AllDirectories);

            foreach (string asmdefPath in asmdefFiles)
            {
                string json = File.ReadAllText(asmdefPath);
                string asmName = ExtractJsonStringField(json, "name");
                if (string.IsNullOrEmpty(asmName))
                    asmName = Path.GetFileNameWithoutExtension(asmdefPath);

                string relPath = GetRelativePath(absSource, asmdefPath);

                var entry = new AsmdefEntry
                {
                    name = asmName,
                    relativePath = relPath,
                    assignToRuntime = false,
                    assignToEditor = false,
                };

                if (previousSelections.TryGetValue(asmName, out var prev))
                {
                    entry.assignToRuntime = prev.runtime;
                    entry.assignToEditor = prev.editor;
                }

                _discoveredAsmdefs.Add(entry);
            }
        }

        private void DrawAssemblyConnectionsUI()
        {
            if (_discoveredAsmdefs.Count == 0)
            {
                string absSource = GetAbsoluteSourcePath();
                if (!Directory.Exists(absSource))
                    EditorGUILayout.HelpBox("Set a valid source folder to scan for assembly definitions.", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("No .asmdef files found in the source folder.", MessageType.Info);

                if (GUILayout.Button("Rescan", GUILayout.Width(80)))
                {
                    _lastScannedSourceFolder = "";
                    RefreshAsmdefList();
                }
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Assembly Definition", EditorStyles.miniBoldLabel, GUILayout.MinWidth(160));
            EditorGUILayout.LabelField("Runtime", EditorStyles.miniBoldLabel, GUILayout.Width(56));
            EditorGUILayout.LabelField("Editor", EditorStyles.miniBoldLabel, GUILayout.Width(56));
            EditorGUILayout.EndHorizontal();

            foreach (var entry in _discoveredAsmdefs)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(entry.name, GUILayout.MinWidth(160));
                entry.assignToRuntime = EditorGUILayout.Toggle(entry.assignToRuntime, GUILayout.Width(56));
                entry.assignToEditor = EditorGUILayout.Toggle(entry.assignToEditor, GUILayout.Width(56));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(2);
            if (GUILayout.Button("Rescan", GUILayout.Width(80)))
            {
                _lastScannedSourceFolder = "";
                RefreshAsmdefList();
            }
        }

        // ─── Validation ─────────────────────────────────────────────────

        private bool IsValid()
        {
            if (string.IsNullOrWhiteSpace(_packageName)) return false;
            if (string.IsNullOrWhiteSpace(_version)) return false;
            if (string.IsNullOrWhiteSpace(_sourceFolder)) return false;
            if (string.IsNullOrWhiteSpace(_outputFolder)) return false;
            if (!Directory.Exists(GetAbsoluteSourcePath())) return false;

            if (_mode == PackageMode.UpdateExisting)
            {
                string pkgJson = Path.Combine(_outputFolder, "package.json");
                if (!File.Exists(pkgJson)) return false;
            }

            return true;
        }

        private string GetValidationMessage()
        {
            if (string.IsNullOrWhiteSpace(_packageName)) return "Package name is required.";
            if (string.IsNullOrWhiteSpace(_version)) return "Version is required.";
            if (string.IsNullOrWhiteSpace(_sourceFolder)) return "Source folder is required.";
            if (string.IsNullOrWhiteSpace(_outputFolder)) return "Output folder is required.";
            if (!Directory.Exists(GetAbsoluteSourcePath())) return "Source folder does not exist.";

            if (_mode == PackageMode.UpdateExisting)
            {
                string pkgJson = Path.Combine(_outputFolder, "package.json");
                if (!File.Exists(pkgJson))
                    return "Output folder must contain an existing package.json for Update mode.";
            }

            return "";
        }

        private string GetAbsoluteSourcePath()
        {
            if (Path.IsPathRooted(_sourceFolder)) return _sourceFolder;
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", _sourceFolder));
        }

        // ─── Package Creation (New) ─────────────────────────────────────

        private void CreatePackage()
        {
            try
            {
                string srcRoot = GetAbsoluteSourcePath();
                string pkgRoot = Path.Combine(_outputFolder, _packageName);

                if (Directory.Exists(pkgRoot))
                {
                    if (!EditorUtility.DisplayDialog("Overwrite?",
                        $"'{pkgRoot}' already exists. Delete and recreate?", "Yes", "Cancel"))
                        return;
                    Directory.Delete(pkgRoot, true);
                }

                string runtimeDir = Path.Combine(pkgRoot, "Runtime");
                string editorDir = Path.Combine(pkgRoot, "Editor");
                Directory.CreateDirectory(runtimeDir);
                Directory.CreateDirectory(editorDir);

                CopySourceFiles(srcRoot, runtimeDir, editorDir, overrideMeta: true);

                foreach (string dir in Directory.GetDirectories(pkgRoot, "*", SearchOption.AllDirectories))
                    WriteMeta(dir, isFolder: true, overrideMeta: true);

                WriteMeta(runtimeDir, isFolder: true, overrideMeta: true);
                WriteMeta(editorDir, isFolder: true, overrideMeta: true);

                WriteAssemblyDefinitions(runtimeDir, editorDir);
                WritePackageJson(pkgRoot);
                WriteMeta(Path.Combine(pkgRoot, "package.json"), isFolder: false, overrideMeta: true);

                SaveCachedSettings();

                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Done!",
                    $"Package created at:\n{pkgRoot}", "OK");

                EditorUtility.RevealInFinder(pkgRoot);
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[PackageCreator] {ex}");
                EditorUtility.DisplayDialog("Error", ex.Message, "OK");
            }
        }

        // ─── Package Update (Existing) ──────────────────────────────────

        private void UpdatePackage()
        {
            try
            {
                string srcRoot = GetAbsoluteSourcePath();
                string pkgRoot = _outputFolder; // directly into the existing package folder

                string runtimeDir = Path.Combine(pkgRoot, "Runtime");
                string editorDir = Path.Combine(pkgRoot, "Editor");
                Directory.CreateDirectory(runtimeDir);
                Directory.CreateDirectory(editorDir);

                CopySourceFiles(srcRoot, runtimeDir, editorDir, overrideMeta: _overrideMetaFiles);

                foreach (string dir in Directory.GetDirectories(pkgRoot, "*", SearchOption.AllDirectories))
                    WriteMeta(dir, isFolder: true, overrideMeta: _overrideMetaFiles);

                WriteMeta(runtimeDir, isFolder: true, overrideMeta: _overrideMetaFiles);
                WriteMeta(editorDir, isFolder: true, overrideMeta: _overrideMetaFiles);

                WriteAssemblyDefinitions(runtimeDir, editorDir);
                WritePackageJson(pkgRoot);
                WriteMeta(Path.Combine(pkgRoot, "package.json"), isFolder: false, overrideMeta: _overrideMetaFiles);

                SaveCachedSettings();

                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Done!",
                    $"Package updated at:\n{pkgRoot}", "OK");

                EditorUtility.RevealInFinder(pkgRoot);
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[PackageCreator] {ex}");
                EditorUtility.DisplayDialog("Error", ex.Message, "OK");
            }
        }

        // ─── Shared: Copy Source Files ──────────────────────────────────

        private void CopySourceFiles(string srcRoot, string runtimeDir, string editorDir, bool overrideMeta)
        {
            var allFiles = Directory.GetFiles(srcRoot, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .ToList();

            bool sourceUnderEditor = IsSourceUnderEditorAncestor(srcRoot);

            int processed = 0;
            foreach (string absFile in allFiles)
            {
                EditorUtility.DisplayProgressBar("Processing files…",
                    Path.GetFileName(absFile),
                    (float)processed++ / allFiles.Count);

                string relPath = GetRelativePath(srcRoot, absFile);
                bool relHasEditor = IsEditorPath(relPath);
                bool isEditor = sourceUnderEditor || relHasEditor;

                string cleanRel = (isEditor && relHasEditor) ? StripEditorSegment(relPath) : relPath;

                string destDir = isEditor ? editorDir : runtimeDir;
                string destPath = Path.Combine(destDir, cleanRel);

                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                File.Copy(absFile, destPath, true); // always overwrite the actual file

                WriteMeta(destPath, isFolder: false, overrideMeta: overrideMeta);
            }
        }

        // ─── Shared: Write Assembly Definitions ─────────────────────────

        private void WriteAssemblyDefinitions(string runtimeDir, string editorDir)
        {
            string asmBase = _packageName.Replace("-", ".").Replace(" ", "");

            var runtimeRefs = _discoveredAsmdefs
                .Where(a => a.assignToRuntime)
                .Select(a => a.name)
                .ToList();

            var editorRefs = _discoveredAsmdefs
                .Where(a => a.assignToEditor)
                .Select(a => a.name)
                .ToList();

            string runtimeAsmName = $"{asmBase}.Runtime";
            string editorAsmName = $"{asmBase}.Editor";

            var editorAllRefs = new List<string> { runtimeAsmName };
            editorAllRefs.AddRange(editorRefs);

            WriteAsmdef(runtimeDir, runtimeAsmName, isEditor: false, references: runtimeRefs);
            WriteAsmdef(editorDir, editorAsmName, isEditor: true, references: editorAllRefs);
        }

        /// <summary>Write a minimal .meta file (GUID derived from path for determinism).</summary>
        private static void WriteMeta(string targetPath, bool isFolder, bool overrideMeta)
        {
            string metaPath = targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".meta";

            // If not overriding and meta already exists, skip
            if (!overrideMeta && File.Exists(metaPath))
                return;

            // If overriding is off but file is new (no existing meta), still create it
            // (handled by the check above — if we reach here, either override is on or meta doesn't exist)

            string guid = DeterministicGuid(targetPath);

            var sb = new StringBuilder();
            sb.AppendLine("fileFormatVersion: 2");
            sb.AppendLine($"guid: {guid}");

            if (isFolder)
            {
                sb.AppendLine("folderAsset: yes");
                sb.AppendLine("DefaultImporter:");
                sb.AppendLine("  externalObjects: {}");
                sb.AppendLine("  userData: ");
                sb.AppendLine("  assetBundleName: ");
                sb.AppendLine("  assetBundleVariant: ");
            }
            else
            {
                string ext = Path.GetExtension(targetPath).ToLowerInvariant();
                if (ext == ".cs")
                {
                    sb.AppendLine("MonoImporter:");
                    sb.AppendLine("  externalObjects: {}");
                    sb.AppendLine("  serializedVersion: 2");
                    sb.AppendLine("  defaultReferences: []");
                    sb.AppendLine("  executionOrder: 0");
                    sb.AppendLine("  icon: {instanceID: 0}");
                    sb.AppendLine("  userData: ");
                    sb.AppendLine("  assetBundleName: ");
                    sb.AppendLine("  assetBundleVariant: ");
                }
                else if (ext == ".asmdef")
                {
                    sb.AppendLine("AssemblyDefinitionImporter:");
                    sb.AppendLine("  externalObjects: {}");
                    sb.AppendLine("  userData: ");
                    sb.AppendLine("  assetBundleName: ");
                    sb.AppendLine("  assetBundleVariant: ");
                }
                else if (ext == ".json")
                {
                    sb.AppendLine("TextScriptImporter:");
                    sb.AppendLine("  externalObjects: {}");
                    sb.AppendLine("  userData: ");
                    sb.AppendLine("  assetBundleName: ");
                    sb.AppendLine("  assetBundleVariant: ");
                }
                else
                {
                    sb.AppendLine("DefaultImporter:");
                    sb.AppendLine("  externalObjects: {}");
                    sb.AppendLine("  userData: ");
                    sb.AppendLine("  assetBundleName: ");
                    sb.AppendLine("  assetBundleVariant: ");
                }
            }

            File.WriteAllText(metaPath, sb.ToString());
        }

        /// <summary>Write a .asmdef and its .meta with the given references.</summary>
        private void WriteAsmdef(string dir, string asmName, bool isEditor, List<string> references)
        {
            string path = Path.Combine(dir, asmName + ".asmdef");

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"    \"name\": \"{asmName}\",");

            if (references != null && references.Count > 0)
            {
                sb.AppendLine("    \"references\": [");
                for (int i = 0; i < references.Count; i++)
                {
                    string comma = i < references.Count - 1 ? "," : "";
                    sb.AppendLine($"        \"{Escape(references[i])}\"{comma}");
                }
                sb.AppendLine("    ],");
            }
            else
            {
                sb.AppendLine("    \"references\": [],");
            }

            if (isEditor)
            {
                sb.AppendLine("    \"includePlatforms\": [\"Editor\"],");
                sb.AppendLine("    \"excludePlatforms\": [],");
            }
            else
            {
                sb.AppendLine("    \"includePlatforms\": [],");
                sb.AppendLine("    \"excludePlatforms\": [],");
            }

            sb.AppendLine("    \"allowUnsafeCode\": false,");
            sb.AppendLine("    \"overrideReferences\": false,");
            sb.AppendLine("    \"precompiledReferences\": [],");
            sb.AppendLine("    \"autoReferenced\": true,");
            sb.AppendLine("    \"defineConstraints\": [],");
            sb.AppendLine("    \"versionDefines\": [],");
            sb.AppendLine("    \"noEngineReferences\": false");
            sb.AppendLine("}");

            File.WriteAllText(path, sb.ToString());
            WriteMeta(path, isFolder: false, overrideMeta: true);
        }

        /// <summary>Write the package.json manifest.</summary>
        private void WritePackageJson(string pkgRoot)
        {
            string path = Path.Combine(pkgRoot, "package.json");

            var kwList = _keywords
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .ToList();

            var depPairs = _dependencies
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Contains(':'))
                .Select(l =>
                {
                    int idx = l.IndexOf(':');
                    return (key: l.Substring(0, idx).Trim(), val: l.Substring(idx + 1).Trim());
                })
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"    \"name\": \"{Escape(_packageName)}\",");
            sb.AppendLine($"    \"version\": \"{Escape(_version)}\",");
            sb.AppendLine($"    \"displayName\": \"{Escape(_displayName)}\",");
            sb.AppendLine($"    \"description\": \"{Escape(_description)}\",");
            sb.AppendLine($"    \"unity\": \"{Escape(_unityVersion)}\",");
            sb.AppendLine($"    \"unityRelease\": \"{Escape(_unityRelease)}\",");

            if (!string.IsNullOrWhiteSpace(_documentationUrl))
                sb.AppendLine($"    \"documentationUrl\": \"{Escape(_documentationUrl)}\",");
            if (!string.IsNullOrWhiteSpace(_changelogUrl))
                sb.AppendLine($"    \"changelogUrl\": \"{Escape(_changelogUrl)}\",");
            if (!string.IsNullOrWhiteSpace(_licensesUrl))
                sb.AppendLine($"    \"licensesUrl\": \"{Escape(_licensesUrl)}\",");

            if (!string.IsNullOrWhiteSpace(_license))
                sb.AppendLine($"    \"license\": \"{Escape(_license)}\",");

            if (kwList.Count > 0)
            {
                sb.AppendLine("    \"keywords\": [");
                for (int i = 0; i < kwList.Count; i++)
                {
                    string comma = i < kwList.Count - 1 ? "," : "";
                    sb.AppendLine($"        \"{Escape(kwList[i])}\"{comma}");
                }
                sb.AppendLine("    ],");
            }
            else
            {
                sb.AppendLine("    \"keywords\": [],");
            }

            bool hasAuthor = !string.IsNullOrWhiteSpace(_authorName);
            if (hasAuthor)
            {
                sb.AppendLine("    \"author\": {");
                sb.AppendLine($"        \"name\": \"{Escape(_authorName)}\"");
                if (!string.IsNullOrWhiteSpace(_authorEmail))
                {
                    sb.Remove(sb.Length - Environment.NewLine.Length, Environment.NewLine.Length);
                    sb.AppendLine(",");
                    sb.AppendLine($"        \"email\": \"{Escape(_authorEmail)}\"");
                }
                if (!string.IsNullOrWhiteSpace(_authorUrl))
                {
                    sb.Remove(sb.Length - Environment.NewLine.Length, Environment.NewLine.Length);
                    sb.AppendLine(",");
                    sb.AppendLine($"        \"url\": \"{Escape(_authorUrl)}\"");
                }
                sb.AppendLine("    },");
            }

            if (depPairs.Count > 0)
            {
                sb.AppendLine("    \"dependencies\": {");
                for (int i = 0; i < depPairs.Count; i++)
                {
                    string comma = i < depPairs.Count - 1 ? "," : "";
                    sb.AppendLine($"        \"{Escape(depPairs[i].key)}\": \"{Escape(depPairs[i].val)}\"{comma}");
                }
                sb.AppendLine("    }");
            }
            else
            {
                sb.AppendLine("    \"dependencies\": {}");
            }

            sb.AppendLine("}");

            File.WriteAllText(path, sb.ToString());
        }

        // ─── Cache Load / Save ──────────────────────────────────────────

        private static string GetEditorDefaultSettingsPath()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, "ProjectSettings");
        }

        private void SaveCachedSettings()
        {
            try
            {
                var settings = new CachedSettings
                {
                    packageName = _packageName,
                    displayName = _displayName,
                    version = _version,
                    description = _description,
                    unityVersion = _unityVersion,
                    unityRelease = _unityRelease,
                    authorName = _authorName,
                    authorEmail = _authorEmail,
                    authorUrl = _authorUrl,
                    license = _license,
                    documentationUrl = _documentationUrl,
                    changelogUrl = _changelogUrl,
                    licensesUrl = _licensesUrl,
                    keywords = _keywords,
                    dependencies = _dependencies,
                    sourceFolder = _sourceFolder,
                    outputFolder = _outputFolder,
                    asmdefEntries = _discoveredAsmdefs.Select(a => new CachedAsmdefEntry
                    {
                        name = a.name,
                        relativePath = a.relativePath,
                        assignToRuntime = a.assignToRuntime,
                        assignToEditor = a.assignToEditor,
                    }).ToList(),
                };

                if (!Directory.Exists(CacheFolderPath))
                    Directory.CreateDirectory(CacheFolderPath);

                string json = JsonUtility.ToJson(settings, true);
                File.WriteAllText(CacheFilePath, json);

                Debug.Log($"[PackageCreator] Settings cached to {CacheFilePath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PackageCreator] Failed to save cache: {ex.Message}");
            }
        }

        private void LoadCachedSettings()
        {
            if (!File.Exists(CacheFilePath))
            {
                EditorUtility.DisplayDialog("No Cache Found",
                    $"No cached settings found.\nExpected at: {CacheFilePath}\n\nSettings are cached automatically after the first package is generated.",
                    "OK");
                return;
            }

            try
            {
                string json = File.ReadAllText(CacheFilePath);
                var settings = JsonUtility.FromJson<CachedSettings>(json);

                _packageName = settings.packageName ?? _packageName;
                _displayName = settings.displayName ?? _displayName;
                _version = settings.version ?? _version;
                _description = settings.description ?? _description;
                _unityVersion = settings.unityVersion ?? _unityVersion;
                _unityRelease = settings.unityRelease ?? _unityRelease;
                _authorName = settings.authorName ?? "";
                _authorEmail = settings.authorEmail ?? "";
                _authorUrl = settings.authorUrl ?? "";
                _license = settings.license ?? "";
                _documentationUrl = settings.documentationUrl ?? "";
                _changelogUrl = settings.changelogUrl ?? "";
                _licensesUrl = settings.licensesUrl ?? "";
                _keywords = settings.keywords ?? "";
                _dependencies = settings.dependencies ?? "";
                _sourceFolder = settings.sourceFolder ?? _sourceFolder;
                _outputFolder = settings.outputFolder ?? _outputFolder;

                if (!string.IsNullOrEmpty(_sourceFolder) && _sourceFolder.StartsWith("Assets"))
                    _sourceFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_sourceFolder);
                else
                    _sourceFolderAsset = null;

                _lastScannedSourceFolder = "";
                RefreshAsmdefList();

                if (settings.asmdefEntries != null)
                {
                    var cachedByName = settings.asmdefEntries.ToDictionary(e => e.name, e => e);
                    foreach (var entry in _discoveredAsmdefs)
                    {
                        if (cachedByName.TryGetValue(entry.name, out var cached))
                        {
                            entry.assignToRuntime = cached.assignToRuntime;
                            entry.assignToEditor = cached.assignToEditor;
                        }
                    }
                }

                Debug.Log("[PackageCreator] Cached settings loaded.");
                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageCreator] Failed to load cache: {ex.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to load cached settings:\n{ex.Message}", "OK");
            }
        }

        // ─── Utilities ──────────────────────────────────────────────────

        private static bool IsEditorPath(string relativePath)
        {
            string[] parts = relativePath.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            return parts.Any(p => p.Equals("Editor", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSourceUnderEditorAncestor(string sourceFolder)
        {
            string assetsDir = Path.GetFullPath(Application.dataPath);

            string fullSource = Path.GetFullPath(sourceFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            string current = Directory.GetParent(fullSource)?.FullName;
            while (!string.IsNullOrEmpty(current) && current.Length >= assetsDir.Length)
            {
                string dirName = Path.GetFileName(current);
                if (dirName.Equals("Editor", StringComparison.OrdinalIgnoreCase))
                    return true;

                current = Directory.GetParent(current)?.FullName;
            }

            return false;
        }

        private static string StripEditorSegment(string relativePath)
        {
            var parts = relativePath.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            int idx = parts.FindIndex(p => p.Equals("Editor", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) parts.RemoveAt(idx);

            return Path.Combine(parts.ToArray());
        }

        private static string GetRelativePath(string root, string full)
        {
            root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                 + Path.DirectorySeparatorChar;

            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return full.Substring(root.Length);

            Uri rootUri = new Uri(root);
            Uri fullUri = new Uri(full);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fullUri).ToString())
                      .Replace('/', Path.DirectorySeparatorChar);
        }

        private static string DeterministicGuid(string input)
        {
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                var sb = new StringBuilder(32);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>Extract a top-level string field from JSON without a full parser.</summary>
        private static string ExtractJsonStringField(string json, string fieldName)
        {
            string pattern = $"\"{fieldName}\"";
            int keyIdx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (keyIdx < 0) return null;

            int colonIdx = json.IndexOf(':', keyIdx + pattern.Length);
            if (colonIdx < 0) return null;

            int openQuote = json.IndexOf('"', colonIdx + 1);
            if (openQuote < 0) return null;

            int closeQuote = json.IndexOf('"', openQuote + 1);
            if (closeQuote < 0) return null;

            return json.Substring(openQuote + 1, closeQuote - openQuote - 1);
        }

        /// <summary>Extract a string field nested inside an object field (e.g. author.name).</summary>
        private static string ExtractNestedJsonStringField(string json, string objectField, string nestedField)
        {
            string objectPattern = $"\"{objectField}\"";
            int objIdx = json.IndexOf(objectPattern, StringComparison.Ordinal);
            if (objIdx < 0) return null;

            int braceOpen = json.IndexOf('{', objIdx + objectPattern.Length);
            if (braceOpen < 0) return null;

            int braceClose = FindMatchingBrace(json, braceOpen);
            if (braceClose < 0) return null;

            string objectContent = json.Substring(braceOpen, braceClose - braceOpen + 1);
            return ExtractJsonStringField(objectContent, nestedField);
        }

        /// <summary>Extract a JSON array of strings as a CSV string.</summary>
        private static string ExtractJsonArrayAsCSV(string json, string fieldName)
        {
            string pattern = $"\"{fieldName}\"";
            int keyIdx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (keyIdx < 0) return null;

            int bracketOpen = json.IndexOf('[', keyIdx + pattern.Length);
            if (bracketOpen < 0) return null;

            int bracketClose = json.IndexOf(']', bracketOpen);
            if (bracketClose < 0) return null;

            string arrayContent = json.Substring(bracketOpen + 1, bracketClose - bracketOpen - 1);

            var items = new List<string>();
            int pos = 0;
            while (pos < arrayContent.Length)
            {
                int qOpen = arrayContent.IndexOf('"', pos);
                if (qOpen < 0) break;
                int qClose = arrayContent.IndexOf('"', qOpen + 1);
                if (qClose < 0) break;
                items.Add(arrayContent.Substring(qOpen + 1, qClose - qOpen - 1));
                pos = qClose + 1;
            }

            return string.Join(", ", items);
        }

        /// <summary>Extract a JSON object of string:string pairs as newline-separated key:value lines.</summary>
        private static string ExtractJsonObjectAsLines(string json, string fieldName)
        {
            string pattern = $"\"{fieldName}\"";
            int keyIdx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (keyIdx < 0) return null;

            int braceOpen = json.IndexOf('{', keyIdx + pattern.Length);
            if (braceOpen < 0) return null;

            int braceClose = FindMatchingBrace(json, braceOpen);
            if (braceClose < 0) return null;

            string objContent = json.Substring(braceOpen + 1, braceClose - braceOpen - 1);

            var lines = new List<string>();
            int pos = 0;
            while (pos < objContent.Length)
            {
                int kOpen = objContent.IndexOf('"', pos);
                if (kOpen < 0) break;
                int kClose = objContent.IndexOf('"', kOpen + 1);
                if (kClose < 0) break;
                string key = objContent.Substring(kOpen + 1, kClose - kOpen - 1);

                int vOpen = objContent.IndexOf('"', kClose + 1);
                if (vOpen < 0) break;
                int vClose = objContent.IndexOf('"', vOpen + 1);
                if (vClose < 0) break;
                string val = objContent.Substring(vOpen + 1, vClose - vOpen - 1);

                lines.Add($"{key}:{val}");
                pos = vClose + 1;
            }

            return string.Join("\n", lines);
        }

        /// <summary>Find the matching closing brace for an opening brace.</summary>
        private static int FindMatchingBrace(string json, int openIndex)
        {
            int depth = 0;
            for (int i = openIndex; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') depth--;
                if (depth == 0) return i;
            }
            return -1;
        }
    }
}