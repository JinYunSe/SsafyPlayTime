#if(UNITY_EDITOR)
using UnityEngine;
using UnityEditor;
using System.IO;
#if (UNITY_2021_2_OR_NEWER)
// prefab stage out of experimental.
using UnityEditor.SceneManagement;
#elif (UNITY_2018_3_OR_NEWER)
// prefabstage is still experimental
// but what it inherits from, PreviewSceneStage/Stage is not experimental as of 2020.1
using UnityEditor.Experimental.SceneManagement;
#endif
namespace ECE
{
  public static class EasyColliderSaving
  {
    public class PrefabData
    {
      public UnityEngine.Object FoundObject;
      public string AssetPath;
    }

    static PrefabData TryFindPrefabObject(GameObject go)
    {
      UnityEngine.Object foundObject = null;
      PrefabData foundData = new PrefabData();
#if UNITY_2018_2_OR_NEWER
      // 2018_2+
      PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
      if (stage != null)
      {
        if (stage.prefabContentsRoot != null)
        {
          foundObject = PrefabUtility.GetCorrespondingObjectFromSource(stage.prefabContentsRoot);
        }
#if (UNITY_2020_1_OR_NEWER)
        // asset path in 2020.1+
        foundData.AssetPath = stage.assetPath;
#elif (UNITY_2018_3_OR_NEWER)
        // prefab asset path 2018.3 to 2019.4
        foundData.AssetPath = stage.prefabAssetPath;
#endif
      }

      // try using just the GO if were not in a prefab stage.
      if (foundObject == null)
      {
        foundObject = PrefabUtility.GetCorrespondingObjectFromSource(go);
        if (foundObject == null)
        {
          foundObject = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
        }
      }

#else
      // legacy unity support. -- (I dont test in earlier than 2019.4+ anymore)
      foundObject = PrefabUtility.GetPrefabParent(go);
      if (foundObject == null)
      {
        foundObject = PrefabUtility.FindPrefabRoot(go);
      }
#endif
      foundData.FoundObject = foundObject;
      return foundData;
    }

    static PrefabData TryFindMeshOrSkinnedMeshObject(GameObject go)
    {
      PrefabData foundPrefabData = new PrefabData();
      MeshFilter mf = go.GetComponent<MeshFilter>();
      if (mf != null)
      {
        foundPrefabData.FoundObject = mf.sharedMesh;
      }
      else
      {
        SkinnedMeshRenderer smr = go.GetComponent<SkinnedMeshRenderer>();
        if (smr != null)
        {
          foundPrefabData.FoundObject = smr.sharedMesh;
        }
      }
      return foundPrefabData;
    }

    /// <summary>
    /// Static preferences asset that is currently loaded.
    /// </summary>
    /// <value></value>
    static EasyColliderPreferences ECEPreferences { get { return EasyColliderPreferences.Preferences; } }


    /// <summary>
    /// removes invalid characters (so path is valid) and trailing slashes (for compatibility with older versions of unity)
    /// </summary>
    /// <param name="path"></param>
    /// <returns>Assets/Folder1/Folder</returns>
    static string CleanAssetPath(string path)
    {
      //remove any invalid path characters.
      path = string.Join("", path.Split(Path.GetInvalidPathChars()));
      // no trailing slash is more compatible with older version of unity and AssetDatabase.IsValidFolder, so prefer no trailing slash when checking.
      // folder path is saved with / at the end, so remove if we have to.
      if (path[path.Length - 1] == '/')
      {
        path = path.Remove(path.Length - 1);
      }
      return path;
    }


