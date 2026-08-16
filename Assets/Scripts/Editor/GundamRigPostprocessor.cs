using UnityEditor;
using UnityEngine;

/// <summary>
/// Forces the Origin Gundam FBX to import as a HUMANOID with an explicit
/// MMD-bone -> human-bone mapping, set through the C# import API.
///
/// Why a postprocessor: hand-writing the mapping into the .meta made the native
/// importer demand a full skeleton pose list (the "Transform ... not found in
/// HumanDescription" error). Through this API, an empty skeleton list is legal -
/// Unity fills the pose from the model file itself.
///
/// Runs automatically on every (re)import of the Gundam. To trigger it:
/// right-click the Gundam FBX -> Reimport.
/// </summary>
public class GundamRigPostprocessor : AssetPostprocessor
{
    private void OnPreprocessModel()
    {
        if (!assetPath.Contains("the-origingundamthe-origin-ver")) return;
        if (!assetPath.ToLowerInvariant().EndsWith(".fbx")) return;

        ModelImporter mi = (ModelImporter)assetImporter;

        string[,] map =
        {
            { "センター",  "Hips" },
            { "上半身",    "Spine" },
            { "上半身2",   "Chest" },
            { "首",        "Neck" },
            { "頭",        "Head" },
            { "肩.L",      "LeftShoulder" },
            { "腕.L",      "LeftUpperArm" },
            { "ひじ.L",    "LeftLowerArm" },
            { "手首.L",    "LeftHand" },
            { "肩.R",      "RightShoulder" },
            { "腕.R",      "RightUpperArm" },
            { "ひじ.R",    "RightLowerArm" },
            { "手首.R",    "RightHand" },
            { "足.L",      "LeftUpperLeg" },
            { "ひざ.L",    "LeftLowerLeg" },
            { "足首.L",    "LeftFoot" },
            { "足.R",      "RightUpperLeg" },
            { "ひざ.R",    "RightLowerLeg" },
            { "足首.R",    "RightFoot" },
        };

        int n = map.GetLength(0);
        HumanBone[] human = new HumanBone[n];
        for (int i = 0; i < n; i++)
        {
            HumanBone hb = new HumanBone
            {
                boneName = map[i, 0],
                humanName = map[i, 1],
            };
            HumanLimit limit = new HumanLimit { useDefaultValues = true };
            hb.limit = limit;
            human[i] = hb;
        }

        // If the T-pose bake exists (Tools > Gundam > 4. Bake Arm T-Pose Into Rig),
        // use it as the reference skeleton - it fixes the arms-behind-the-body
        // problem caused by the model's straight-down zero-bend bind pose.
        SkeletonBone[] skeleton = new SkeletonBone[0];
        string json = "Assets/Model/the-origingundamthe-origin-ver/gundam_tpose.json";
        if (System.IO.File.Exists(json))
        {
            GundamSetup.BonePoseList store = JsonUtility.FromJson<GundamSetup.BonePoseList>(System.IO.File.ReadAllText(json));
            if (store != null && store.bones != null && store.bones.Count > 0)
            {
                skeleton = new SkeletonBone[store.bones.Count];
                for (int i = 0; i < store.bones.Count; i++)
                {
                    skeleton[i] = new SkeletonBone
                    {
                        name = store.bones[i].n,
                        position = store.bones[i].p,
                        rotation = store.bones[i].r,
                        scale = store.bones[i].s,
                    };
                }
            }
        }

        HumanDescription hd = new HumanDescription
        {
            human = human,
            skeleton = skeleton, // empty = use the pose from the file
            upperArmTwist = 0.5f,
            lowerArmTwist = 0.5f,
            upperLegTwist = 0.5f,
            lowerLegTwist = 0.5f,
            armStretch = 0.05f,
            legStretch = 0.05f,
            feetSpacing = 0f,
            hasTranslationDoF = false,
        };

        mi.humanDescription = hd;
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

        // The FBX ships with a broken animation take (NaN / empty curves - the
        // "IsFinite(curve.GetKey...)" console assertions). We only need the MESH
        // and the RIG from this file; all gameplay clips come from the Mixamo FBXs.
        mi.importAnimation = false;

        Debug.Log("[GundamRig] Applied humanoid mapping (19 bones) to " + assetPath);
    }
}
