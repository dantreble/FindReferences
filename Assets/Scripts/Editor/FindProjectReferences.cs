using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class FindProject : ScriptableWizard
{
    public UnityEngine.Object m_source;
    public UnityEngine.Object m_replacement;

    [MenuItem("Assets/Replace References In Project", false, 2000)]
    static void CreateWizard()
    {
        var wizard = DisplayWizard<FindProject>("Replace Project References", "Replace");

        wizard.m_source = Selection.activeObject;
    }

    void OnWizardCreate()
    {
        if (m_source == null || m_replacement == null)
        {
            Debug.LogError("Source and replacement need to be set");
            return;
        }

        var references = FindReferences(m_source);

        if (references == null)
        {
            return;
        }

        var sourceGUID = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m_source));
        var replacementGUID = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m_replacement));

        foreach (var reference in references)
        {
            var text = System.IO.File.ReadAllText(reference);
            text = text.Replace(sourceGUID, replacementGUID);
            System.IO.File.WriteAllText(reference, text);
        }

        Debug.Log("Replaced " + references.Count + " references for " + m_source.name);
    }

    [MenuItem("Assets/Find References In Project", false, 2000)]
    private static void FindProjectReferences()
    {
        var references = FindReferences(Selection.activeObject);

        if (references == null)
        {
            return;
        }

        var selectedAssetName = Selection.activeObject.name;

        foreach (var reference in references)
        {
            Debug.Log(reference + " references " + selectedAssetName, AssetDatabase.LoadMainAssetAtPath(reference));
        }

        Debug.Log("Found " + references.Count + " references for " + selectedAssetName, Selection.activeObject);
    }


#if UNITY_EDITOR_OSX

    public static List<string> FindReferences(UnityEngine.Object a_activeObject)
    {
        var appDataPath = Application.dataPath;

        var selectedAssetPath = AssetDatabase.GetAssetPath(a_activeObject);
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

    public static List<string> FindReferences(UnityEngine.Object a_activeObject)
    {
        var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(a_activeObject));

        var agantRansackPath = AgentRansackPath();

        if (string.IsNullOrEmpty(agantRansackPath) || !File.Exists(agantRansackPath))
        {
            Debug.LogError("Please install Agent Ransack https://www.mythicsoft.com/agentransack");
            return null;
        }

        var processStartInfo = new System.Diagnostics.ProcessStartInfo { FileName = agantRansackPath };

        var uniqueTempPathInProject = Path.GetFullPath(FileUtil.GetUniqueTempPathInProject());

        var pathArgment = " -d \"" + Application.dataPath.Replace("/", "\\") + "\" ";
        var fileTypeArgment = " -f \"*.unity;*.prefab;*.asset;*.mat\" ";
        var searchArgment = " -ceb -cm -c \"" + guid + "\" ";
        var outputFileArgment = "-ofb -o \"" + uniqueTempPathInProject.Replace("/", "\\") + "\" ";

        processStartInfo.Arguments = pathArgment + fileTypeArgment + searchArgment + outputFileArgment;

        var process = new System.Diagnostics.Process { StartInfo = processStartInfo };

        process.Start();

        process.WaitForExit();

        var references = new List<string>();

        using (
            var outputFile =
                new StreamReader(File.Open(uniqueTempPathInProject, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
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

                references.Add(fields[0].Substring(assets));
            }
        }
        return references;
    }

#endif


   

   
}
