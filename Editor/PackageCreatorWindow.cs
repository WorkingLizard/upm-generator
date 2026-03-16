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
        // ─── Package Identity ───────────────────────────────────────────
        private string _packageName = "com.company.mypackage";
        private string _displayName = "My Package";
        private string _version = "1.0.0";
        private string _description = "A custom Unity package.";
        private string _unityVersion = "2021.3";
        private string _unityRelease = "0f1";

        // ─── Author ─────────────────────────────────────────────────────
        private string _authorName = "";
        private string _authorEmail = "";
        private string _authorUrl = "";

        // ─── Optional ───────────────────────────────────────────────────
        private string _license = "MIT";
        private string _documentationUrl = "";
        private string _changelogUrl = "";
        private string _licensesUrl = "";
        private string _keywords = "";          // comma-separated
        private string _dependencies = "";          // "com.unity.something:1.0.0" per line

        // ─── Paths ──────────────────────────────────────────────────────
        private string _sourceFolder = "Assets/";
        private string _outputFolder = "";
        private DefaultAsset _sourceFolderAsset;

        // ─── UI State ───────────────────────────────────────────────────
        private Vector2 _scroll;
        private bool _foldoutAuthor = true;
        private bool _foldoutOptional = false;

        // ─────────────────────────────────────────────────────────────────
        [MenuItem("Tools/Package Creator")]
        public static void Open()
        {
            var win = GetWindow<PackageCreatorWindow>("Package Creator");
            win.minSize = new Vector2(460, 520);
        }

        // ================================================================
        //  GUI
        // ================================================================
        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Unity Package Creator", EditorStyles.boldLabel);
            EditorGUILayout.Space(6);

            // ── Source folder (drag-drop or picker) ─────────────────────
            EditorGUILayout.LabelField("Source Folder", EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            _sourceFolderAsset = (DefaultAsset)EditorGUILayout.ObjectField(
                "Folder in Assets", _sourceFolderAsset, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck() && _sourceFolderAsset != null)
                _sourceFolder = AssetDatabase.GetAssetPath(_sourceFolderAsset);

            _sourceFolder = EditorGUILayout.TextField("Path", _sourceFolder);
            EditorGUILayout.Space(8);

            // ── Output folder ───────────────────────────────────────────
            EditorGUILayout.LabelField("Output Folder", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            _outputFolder = EditorGUILayout.TextField(_outputFolder);
            if (GUILayout.Button("Browse…", GUILayout.Width(80)))
            {
                string picked = EditorUtility.OpenFolderPanel("Choose output directory", _outputFolder, "");
                if (!string.IsNullOrEmpty(picked)) _outputFolder = picked;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8);

            // ── Package identity ────────────────────────────────────────
            EditorGUILayout.LabelField("Package Details", EditorStyles.miniBoldLabel);
            _packageName = EditorGUILayout.TextField("Name (com.x.y)", _packageName);
            _displayName = EditorGUILayout.TextField("Display Name", _displayName);
            _version = EditorGUILayout.TextField("Version", _version);
            _description = EditorGUILayout.TextField("Description", _description);
            _unityVersion = EditorGUILayout.TextField("Min Unity Ver.", _unityVersion);
            _unityRelease = EditorGUILayout.TextField("Unity Release", _unityRelease);
            _license = EditorGUILayout.TextField("License", _license);
            EditorGUILayout.Space(4);

            // ── Author ──────────────────────────────────────────────────
            _foldoutAuthor = EditorGUILayout.Foldout(_foldoutAuthor, "Author", true);
            if (_foldoutAuthor)
            {
                EditorGUI.indentLevel++;
                _authorName = EditorGUILayout.TextField("Name", _authorName);
                _authorEmail = EditorGUILayout.TextField("Email", _authorEmail);
                _authorUrl = EditorGUILayout.TextField("URL", _authorUrl);
                EditorGUI.indentLevel--;
            }

            // ── Optional fields ─────────────────────────────────────────
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

            EditorGUILayout.Space(12);

            // ── Create button ───────────────────────────────────────────
            GUI.enabled = IsValid();
            if (GUILayout.Button("Create Package", GUILayout.Height(32)))
                CreatePackage();
            GUI.enabled = true;

            if (!IsValid())
                EditorGUILayout.HelpBox(GetValidationMessage(), MessageType.Warning);

            EditorGUILayout.EndScrollView();
        }

        // ================================================================
        //  Validation
        // ================================================================
        private bool IsValid()
        {
            if (string.IsNullOrWhiteSpace(_packageName)) return false;
            if (string.IsNullOrWhiteSpace(_version)) return false;
            if (string.IsNullOrWhiteSpace(_sourceFolder)) return false;
            if (string.IsNullOrWhiteSpace(_outputFolder)) return false;
            if (!Directory.Exists(GetAbsoluteSourcePath())) return false;
            return true;
        }

        private string GetValidationMessage()
        {
            if (string.IsNullOrWhiteSpace(_packageName)) return "Package name is required.";
            if (string.IsNullOrWhiteSpace(_version)) return "Version is required.";
            if (string.IsNullOrWhiteSpace(_sourceFolder)) return "Source folder is required.";
            if (string.IsNullOrWhiteSpace(_outputFolder)) return "Output folder is required.";
            if (!Directory.Exists(GetAbsoluteSourcePath())) return "Source folder does not exist.";
            return "";
        }

        private string GetAbsoluteSourcePath()
        {
            if (Path.IsPathRooted(_sourceFolder)) return _sourceFolder;
            // Relative to project root (Assets lives one level in)
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", _sourceFolder));
        }

        // ================================================================
        //  Package creation
        // ================================================================
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

                // ── Collect every file (skip .meta originals) ───────────
                var allFiles = Directory.GetFiles(srcRoot, "*", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Check once whether the source folder itself sits under an
                // "Editor" ancestor (between it and Assets).
                bool sourceUnderEditor = IsSourceUnderEditorAncestor(srcRoot);

                int processed = 0;
                foreach (string absFile in allFiles)
                {
                    EditorUtility.DisplayProgressBar("Creating package…",
                        Path.GetFileName(absFile),
                        (float)processed++ / allFiles.Count);

                    string relPath = GetRelativePath(srcRoot, absFile);
                    bool relHasEditor = IsEditorPath(relPath);
                    bool isEditor = sourceUnderEditor || relHasEditor;

                    // Only strip the "Editor" segment from the relative path if
                    // it actually has one. When the source folder's *ancestor* is
                    // Editor, the relative path has nothing to strip.
                    string cleanRel = (isEditor && relHasEditor) ? StripEditorSegment(relPath) : relPath;

                    string destDir = isEditor ? editorDir : runtimeDir;
                    string destPath = Path.Combine(destDir, cleanRel);

                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    File.Copy(absFile, destPath, true);

                    // Generate a .meta for the file
                    WriteMeta(destPath, isFolder: false);
                }

                // ── Generate .meta for every created directory ──────────
                foreach (string dir in Directory.GetDirectories(pkgRoot, "*", SearchOption.AllDirectories))
                    WriteMeta(dir, isFolder: true);

                // Also write metas for the top-level Runtime / Editor dirs
                // (already covered above, but ensure root package folder too)
                WriteMeta(runtimeDir, isFolder: true);
                WriteMeta(editorDir, isFolder: true);

                // ── Root assembly name helper ───────────────────────────
                string asmBase = _packageName.Replace("-", ".").Replace(" ", "");

                // ── Assembly definitions ────────────────────────────────
                WriteAsmdef(runtimeDir, $"{asmBase}.Runtime", isEditor: false);
                WriteAsmdef(editorDir, $"{asmBase}.Editor", isEditor: true, runtimeRef: $"{asmBase}.Runtime");

                // ── package.json ────────────────────────────────────────
                WritePackageJson(pkgRoot);

                // ── Meta for package.json itself ────────────────────────
                WriteMeta(Path.Combine(pkgRoot, "package.json"), isFolder: false);

                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Done!",
                    $"Package created at:\n{pkgRoot}", "OK");

                // Reveal in explorer / finder
                EditorUtility.RevealInFinder(pkgRoot);
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[PackageCreator] {ex}");
                EditorUtility.DisplayDialog("Error", ex.Message, "OK");
            }
        }

        // ================================================================
        //  File writers
        // ================================================================

        /// <summary>Write a minimal .meta file (GUID derived from path for determinism).</summary>
        private static void WriteMeta(string targetPath, bool isFolder)
        {
            string metaPath = targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".meta";
            if (File.Exists(metaPath)) return;

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

        /// <summary>Write a .asmdef and its .meta.</summary>
        private void WriteAsmdef(string dir, string asmName, bool isEditor, string runtimeRef = null)
        {
            string path = Path.Combine(dir, asmName + ".asmdef");

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"    \"name\": \"{asmName}\",");

            // References
            if (!string.IsNullOrEmpty(runtimeRef))
                sb.AppendLine($"    \"references\": [\"{runtimeRef}\"],");
            else
                sb.AppendLine("    \"references\": [],");

            // Include platforms
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
            WriteMeta(path, isFolder: false);
        }

        /// <summary>Write the package.json manifest.</summary>
        private void WritePackageJson(string pkgRoot)
        {
            string path = Path.Combine(pkgRoot, "package.json");

            // Build keywords array
            var kwList = _keywords
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .ToList();

            // Build dependencies dict
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

            // Optional URLs
            if (!string.IsNullOrWhiteSpace(_documentationUrl))
                sb.AppendLine($"    \"documentationUrl\": \"{Escape(_documentationUrl)}\",");
            if (!string.IsNullOrWhiteSpace(_changelogUrl))
                sb.AppendLine($"    \"changelogUrl\": \"{Escape(_changelogUrl)}\",");
            if (!string.IsNullOrWhiteSpace(_licensesUrl))
                sb.AppendLine($"    \"licensesUrl\": \"{Escape(_licensesUrl)}\",");

            // License
            if (!string.IsNullOrWhiteSpace(_license))
                sb.AppendLine($"    \"license\": \"{Escape(_license)}\",");

            // Keywords
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

            // Author
            bool hasAuthor = !string.IsNullOrWhiteSpace(_authorName);
            if (hasAuthor)
            {
                sb.AppendLine("    \"author\": {");
                sb.AppendLine($"        \"name\": \"{Escape(_authorName)}\"");
                if (!string.IsNullOrWhiteSpace(_authorEmail))
                {
                    // Replace previous line's newline: add comma
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

            // Dependencies
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

        // ================================================================
        //  Helpers
        // ================================================================

        /// <summary>True if any segment of the relative path is named "Editor" (case-insensitive).</summary>
        private static bool IsEditorPath(string relativePath)
        {
            string[] parts = relativePath.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            return parts.Any(p => p.Equals("Editor", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Checks whether the source folder sits underneath an "Editor" directory
        /// by walking from the source path back up to (and including) "Assets".
        /// e.g. Assets/Tools/Editor/SomeFolder → true
        /// </summary>
        private static bool IsSourceUnderEditorAncestor(string sourceFolder)
        {
            string assetsDir = Path.GetFullPath(Application.dataPath); // …/Assets

            // Get the full, normalized source path
            string fullSource = Path.GetFullPath(sourceFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Walk upwards from the source's parent until we reach (or pass) Assets
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

        /// <summary>Remove the first "Editor" directory segment from a relative path.</summary>
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

        /// <summary>Get a path relative to a root directory.</summary>
        private static string GetRelativePath(string root, string full)
        {
            root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                 + Path.DirectorySeparatorChar;

            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return full.Substring(root.Length);

            // Fallback for .NET versions without Path.GetRelativePath
            Uri rootUri = new Uri(root);
            Uri fullUri = new Uri(full);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fullUri).ToString())
                      .Replace('/', Path.DirectorySeparatorChar);
        }

        /// <summary>Deterministic 32-hex-char GUID derived from the path (stable across runs).</summary>
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

        /// <summary>Escape a string for safe embedding in JSON.</summary>
        private static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}