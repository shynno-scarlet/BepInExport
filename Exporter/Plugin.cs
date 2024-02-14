using BepInEx;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlowScapeExporterMod;

[BepInPlugin("Exporter", "Exporter", "1.0.0.0")]
public class Plugin : BaseUnityPlugin
{
	private void OnGUI()
	{
		if (GUI.Button(new Rect(20, Screen.height - 55, 300, 35), "Save OBJ"))
			SaveOBJ();
	}

	private void SaveOBJ()
	{
		var dir = @"./export";
		var path = @$"{dir}/save.obj";

		if (!Directory.Exists(dir))
			Directory.CreateDirectory(dir);

		var scene = SceneManager.GetActiveScene();

		try
		{
			ObjExporter.Export(scene, path);
			Logger.LogInfo($"Saved GameObject '{scene.name}' to '{path}'");
		} catch (Exception ex)
		{
			Logger.LogError(ex.Message);
			Logger.LogError(ex.StackTrace);
		}
	}
}
