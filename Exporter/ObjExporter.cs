using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.UIElements;
using UnityEngine.SceneManagement;

namespace FlowScapeExporterMod;
public static class ObjExporter
{
	static Vector3 RotateAroundPoint(Vector3 point, Vector3 pivot, Quaternion angle) => angle * (point - pivot) + pivot;
	static Vector3 MultiplyVec3s(Vector3 v1, Vector3 v2) => new(v1.x * v2.x, v1.y * v2.y, v1.z * v2.z);

	static string SafeString(this string s) => s.Replace(" ", "_");
	static bool Has<T>(this GameObject go) => go.GetComponent<T>() != null;

	public static void Export(Scene scene, string exportPath)
	{
		Dictionary<string, bool> materialCache = [];
		var exportDir = Path.GetDirectoryName(exportPath);
		var baseFileName = Path.GetFileNameWithoutExtension(exportPath);

		var parent = new GameObject(Guid.NewGuid().ToString());
		var rootObjs = scene.GetRootGameObjects();
		if (rootObjs.Length > 1)
			foreach (var ro in rootObjs)
				ro.transform.SetParent(parent.transform);

		List<MeshFilter> sceneMeshes = [];
		foreach (var f in parent.GetComponentsInChildren<MeshFilter>())
		{
			if (f == null) continue;

			var go = f.gameObject;

			if (LayerMask.LayerToName(go.layer).ToUpper() is "UI" or "GUI" or "MENUI" or "MENU" or "CURSOR" or "FLARES" or "FLARE" or "BACKGROUND") continue;
			if (!go.activeInHierarchy || !go.activeSelf) continue;
			if (go.Has<Skybox>() || go.Has<VisualElement>() || go.Has<GUIElement>() || go.Has<Camera>() || go.Has<Light>()) continue;

			var mr = go.GetComponent<MeshRenderer>();
			var smr = go.GetComponent<SkinnedMeshRenderer>();
			if ((mr == null || !mr.enabled || !mr.isVisible) && (smr == null || !smr.enabled || !smr.isVisible)) continue;

			sceneMeshes.Add(f);
		}

		StringBuilder sb = new();
		StringBuilder sbMaterials = new();
		sb.AppendLine($"mtllib {baseFileName}.mtl");
		int lastIndex = 0;
		for (int i = 0; i < sceneMeshes.Count; i++)
		{
			string meshName = sceneMeshes[i].gameObject.name.SafeString();
			MeshFilter mf = sceneMeshes[i];
			MeshRenderer mr = sceneMeshes[i].gameObject.GetComponent<MeshRenderer>();

			sb.AppendLine($"g {meshName}_{i}");
			if (mr != null)
			{
				var mats = mr.sharedMaterials;
				for (int j = 0; j < mats.Length; j++)
				{
					Material m = mats[j];
					var matName = m.name.SafeString();
					if (!materialCache.ContainsKey(matName))
					{
						materialCache[matName] = true;
						sbMaterials.Append(MaterialToString(m, exportDir));
						sbMaterials.AppendLine();
					}
				}
			}

			Mesh mesh = mf.sharedMesh;
			var faceOrder = (int)Mathf.Clamp(mf.gameObject.transform.lossyScale.x * mf.gameObject.transform.lossyScale.z, -1, 1);

			foreach (Vector3 vx in mesh.vertices)
			{
				Vector3 v = vx;
				v = MultiplyVec3s(v, mf.gameObject.transform.lossyScale);
				v = RotateAroundPoint(v, Vector3.zero, mf.gameObject.transform.rotation);
				v += mf.gameObject.transform.position;
				v.x *= -1;
				sb.AppendLine($"v {v.x} {v.y} {v.z}");
			}
			foreach (Vector3 vx in mesh.normals)
			{
				Vector3 v = vx;
				v = MultiplyVec3s(v, mf.gameObject.transform.lossyScale.normalized);
				v = RotateAroundPoint(v, Vector3.zero, mf.gameObject.transform.rotation);
				v.x *= -1;
				sb.AppendLine($"vn {v.x} {v.y} {v.z}");

			}
			foreach (Vector2 v in mesh.uv)
			{
				sb.AppendLine($"vt {v.x} {v.y}");
			}

			for (int j = 0; j < mesh.subMeshCount; j++)
			{
				if (mr != null && j < mr.sharedMaterials.Length)
					sb.AppendLine($"usemtl {mr.sharedMaterials[j].name.SafeString()}");
				else
					sb.AppendLine($"usemtl {meshName}_sm{j}");

				var tris = mesh.GetTriangles(j);
				for (int t = 0; t < tris.Length; t += 3)
				{
					var idx2 = tris[t] + 1 + lastIndex;
					var idx1 = tris[t + 1] + 1 + lastIndex;
					var idx0 = tris[t + 2] + 1 + lastIndex;

					if (faceOrder < 0)
						sb.AppendLine($"f {ObjTriple(idx2)} {ObjTriple(idx1)} {ObjTriple(idx0)}");
					else
						sb.AppendLine($"f {ObjTriple(idx0)} {ObjTriple(idx1)} {ObjTriple(idx2)}");
				}
			}

			lastIndex += mesh.vertices.Length;
		}

		File.WriteAllText($@"{exportDir}/{baseFileName}.obj", sb.ToString());
		File.WriteAllText($@"{exportDir}/{baseFileName}.mtl", sbMaterials.ToString());
	}