    /// <summary>
    /// Gets a valid path to save a convex hull at to feed into save convex hull meshes function.
    /// </summary>
    /// <param name="go">selected gameobject</param>
    /// <param name="ECEPreferences">preferences object</param>
    /// <returns>assetdb path like: Assets/Folder/OptionSubfolder/baseObjectsName</returns>
    public static string GetValidConvexHullPath(GameObject go)
    {
      // use default specified path
      // remove invalid characters from file name, just in case (user reported error, thanks!)
      string goName = string.Join("_", go.name.Split(Path.GetInvalidFileNameChars()));
      // start with the default path specified in preferences.
      string path = ECEPreferences.SaveConvexHullPath;
      // get path to gameobject
      if (ECEPreferences.ConvexHullSaveMethod != CONVEX_HULL_SAVE_METHOD.Folder)
      {
        // bandaid for scaled temporary skinned mesh:
        // as the scaled mesh filter is added during setup with the name Scaled Mesh Filter (Temporary)
        if (go.name.Contains("Scaled Mesh Filter"))
        {
          go = go.transform.parent.gameObject; // set the gameobject to the temp's parent (as that will be a part of the prefab if it is one and thus should work.)
        }
        PrefabData foundPrefabData = null;

        if (ECEPreferences.ConvexHullSaveMethod == CONVEX_HULL_SAVE_METHOD.Prefab)
        {
          foundPrefabData = TryFindPrefabObject(go);
        }
        else if (ECEPreferences.ConvexHullSaveMethod == CONVEX_HULL_SAVE_METHOD.Mesh)
        {
          foundPrefabData = TryFindMeshOrSkinnedMeshObject(go);
        }
        else if (ECEPreferences.ConvexHullSaveMethod == CONVEX_HULL_SAVE_METHOD.PrefabMesh)
        {
          foundPrefabData = TryFindPrefabObject(go);
          if (foundPrefabData.FoundObject == null && string.IsNullOrEmpty(foundPrefabData.AssetPath))
          {
            foundPrefabData = TryFindMeshOrSkinnedMeshObject(go);
          }
        }
        else if (ECEPreferences.ConvexHullSaveMethod == CONVEX_HULL_SAVE_METHOD.MeshPrefab)
        {
          foundPrefabData = TryFindMeshOrSkinnedMeshObject(go);
          if (foundPrefabData.FoundObject == null)
          {
            foundPrefabData = TryFindPrefabObject(go);
          }
        }
        // by default, use the found AssetPath, this should be the outermost prefab which is what is wanted.
        string pathToPrefabOrMesh = foundPrefabData.AssetPath;
        if (string.IsNullOrEmpty(pathToPrefabOrMesh) && foundPrefabData.FoundObject != null)
        {
          pathToPrefabOrMesh = AssetDatabase.GetAssetPath(foundPrefabData.FoundObject);
        }
        // but only use the path it if it exists.
        // here we trim the object name just down to the last / so Asset/FolderWithPrefabInIt/
        if (!string.IsNullOrEmpty(pathToPrefabOrMesh))
        {
          int index = pathToPrefabOrMesh.LastIndexOf("/");
          if (index >= 0)
          {
            // removes object name.
            path = pathToPrefabOrMesh.Remove(index + 1);
          }
        }
      }
      string originalPath = path;
      path = CleanAssetPath(path);

      // AssetDatabase.IsValidFolder("Assets/"); // not valid in 2019
      // AssetDatabase.IsValidFolder("Assets"); // valid in 2019 and 6000. Prefer this one!


      // prefab/mesh searched for a folder to save in and failed to find a valid path, default to the save convex hull path specified in preferences.
      if ((!AssetDatabase.IsValidFolder(path) && path + "/" != ECEPreferences.SaveConvexHullPath)
        // saving in a non-asset path and does not have allow saving convex hulls in packages enabled
        || (!path.StartsWith("Assets") && !ECEPreferences.AllowSavingConvexHullsInPackages))
      {
        path = ECEPreferences.SaveConvexHullPath;
        path = CleanAssetPath(path);
        // could not find a valid mesh/prefab location, but the fallback save convex hull folder DOES work.
        if (AssetDatabase.IsValidFolder(path))
        {
          Debug.LogWarning("Easy Collider Editor: Could not find a valid location to save the collider. Saving in the folder specified in preferences: " + path);
        }
      }

      // path and path fallback both are clean and have no trailing / here.

      // this will automatically be true if we're using a folder (and it failed) OR the fallback on prefab/mesh fails (which also uses SaveConvexHullPath)
      if (!AssetDatabase.IsValidFolder(path) && (path + "/").Contains(ECEPreferences.SaveConvexHullPath))
      {
        // path to ece preferences (in scripts typically)
        path = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(ECEPreferences));
        // because the above is the full path to the file including the files name.
        path = path.Remove(path.LastIndexOf("/"));
        path = path.Replace("/Scripts", "");
        path = string.Join("", path.Split(Path.GetInvalidPathChars()));
        //  asset path for preferences file is invalid, not in assets folder a
        if (path.StartsWith("Assets") || ECEPreferences.AllowSavingConvexHullsInPackages)
        {
          if (!AssetDatabase.IsValidFolder(path + "/Convex Hulls"))
          {
            AssetDatabase.CreateFolder(path, "Convex Hulls");
            AssetDatabase.Refresh();
          }
          path = path + "/Convex Hulls";
          if (originalPath == ECEPreferences.SaveConvexHullPath)
          {
            // original path was saving to a folder.
            ECEPreferences.SaveConvexHullPath = path + "/";
            Debug.LogWarning("Easy Collider Editor: Convex Hull save path specified in Easy Collider Editor's preferences could not be found, or it is an invalid asset folder. Saving in: " + path + " as a fallback and updating preferences. If the folder has been moved or deleted, update to a different folder in the edit preferences foldout.\n\n Original path: " + originalPath);
          }
          // so that if we already have a valid 
          else if (!ECEPreferences.SaveConvexHullPath.StartsWith("Assets"))
          {
            // used an alternative mesh/prefab/prefabmesh save method.
            ECEPreferences.SaveConvexHullPath = path + "/";
            Debug.LogWarning("Easy Collider Editor: Could not find a valid location to save the collider and the save path specified in preferences could not be found, or it is an invalid asset folder. Saving in: " + path + " and updated preferences to this folder. If the folder has been moved or deleted, update to a different folder in the edit preferences foldout.\n\n Original path: " + originalPath);
          }
        }
        else // in a packages folder probably without AllowSavingConvexHullsInPackages enabled.
        {
          // final fallback.
          path = "Assets/Convex Hulls";
          if (originalPath == ECEPreferences.SaveConvexHullPath)
          {
            // change default path to a valid path.
            ECEPreferences.SaveConvexHullPath = path + "/";
#if (UNITY_2020_3_OR_NEWER)
            AssetDatabase.SaveAssetIfDirty(ECEPreferences);
#endif
          }
          if (!AssetDatabase.IsValidFolder(path))
          {
            AssetDatabase.CreateFolder("Assets", "Convex Hulls");
            AssetDatabase.Refresh();
            if (originalPath == ECEPreferences.SaveConvexHullPath)
            {
              // original path was saving to a folder.
              Debug.LogWarning("Easy Collider Editor: Convex Hull save path specified in Easy Collider Editor's preferences could not be found, or it is an invalid asset folder. A folder to save collider assets was created at: " + path + " and automatically set in preferences. A different folder in Easy Collider Editor's preferences foldout can be specified if desired.\n\n Original path: " + originalPath);
            }
            else
            {
              // used an alternative mesh/prefab/prefabmesh save method.
              Debug.LogWarning("Easy Collider Editor: Could not find a valid location to save the collider and the save path specified in preferences could not be found, or it is an invalid asset folder. A new folder has been created at " + path + " to save mesh collider assets and automatically set in preferences.  A different folder in Easy Collider Editor's preferences foldout can be specified if desired. \n\n Original path: " + originalPath);
            }
          }
        }
      }

