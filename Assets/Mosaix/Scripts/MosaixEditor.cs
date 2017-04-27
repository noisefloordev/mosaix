using UnityEditor;
using UnityEngine;
using System.Collections;
 
[CustomEditor(typeof(Mosaix))]
public class MosaixEditor: Editor
{
    public override void OnInspectorGUI()
    {
        Mosaix obj = (Mosaix) target;
        
        EditorGUILayout.LabelField("Basic settings", EditorStyles.boldLabel);
        obj.MosaicLayer = EditorGUILayout.LayerField("Layer Name", obj.MosaicLayer);
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

        EditorGUILayout.LabelField("Advanced settings", EditorStyles.boldLabel);
        obj.ShadowsCastOnMosaic = EditorGUILayout.Toggle("Shadows Cast On Mosaic", obj.ShadowsCastOnMosaic);
        obj.HighResolutionRender = EditorGUILayout.Toggle("High Resolution Render", obj.HighResolutionRender);
        obj.Alpha = EditorGUILayout.Slider("Alpha", obj.Alpha, 0, 1);
    }
}
 
