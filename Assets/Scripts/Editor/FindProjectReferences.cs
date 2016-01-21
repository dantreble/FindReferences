using System;
using UnityEngine;
using System.Collections;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

public class FindProject
{
    
#if UNITY_EDITOR_OSX
	
	[MenuItem("Assets/Find References In Project", false, 2000)]
	private static void FindProjectReferences()
	{
		string appDataPath = Application.dataPath;
		string output = "";
		string selectedAssetPath = AssetDatabase.GetAssetPath (Selection.activeObject);
		List<string> references = new List<string>();
		
		string guid = AssetDatabase.AssetPathToGUID (selectedAssetPath);
		
		var psi = new System.Diagnostics.ProcessStartInfo();
		psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Maximized;
		psi.FileName = "/usr/bin/mdfind";
		psi.Arguments = "-onlyin " + Application.dataPath + " " + guid;
		psi.UseShellExecute = false;
		psi.RedirectStandardOutput = true;
		psi.RedirectStandardError = true;
		
		System.Diagnostics.Process process = new System.Diagnostics.Process();
		process.StartInfo = psi;
		
		process.OutputDataReceived += (sender, e) => {
			if(string.IsNullOrEmpty(e.Data))
				return;
			
			string relativePath = "Assets" + e.Data.Replace(appDataPath, "");
			
			// we don't care about meta files.
			if(relativePath == selectedAssetPath + ".meta")
				return;
			
			references.Add(relativePath);
			
		};
		process.ErrorDataReceived += (sender, e) => {
			if(string.IsNullOrEmpty(e.Data))
				return;
			
			output += "Error: " + e.Data + "\n";
		};
		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		
		process.WaitForExit(2000);
		
		foreach(var file in references){
			output += file + "\n";
			Debug.Log(file, AssetDatabase.LoadMainAssetAtPath(file));
		}
		
		Debug.LogWarning(references.Count + " references found for object " + Selection.activeObject.name + "\n\n" + output);
	}
	
#else

    private static string AgentRansackPath()
    {
        //HKEY_CURRENT_USER\Software\Microsoft\IntelliPoint\AppSpecific\AgentRansack.exe Path
        //HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\AgentRansack.EXE (Default)

        var path = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\AgentRansack.EXE",
            null, null) as string;

        return path;
    }

    [MenuItem("Assets/Find References In Project", false, 2000)]
	private static void FindProjectReferences()
	{
		var appDataPath = Application.dataPath;
		var selectedAssetPath = AssetDatabase.GetAssetPath (Selection.activeObject);

        var selectedAssetName = Selection.activeObject.name;
		
		var guid = AssetDatabase.AssetPathToGUID (selectedAssetPath);

	    var agantRansackPath = AgentRansackPath();

	    if (string.IsNullOrEmpty(agantRansackPath) || !File.Exists(agantRansackPath))
	    {
            Debug.LogError("Please install Agent Ransack https://www.mythicsoft.com/agentransack");
	        return;
	    }

	    var processStartInfo = new System.Diagnostics.ProcessStartInfo {FileName = agantRansackPath};

	    var uniqueTempPathInProject = Path.GetFullPath(FileUtil.GetUniqueTempPathInProject());

        var pathArgment = " -d \"" + appDataPath.Replace("/", "\\") + "\" ";
	    var fileTypeArgment = " -f \"*.unity;*.prefab;*.asset;*.mat\" ";
	    var searchArgment = " -ceb -cm -c \"" + guid + "\" ";
        var outputFileArgment = "-ofb -o \"" + uniqueTempPathInProject.Replace("/", "\\") + "\" ";

        processStartInfo.Arguments = pathArgment + fileTypeArgment + searchArgment + outputFileArgment;

	    var process = new System.Diagnostics.Process {StartInfo = processStartInfo};

	    process.Start();

        process.WaitForExit();

	    var referencesFound = 0;

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

                var file = fields[0].Substring(assets);

                Debug.Log(file + " references " + selectedAssetName, AssetDatabase.LoadMainAssetAtPath(file));

                referencesFound++;
            }
        }

        Debug.Log("Found " + referencesFound + " references for " + selectedAssetName +  " in " + process.TotalProcessorTime , Selection.activeObject);
	}

#endif
   
}