      // if they want a subfolder, create it if needed and use it.
      if (!string.IsNullOrEmpty(ECEPreferences.SaveConvexHullSubFolder))
      {
        if (!AssetDatabase.IsValidFolder(path + "/" + ECEPreferences.SaveConvexHullSubFolder))
        {
          AssetDatabase.CreateFolder(path, ECEPreferences.SaveConvexHullSubFolder);
        }
        path += "/" + ECEPreferences.SaveConvexHullSubFolder + "/";
        path = CleanAssetPath(path);
      }
      string fullPath = path + "/" + goName;
      return fullPath;
    }

    /// <summary>
    /// goes thorugh the path and finds the first non-existing path that can be used to save.
    /// </summary>
    /// <param name="path">Full path up to save location: ie C:/UnityProjects/ProjectName/Assets/Folder/baseObject</param>
    /// <param name="suffix">Suffix to add to save files ie _Suffix_</param>
    /// <returns>first valid path for AssetDatabase.CreateAsset ie baseObject_Suffix_0</returns>
    private static string GetFirstValidAssetPath(string path, string suffix)
    {

      string validPath = path;
      if (File.Exists(validPath + suffix + "0.asset"))
      {
        int i = 1;
        while (File.Exists(validPath + suffix + i + ".asset"))
        {
          i += 1;
        }
        validPath += suffix + i + ".asset";
      }
      else
      {
        validPath += suffix + "0.asset";
      }

      // replace application's data path  (Unity Editor: <path to project folder>/Assets) 
      // "Assets" earlier in the path should no longer cause issues.
      validPath = validPath.Replace(Application.dataPath, "Assets");
      return validPath;
    }

