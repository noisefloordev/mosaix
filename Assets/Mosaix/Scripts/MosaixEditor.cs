using UnityEditor;
using UnityEngine;
using System.Collections;
 
[CustomEditor(typeof(Mosaix))]
public class MosaixEditor: Editor
{
    bool ShowShaders = false;
    bool ShowAdvanced = false;
    bool ShowTextures = false;

    int DisplayedTexture = 0;
    bool ShowAlpha = false;

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
            obj.MosaicMaterial = (Material) EditorGUILayout.ObjectField("Mosaic Material", obj.MosaicMaterial, typeof(Material), false);
            obj.ExpandEdgesShader = (Shader) EditorGUILayout.ObjectField("Expand Edges Shader", obj.ExpandEdgesShader, typeof(Shader), false);
            obj.PremultiplyShader = (Shader) EditorGUILayout.ObjectField("Premultiply Shader", obj.PremultiplyShader, typeof(Shader), false);
        }

        ShowAdvanced = EditorGUILayout.Foldout(ShowAdvanced, "Advanced settings", true, boldFoldout);
        if(ShowAdvanced)
        {
            obj.ShadowsCastOnMosaic = EditorGUILayout.Toggle("Shadows Cast On Mosaic", obj.ShadowsCastOnMosaic);
            obj.HighResolutionRender = EditorGUILayout.Toggle("High Resolution Render", obj.HighResolutionRender);
            obj.Alpha = EditorGUILayout.Slider("Alpha", obj.Alpha, 0, 1);
        }

        if(EditorApplication.isPlaying)
        {
            // For development, allow inspecting the various textures used by the shader.
            ShowTextures = EditorGUILayout.Foldout(ShowTextures, "Textures (debugging)", true, boldFoldout);

            if(ShowTextures)
            {
                int NumTextures = obj.OutputTextures != null?  NumTextures = obj.OutputTextures.Length:0;
                DisplayedTexture = EditorGUILayout.IntSlider("Texture", DisplayedTexture, 0, NumTextures-1);

                ShowAlpha = EditorGUILayout.Toggle("Show alpha", ShowAlpha);

                if(obj.OutputTextures != null && obj.OutputTextures.Length != 0)
                {
                    // Wrap this in a BeginHorizontal, so we can use FlexibleSpace to center the texture.
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    
                    // Begin the BeginVertical block that will contain only the texture.
                    Rect r = EditorGUILayout.BeginVertical(new GUILayoutOption[] {
                            GUILayout.MinHeight(obj.OutputTextures[0].height),
                            GUILayout.MinWidth(obj.OutputTextures[0].width),
                    });

                    // There's no EditorGUILayout for drawing textures.  Insert a space, to tell layout
                    // about the space we need for the texture display.
                    GUILayout.Space(obj.OutputTextures[0].height);

                    Texture tex = obj.OutputTextures[DisplayedTexture];

                    // Save the filter mode and switch to nearest neighbor to draw it in the editor.
                    FilterMode SavedFilterMode = tex.filterMode;
                    tex.filterMode = FilterMode.Point;
                    if(ShowAlpha)
                        EditorGUI.DrawTextureAlpha(r, tex, ScaleMode.ScaleToFit);
                    else
                        EditorGUI.DrawPreviewTexture(r, tex, null, ScaleMode.ScaleToFit);
                    tex.filterMode = SavedFilterMode;
                    
                    EditorGUILayout.EndVertical();
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }
            }
        }
    }
}
 
