using UnityEditor;
using UnityEngine;
using System.Collections;
 
[CustomEditor(typeof(Mosaix))]
public class MosaixEditor: Editor
{
    bool ShowShaders = false;
    bool ShowAdvanced = false;
    public override void OnInspectorGUI()
    {
        Mosaix obj = (Mosaix) target;
        
        EditorGUILayout.LabelField("Basic settings", EditorStyles.boldLabel);
        obj.MosaicLayer = EditorGUILayout.LayerField("Mosaic Layer", obj.MosaicLayer);
        obj.MosaicBlocks = EditorGUILayout.IntSlider("Mosaic Blocks", obj.MosaicBlocks, 1, 100);
        
        EditorGUILayout.LabelField("Masking", EditorStyles.boldLabel);
        obj.MaskingMode = (Mosaix.MaskMode) EditorGUILayout.EnumPopup("Masking Mode", obj.MaskingMode);

        if(obj.MaskingMode == Mosaix.MaskMode.Texture)
        {
            obj.MaskingTexture = (Texture) EditorGUILayout.ObjectField("Masking Texture", obj.MaskingTexture, typeof(Texture), true);
        }
        else if(obj.MaskingMode == Mosaix.MaskMode.Sphere)
        {
            obj.MaskingSphere = (GameObject) EditorGUILayout.ObjectField("Masking Sphere", obj.MaskingSphere, typeof(GameObject), true);
            obj.MaskFade = EditorGUILayout.Slider("Mask Fade", obj.MaskFade, 0, 1);
        }

        GUIStyle boldFoldout = new GUIStyle(EditorStyles.foldout);
        boldFoldout.fontStyle = FontStyle.Bold;
        ShowShaders = EditorGUILayout.Foldout(ShowShaders, "Shaders", true, boldFoldout);

        if(ShowShaders)
        {
            obj.ExpandEdgesShader = (Shader) EditorGUILayout.ObjectField("Expand Edges Shader", obj.ExpandEdgesShader, typeof(Shader), false);
            obj.MosaicShader = (Shader) EditorGUILayout.ObjectField("Mosaic Shader", obj.MosaicShader, typeof(Shader), false);
            obj.PremultiplyShader = (Shader) EditorGUILayout.ObjectField("Premultiply Shader", obj.PremultiplyShader, typeof(Shader), false);
        }

        ShowAdvanced = EditorGUILayout.Foldout(ShowAdvanced, "Advanced settings", true, boldFoldout);
        if(ShowAdvanced)
        {
            obj.ShadowsCastOnMosaic = EditorGUILayout.Toggle("Shadows Cast On Mosaic", obj.ShadowsCastOnMosaic);
            obj.HighResolutionRender = EditorGUILayout.Toggle("High Resolution Render", obj.HighResolutionRender);
            obj.Alpha = EditorGUILayout.Slider("Alpha", obj.Alpha, 0, 1);
        }
    }
}
 