    /// <summary>
    /// Creates and saves a mesh asset in the asset database with appropriate path and suffix.
    /// </summary>
    /// <param name="mesh">mesh</param>
    /// <param name="attachTo">gameobject the mesh will be attached to, used to find asset path.</param>
    public static void CreateAndSaveMeshAsset(Mesh mesh, GameObject attachTo)
    {
      if (mesh != null && !DoesMeshAssetExists(mesh))
      {
        string savePath = GetValidConvexHullPath(attachTo);
        if (savePath != "")
        {
          string assetPath = GetFirstValidAssetPath(savePath, ECEPreferences.SaveConvexHullSuffix);
          AssetDatabase.CreateAsset(mesh, assetPath);
          AssetDatabase.SaveAssets();
        }
      }
    }

    /// <summary>
    /// Checks if the asset already exists (needed for rotate and duplicate, as the mesh is the same mesh repeated.)
    /// </summary>
    /// <param name="mesh"></param>
    /// <returns></returns>
    public static bool DoesMeshAssetExists(Mesh mesh)
    {
      string p = AssetDatabase.GetAssetPath(mesh);
      if (p == null || p.Length == 0)
      {
        return false;
      }
      return true;
    }



    /// <summary>
    /// Creates and saves an array of mesh assets in the assetdatabase at the path with the the format "savePath"+"suffix"+[0-n].asset
    /// </summary>
    /// <param name="savePath">Full path up to save location: ie C:/UnityProjects/ProjectName/Assets/Folder/baseObject</param>
    /// <param name="suffix">Suffix to add to save files ie _Suffix_</param>
    public static Mesh[] CreateAndSaveMeshAssets(Mesh[] meshes, string savePath, string suffix)
    {
      string firstAssetPath = null;
      int assetSuffixIndex = -1;
      for (int i = 0; i < meshes.Length; i++)
      {
        // get a new valid path for each mesh to save.
        string path = GetFirstValidAssetPath(savePath, suffix);
        try
        {
          if (ECEPreferences.CombinedVHACDColliders && firstAssetPath != null)
          {
            //adding a name to the mesh even though it isn't required to match the first assets name as it by default has the path's name.
            string name = firstAssetPath.Remove(assetSuffixIndex, firstAssetPath.Length - assetSuffixIndex);
            name = name.Remove(0, name.LastIndexOf("/") + 1);
            meshes[i].name = name + suffix + i.ToString();
            AssetDatabase.AddObjectToAsset(meshes[i], firstAssetPath);
          }
          else
          {
            AssetDatabase.CreateAsset(meshes[i], path);
          }
        }
        catch (System.Exception error)
        {
          Debug.LogError("Error saving at path:" + path + ". Try changing the save Convex Hull path in Easy Collider Editor's preferences to a different folder.\n" + error.ToString());
        }
        if (firstAssetPath == null)
        {
          firstAssetPath = path;
          assetSuffixIndex = firstAssetPath.IndexOf(suffix);
        }
      }
      AssetDatabase.SaveAssets();
      if (ECEPreferences.CombinedVHACDColliders)
      {
        // need to reload the assets and update the meshes array otherwise they don't point to the correct object
        // only the first one will without this as for whatever reason create asset will correctly link the objects but adding an object to an asset will not.
        var assets = AssetDatabase.LoadAllAssetsAtPath(firstAssetPath);
        for (int i = 0; i < assets.Length; i++)
        {
          meshes[i] = (Mesh)assets[i];
        }
      }
      return meshes;
    }

  }
}
#endif
