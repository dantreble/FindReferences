using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine.Audio;
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

    [System.Serializable]
    [Flags]
    public enum FileTypes
    {
        Prefab = 1<<0,
        Unity = 1<<1,
        Asset = 1<<2,
        Mat = 1<<3,
        Controller = 1<<4,
        Meta = 1<<5
    }

    [SerializeField]
    private FileTypes m_fileTypes = FileTypes.Prefab | FileTypes.Unity | FileTypes.Asset | FileTypes.Mat | FileTypes.Controller;


    private Process m_process;
    private string m_outputPath;
    private Vector2 m_scrollPosition;

    [MenuItem("Assets/Find References In Project", false, 2000)]
    static void ShowWindow()
    {
        var window = GetWindow(typeof(FindProjectReferences)) as FindProjectReferences;

        if (window != null)
        {
            window.m_sources = Selection.objects;
            window.SetDefaultDirectory();
            window.SetDefaultFileTypes();
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

    public void SetDefaultFileTypes()
    {
        //This is just a best (most common) guess

        FileTypes fileTypes = 0;

        foreach (var source in m_sources)
        {
            if (source is Texture || source is Shader)
            {
                fileTypes = fileTypes | FileTypes.Mat;
            }
            else if (source is Material)
            {
                fileTypes = fileTypes | FileTypes.Prefab | FileTypes.Unity;
            }
            else if (source is ScriptableObject)
            {
                fileTypes = fileTypes | FileTypes.Prefab | FileTypes.Unity | FileTypes.Asset;
            }
            else if (source is AnimationClip)
            {
                fileTypes = fileTypes | FileTypes.Controller;
            }
            else if (source is Mesh)
            {
                fileTypes = fileTypes | FileTypes.Prefab | FileTypes.Unity;
            }
            else if (source is GameObject)
            {
                fileTypes = fileTypes | FileTypes.Prefab | FileTypes.Unity;
            }
            else if (source is AudioClip)
            {
                fileTypes = fileTypes | FileTypes.Prefab | FileTypes.Unity;
            }
            else if (source is MonoScript)
            {
                fileTypes = fileTypes | FileTypes.Prefab | FileTypes.Unity | FileTypes.Asset; //FileTypes.Controller
            }
            else if (source is Avatar)
            {
                fileTypes = fileTypes | FileTypes.Meta;
            }
            else if (source is AvatarMask)
            {
                fileTypes = fileTypes | FileTypes.Controller;
            }
            else if (source is AnimatorOverrideController)
            {
                fileTypes = fileTypes | FileTypes.Prefab;
            }
            else
            {
                //Debug.Log(source.GetType().Name);
                //Fallback to almost everything

                fileTypes = fileTypes | FileTypes.Prefab | FileTypes.Unity | FileTypes.Asset | FileTypes.Mat |
                            FileTypes.Controller;
            }



        }

        m_fileTypes = fileTypes;

    }

    void OnGUI()
    {
        GUILayout.Label(m_sources != null && m_sources.Length > 1 ? "Find Multiple References" : "Find References", EditorStyles.boldLabel);

        m_directory = EditorGUILayout.ObjectField("Directory : ", m_directory, typeof(DefaultAsset), false, null) as DefaultAsset;

        GUILayout.Label("File Types");

        foreach (FileTypes fileType in Enum.GetValues(typeof(FileTypes)))
        {
            var wasSet = ((int) m_fileTypes & (int) fileType) == (int) fileType;
            var isSet = EditorGUILayout.Toggle(fileType.ToString(),wasSet);

            if (isSet != wasSet)
            {
                if (isSet)
                {
                    m_fileTypes = (FileTypes)((int) m_fileTypes | (int) fileType);
                }
                else
                {
                    m_fileTypes = (FileTypes)((int)m_fileTypes & ~(int)fileType);
                }
            }
        }

        var firstObject = m_sources != null && m_sources.Any() ? m_sources[0] : null;

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

                m_scrollPosition = GUILayout.BeginScrollView(m_scrollPosition);

                foreach (var reference in m_references)
                {
                    GUILayout.BeginHorizontal();

                    //GUILayout.Label(AssetDatabase.GetCachedIcon(reference));

                    //if (GUILayout.Button(Path.GetFileNameWithoutExtension(reference))) //, "Label"
                    //{
                    //    Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(reference);
                    //}

                    EditorGUILayout.ObjectField(ReferenceToObj(reference), typeof(Object), false, null);

                    GUILayout.EndHorizontal();
                }

                GUILayout.EndScrollView();

                if (m_references.Any())
                {
                    if (GUILayout.Button("Select All"))
                    {
                        var objects = new Object[m_references.Count];

                        for (var index = 0; index < m_references.Count; index++)
                        {
                            objects[index] = ReferenceToObj(m_references[index]);
                        }

                        Selection.objects = objects;
                    }

                    m_replacement = EditorGUILayout.ObjectField("Replace : ", m_replacement, typeof(Object), false, null);

                    if (GUILayout.Button("Replace"))
                    {
                        var textReplacements = new List<KeyValuePair<string, string>>(m_sources.Length);

                        var replacementGUID = m_replacement != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m_replacement)) : null;

                        var replaceFileRef = replacementGUID == null
                            ? "0"
                            : string.Format("2800000, guid: {0}, type: 3", replacementGUID);

                        foreach (var source in m_sources)
                        {
                            var sourceAssetPath = AssetDatabase.GetAssetPath(source);
                            var sourceGUID = AssetDatabase.AssetPathToGUID(sourceAssetPath);

                            var sourceFileRef = string.Format("2800000, guid: {0}, type: 3", sourceGUID);

                            textReplacements.Add(new KeyValuePair<string, string>(sourceFileRef, replaceFileRef));

                            if (replacementGUID != null)
                            {
                                textReplacements.Add(new KeyValuePair<string, string>(sourceGUID, replacementGUID));
                            }
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

    private static Object ReferenceToObj(string a_reference)
    {
        Object obj = null;

        if (!a_reference.EndsWith(".meta"))
        {
            obj = AssetDatabase.LoadMainAssetAtPath(a_reference);
        }
        else
        {
            var allAssetsAtPath = AssetDatabase.LoadAllAssetsAtPath(a_reference.Replace(".meta", ""));

            if (allAssetsAtPath.Length > 0)
            {
                obj =
                    allAssetsAtPath[0];
            }
        }
        return obj;
    }

    private void Update()
    {
        if (m_process != null && m_process.HasExited)
        {
            m_process.Close();

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

                var length = fields[0].Length - assets;

                //if (fields[0].EndsWith(".meta", StringComparison.Ordinal))
                //{
                //    length -= 5;
                //}

                var filename = fields[0].Substring(assets, length);
                if(fields.Length > 7)
                {
                    filename = string.Concat(filename, fields[1]);
                }

                a_references.Add(filename);
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

            m_process = CreateProcess(m_sources, path, m_fileTypes, m_outputPath);
            m_process.Start();
        }
    }



#if UNITY_EDITOR_OSX

    public static Process CreateProcess(Object[] a_objects, string a_path, FileTypes a_fileTypes, string a_outputPath)
    {
        // /bin/sh -c "/usr/bin/mdfind  -onlyin /Users/dan/Documents/hof2/Assets '(kMDItemDisplayName==*.unity||kMDItemDisplayName==*.prefab||kMDItemDisplayName==*.asset||kMDItemDisplayName==*.mat)&&(kMDItemTextContent=\"b0f572c827a1f6146a10c19119414b1c\"c)' > foo.foo"
         
        var pathArgment = " -onlyin " + a_path;
        //var fileTypeArgment = "(kMDItemDisplayName==*.unity||kMDItemDisplayName==*.prefab||kMDItemDisplayName==*.asset||kMDItemDisplayName==*.mat||kMDItemDisplayName==*.controller||kMDItemDisplayName==*.meta)";

        var fileTypeArgment = "(";

        foreach (FileTypes fileType in Enum.GetValues(typeof(FileTypes)))
        {
            var isSet = ((int)a_fileTypes & (int)fileType) == (int)fileType;
            if (isSet)
            {
                fileTypeArgment += "kMDItemDisplayName==*." + fileType.ToString().ToLower() + "||";
            }
        }

        fileTypeArgment = fileTypeArgment.Remove(fileTypeArgment.LastIndexOf("||"));

        fileTypeArgment += ")";


        var searchArgment = "(";

        for (var index = 0; index < a_objects.Length; index++)
        {
            var activeObject = a_objects[index];
            var assetPath = AssetDatabase.GetAssetPath(activeObject);
            var guid = AssetDatabase.AssetPathToGUID(assetPath);

            //This is faster, as it is case insensitive, but I can't get it to work
            //var guidArgment = "kMDItemTextContent=\\\""+ guid + "\\\"c";

            var guidArgment = "kMDItemTextContent="+ guid;

            searchArgment += index > 0 ? "||" + guidArgment : guidArgment;

            var resourcesIndex = assetPath.IndexOf(ResourcesDir, StringComparison.OrdinalIgnoreCase);
            if (resourcesIndex > 0)
            {
                var resourcesPath = Path.ChangeExtension(assetPath.Substring(resourcesIndex + ResourcesDir.Length), null);
                searchArgment += "||kMDItemTextContent=\\\"" + resourcesPath+ "\\\"";
            }

            //Asset bundle refs have the guid in anyway
            //var assetImporter = AssetImporter.GetAtPath(assetPath);
            //if (assetImporter != null && !string.IsNullOrEmpty(assetImporter.assetBundleName))
            //{
            //    var assetbundlepath = assetPath.Remove(0, 7);
            //    searchArgment += " OR " + AssetBundleManager.BundleAndAssetToRef(assetImporter.assetBundleName, assetPath, guid);
            //}
        }

        searchArgment += ")";


        var processStartInfo = new ProcessStartInfo {FileName = "/bin/sh"};
        processStartInfo.Arguments = " -c \"/usr/bin/mdfind " + pathArgment + " '" + fileTypeArgment + "&&" + searchArgment +"' > " + a_outputPath + "\"";
       
        return new Process { StartInfo = processStartInfo };
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

    public static Process CreateProcess(Object[] a_objects, string a_path, FileTypes a_fileTypes, string a_outputPath)
    {
        var agantRansackPath = AgentRansackPath();

        if (string.IsNullOrEmpty(agantRansackPath) || !File.Exists(agantRansackPath))
        {
            Debug.LogError("Please install Agent Ransack https://www.mythicsoft.com/agentransack");
            return null;
        }

        var processStartInfo = new ProcessStartInfo {FileName = agantRansackPath};

        var pathArgment = " -d \"" + a_path.Replace("/", "\\") + "\" ";
        //var fileTypeArgment = " -f \"*.unity;*.prefab;*.asset;*.mat;*.controller;*.meta\" ";

        var fileTypeArgment = " -f \"";

        foreach (FileTypes fileType in Enum.GetValues(typeof (FileTypes)))
        {
            var isSet = ((int)a_fileTypes & (int) fileType) == (int) fileType;
            if (isSet)
            {
                fileTypeArgment += "*." + fileType.ToString().ToLower() + ";";
            }
        }

        fileTypeArgment.Remove(fileTypeArgment.LastIndexOf(";"));

        fileTypeArgment += "\" ";

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
