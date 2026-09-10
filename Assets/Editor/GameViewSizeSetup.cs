using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Forces the Game view to a fixed 1920x1080. A free-aspect Game view lands on odd pixel
/// dimensions, which makes the Pixel Perfect Camera warn and pick a fractional zoom, so the
/// art shimmers while scrolling. Pinning the resolution makes what you see in the editor match
/// what a player gets in a build.
/// </summary>
public static class GameViewSizeSetup
{
    private const string SizeName = "2DGame 1920x1080";

    [MenuItem("Tools/2D Game/4. Set Game View to 1920x1080")]
    public static void Apply()
    {
        try
        {
            Assembly editorAsm = typeof(Editor).Assembly;
            Type sizesType = editorAsm.GetType("UnityEditor.GameViewSizes");
            Type sizeType = editorAsm.GetType("UnityEditor.GameViewSize");
            Type sizeTypeEnum = editorAsm.GetType("UnityEditor.GameViewSizeType");
            Type groupType = editorAsm.GetType("UnityEditor.GameViewSizeGroup");
            if (sizesType == null || sizeType == null || groupType == null)
            {
                Debug.LogWarning("[GameViewSizeSetup] Editor internals not found on this version.");
                return;
            }

            object instance = typeof(ScriptableSingleton<>)
                .MakeGenericType(sizesType)
                .GetProperty("instance", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null, null);

            object group = sizesType
                .GetMethod("GetGroup")
                .Invoke(instance, new object[] { (int)GameViewSizeGroupType.Standalone });

            int index = FindSize(groupType, group, SizeName);
            if (index < 0)
            {
                ConstructorInfo ctor = sizeType.GetConstructor(
                    new[] { sizeTypeEnum, typeof(int), typeof(int), typeof(string) });
                object size = ctor.Invoke(new object[]
                {
                    Enum.Parse(sizeTypeEnum, "FixedResolution"), 1920, 1080, SizeName
                });
                groupType.GetMethod("AddCustomSize").Invoke(group, new[] { size });
                index = FindSize(groupType, group, SizeName);
            }
            if (index < 0) { Debug.LogWarning("[GameViewSizeSetup] Could not register the size."); return; }

            Type gameViewType = editorAsm.GetType("UnityEditor.GameView");
            EditorWindow gv = EditorWindow.GetWindow(gameViewType, false, null, false);
            PropertyInfo prop = gameViewType.GetProperty("selectedSizeIndex",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            prop.SetValue(gv, index, null);
            gv.Repaint();

            Debug.Log("[GameViewSizeSetup] Game view pinned to 1920x1080 (index " + index + ").");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GameViewSizeSetup] Failed: " + e.Message);
        }
    }

    private static int FindSize(Type groupType, object group, string name)
    {
        int total = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
        MethodInfo getSize = groupType.GetMethod("GetGameViewSize");
        for (int i = 0; i < total; i++)
        {
            object s = getSize.Invoke(group, new object[] { i });
            string n = (string)s.GetType().GetProperty("baseText").GetValue(s, null);
            if (n == name) return i;
        }
        return -1;
    }
}
