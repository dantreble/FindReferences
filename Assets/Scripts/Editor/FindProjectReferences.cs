using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public class FindProjectReferences : EditorWindow
{
    private const string ResourcesDir = "Resources/";
    private Object[] m_sources;

    [SerializeField]
    private Object m_replacement;
    private List<string> m_references;

    [SerializeField]
    private DefaultAsset m_directory;

    private Process m_process;
    private string m_outputPath;

    [MenuItem("Assets/Find References In Project", false, 2000)]
    static void ShowWindow()
    {
        var window = GetWindow(typeof(FindProjectReferences)) as FindProjectReferences;

        if (window != null)
        {
            window.m_sources = Selection.objects;
            window.SetDefaultDirectory();
            window.StartProcess();
        }
    }

    public void SetDefaultDirectory()
    {
        if (!m_directory || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(m_directory)))
        {
            m_directory = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets");
        }
    }

    void OnGUI()
    {
        GUILayout.Label(m_sources.Length > 1 ? "Find Multiple References" : "Find References", EditorStyles.boldLabel);

        m_directory = EditorGUILayout.ObjectField("Directory : ", m_directory, typeof(DefaultAsset), false, null) as DefaultAsset;

        var firstObject = m_sources.Any() ? m_sources[0] : null;

        var newFirstObject = EditorGUILayout.ObjectField("Find : ", firstObject, typeof(Object), false, null);

        if (newFirstObject != firstObject)
        {
            m_sources = new[] {newFirstObject};
        }
                

        if (m_process != null && !m_process.HasExited)
        {
            //Show spinner
            GUILayout.Label("Searching...");
        }
        else
        {
            if (GUILayout.Button("Find"))
            {
                StartProcess();
            }

            if (m_references != null)
            {
                GUILayout.Label("Found " + m_references.Count + " references", EditorStyles.boldLabel);

                foreach (var reference in m_references)
                {
                    GUILayout.BeginHorizontal();

                    //GUILayout.Label(AssetDatabase.GetCachedIcon(reference));

                    //if (GUILayout.Button(Path.GetFileNameWithoutExtension(reference))) //, "Label"
                    //{
                    //    Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(reference);
                    //}

                    EditorGUILayout.ObjectField(AssetDatabase.LoadMainAssetAtPath(reference), typeof(Object), false, null);

                    GUILayout.EndHorizontal();
                }

                if (m_references.Any())
                {
                    m_replacement = EditorGUILayout.ObjectField("Replace : ", m_replacement, typeof(Object), false, null);

                    if (GUILayout.Button("Replace"))
                    {
                        var textReplacements = new List<KeyValuePair<string, string>>(m_sources.Length);

                        var replacementAssetPath = AssetDatabase.GetAssetPath(m_replacement);
                        var replacementGUID = AssetDatabase.AssetPathToGUID(replacementAssetPath);

                        var replacementResourcesIndex = replacementAssetPath.IndexOf(ResourcesDir, StringComparison.OrdinalIgnoreCase);


                        foreach (var source in m_sources)
                        {
                            var sourceAssetPath = AssetDatabase.GetAssetPath(source);
                            var sourceGUID = AssetDatabase.AssetPathToGUID(sourceAssetPath);

                            textReplacements.Add(new KeyValuePair<string, string>(sourceGUID, replacementGUID));

                            var sourceResourcesIndex = sourceAssetPath.IndexOf(ResourcesDir, StringComparison.OrdinalIgnoreCase);

                            if (sourceResourcesIndex >= 0 && replacementResourcesIndex >= 0)
                            {
                                var sourceResourcesPath = Path.ChangeExtension(sourceAssetPath.Substring(sourceResourcesIndex + ResourcesDir.Length), null);
                                var replacementResourcesPath = Path.ChangeExtension(replacementAssetPath.Substring(replacementResourcesIndex + ResourcesDir.Length), null);

                                textReplacements.Add(new KeyValuePair<string, string>(sourceResourcesPath,
                                    replacementResourcesPath));
                            }

                            //var assetImporter = AssetImporter.GetAtPath(sourceAssetPath);
                            //if (assetImporter != null && !string.IsNullOrEmpty(assetImporter.assetBundleName))
                            //{
                            //    var assetbundlepath = assetPath.Remove(0, 7);
                            //    searchArgment += " OR " + assetbundlepath;
                            //}
                        }

                        foreach (var reference in m_references)
                        {
                            var text = File.ReadAllText(reference);

                            foreach (var textReplacement in textReplacements)
                            {
                                text = Regex.Replace(text, string.Format(@"\b{0}\b", textReplacement.Key), textReplacement.Value);
                            }
                            
                            File.WriteAllText(reference, text);
                        }
                    }
                }
            }
        }
    }

    private void Update()
    {
        if (m_process != null && m_process.HasExited)
        {
            m_process = null;

            m_references = ParseResults(m_references, m_outputPath);

            Repaint();
        }
    }

    public static List<string> ParseResults(List<string> a_references, string a_outputPath)
    {
        a_references = new List<string>();

        using (
            var outputFile =
                new StreamReader(File.Open(a_outputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
        {
            while (true)
            {
                var logLine = outputFile.ReadLine();

                if (logLine == null)
                {
                    break;
                }

                var fields = logLine.Split('\t');

                if (fields.Length <= 0)
                {
                    continue;
                }

                var assets = fields[0].IndexOf("Assets", StringComparison.Ordinal);

                if (assets < 0)
                {
                    continue;
                }

                a_references.Add(fields[0].Substring(assets));
            }
        }

        return a_references;
    }

    private void StartProcess()
    {
        if (m_process == null)
        {
            m_references = null;
            m_outputPath = Path.GetFullPath(FileUtil.GetUniqueTempPathInProject());

            var directoryPath = AssetDatabase.GetAssetPath(m_directory);

            var path = string.IsNullOrEmpty(directoryPath) ? Application.dataPath : Path.GetFullPath(directoryPath);

            m_process = CreateProcess(m_sources, path, m_outputPath);
            m_process.Start();
        }
    }



#if UNITY_EDITOR_OSX

    public static Process CreateProcess(Object[] a_objects, string a_path, string a_outputPath)
    {
        return null;
    }

    public static List<string> FindReferences(UnityEngine.Object a_objects)
    {
        var appDataPath = Application.dataPath;

        var selectedAssetPath = AssetDatabase.GetAssetPath(a_objects);
        var references = new List<string>();

        var guid = AssetDatabase.AssetPathToGUID(selectedAssetPath);

        var psi = new System.Diagnostics.ProcessStartInfo();
        psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Maximized;
        psi.FileName = "/usr/bin/mdfind";
        psi.Arguments = "-onlyin " + appDataPath + " " + guid;
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;

        System.Diagnostics.Process process = new System.Diagnostics.Process {StartInfo = psi};

        process.OutputDataReceived += (sender, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
            {
                return;
            }

            string relativePath = "Assets" + e.Data.Replace(appDataPath, "");

            // we don't care about meta files.
            if (relativePath == selectedAssetPath + ".meta")
            {
                return;
            }

            references.Add(relativePath);
        };
        //process.ErrorDataReceived += (sender, e) =>
        //{
        //    if (string.IsNullOrEmpty(e.Data))
        //    {
        //        return;
        //    }

        //    output += "Error: " + e.Data + "\n";
        //};
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        process.WaitForExit(2000);
        return references;
    }

#else

    private static string AgentRansackPath()
    {
        //HKEY_CURRENT_USER\Software\Microsoft\IntelliPoint\AppSpecific\AgentRansack.exe Path
        //HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\AgentRansack.EXE (Default)

        var path = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\AgentRansack.EXE",
            null, null) as string;

        return path;
    }

    public static Process CreateProcess(Object[] a_objects, string a_path, string a_outputPath)
    {
        var agantRansackPath = AgentRansackPath();

        if (string.IsNullOrEmpty(agantRansackPath) || !File.Exists(agantRansackPath))
        {
            Debug.LogError("Please install Agent Ransack https://www.mythicsoft.com/agentransack");
            return null;
        }

        var processStartInfo = new ProcessStartInfo {FileName = agantRansackPath};

        var pathArgment = " -d \"" + a_path.Replace("/", "\\") + "\" ";
        var fileTypeArgment = " -f \"*.unity;*.prefab;*.asset;*.mat\" ";

        var searchArgment = " -ceb -cm -c \"";

        for (var index = 0; index < a_objects.Length; index++)
        {
            var activeObject = a_objects[index];
            var assetPath = AssetDatabase.GetAssetPath(activeObject);
            var guid = AssetDatabase.AssetPathToGUID(assetPath);

            searchArgment += index > 0 ? " OR " + guid : guid;

            var resourcesIndex = assetPath.IndexOf(ResourcesDir, StringComparison.OrdinalIgnoreCase);
            if (resourcesIndex > 0)
            {
                var resourcesPath = Path.ChangeExtension(assetPath.Substring(resourcesIndex + ResourcesDir.Length), null);
                searchArgment += " OR " + resourcesPath;
            }

            //Asset bundle refs have the guid in anyway
            //var assetImporter = AssetImporter.GetAtPath(assetPath);
            //if (assetImporter != null && !string.IsNullOrEmpty(assetImporter.assetBundleName))
            //{
            //    var assetbundlepath = assetPath.Remove(0, 7);
            //    searchArgment += " OR " + AssetBundleManager.BundleAndAssetToRef(assetImporter.assetBundleName, assetPath, guid);
            //}
        }

        searchArgment += " \" ";

        var outputFileArgment = "-ofb -o \"" + a_outputPath.Replace("/", "\\") + "\" ";

        processStartInfo.Arguments = pathArgment + fileTypeArgment + searchArgment + outputFileArgment;

        return new Process { StartInfo = processStartInfo };
    }

#endif


   

   
}