	static bool TryExportMaterialTexture(string propertyName, Material m, string exportPath, out string savePath)
	{
		savePath = exportPath;
		if (m.HasProperty(propertyName))
		{
			Texture t = m.GetTexture(propertyName);
			if (t != null)
			{
				return TryExportTexture((Texture2D)t, ref savePath);
			}
		}
		return false;
	}

	static Texture2D AsReadableTexture(this Texture2D src)
	{
		RenderTexture renderTex = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
		Graphics.Blit(src, renderTex);
		RenderTexture previous = RenderTexture.active;
		RenderTexture.active = renderTex;
		Texture2D readableTex = new(src.width, src.height);
		readableTex.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
		readableTex.Apply();
		RenderTexture.active = previous;
		RenderTexture.ReleaseTemporary(renderTex);
		return readableTex;
	}

	static bool TryExportTexture(Texture2D t, ref string exportPath)
	{
		try
		{
			var basePath = exportPath;
			exportPath += @"/textures/";
			if (!Directory.Exists(exportPath))
				Directory.CreateDirectory(exportPath);
			exportPath += $"{t.name.SafeString()}.png";

			File.WriteAllBytes(exportPath, t.AsReadableTexture().EncodeToPNG());
			exportPath = exportPath.Replace(basePath, @"./").Replace(@"//", @"/");
			return true;
		}
		catch (Exception ex)
		{
			Debug.Log($"Couldn't save texture '{t.name}': {ex.Message}");
			return false;
		}
	}

	static string ObjTriple(int index) => $"{index}/{index}/{index}";

	static string MaterialToString(Material m, string exportDir)
	{
		StringBuilder sb = new();

		sb.AppendLine($"newmtl {m.name.SafeString()}");

		if (m.HasProperty("_Color"))
		{
			sb.AppendLine($"Kd {m.color.r} {m.color.g} {m.color.b}");
			if (m.color.a < 1.0f)
			{
				sb.AppendLine($"Tr {1f - m.color.a}");
				sb.AppendLine($"d {m.color.a}");
			}
		}
		if (m.HasProperty("_SpecColor"))
		{
			Color sc = m.GetColor("_SpecColor");
			sb.AppendLine($"Ks {sc.r} {sc.g} {sc.b}");
		}

		if (TryExportMaterialTexture("_MainTex", m, exportDir, out var diffuse))
			sb.AppendLine($"map_Kd {diffuse}");

		if (TryExportMaterialTexture("_SpecMap", m, exportDir, out var spec))
			sb.AppendLine($"map_Ks {spec}");


		if (TryExportMaterialTexture("_BumpMap", m, exportDir, out var bump))
			sb.AppendLine($"map_Bump {bump}");

		sb.AppendLine("illum 2");

		return sb.ToString();
	}
}
