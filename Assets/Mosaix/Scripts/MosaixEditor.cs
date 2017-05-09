// This isn't in an Editor directory due to Unity's broken compile order handling, which
// makes it impossible to access Editor scripts from non-Editor scripts.  Instead, we just
// conditionally compile this.
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[CustomEditor(typeof(Mosaix))]
public class MosaixEditor: Editor
{
    static Material TextureDisplayMaterial;

    public enum TextureDisplayMode
    {
        Normal,
        ShowAlphaOnly,
        ShowWithoutAlpha,
        ShowUnpremultiplied,
    };

    // These are transient settings that affect the inspector only.  We store these on the Mosaix object
    // itself so they're preserved when the object is deselected and reselected, but they're Nonserialized
    // so they aren't saved with the scene, which would cause annoying noise in version control.
    public class EditorSettings
    {
        public bool ShowShaders = false;
        public bool ShowMasking = false;
        public bool ShowAnchoring = false;
        public bool ShowAdvanced = false;
        public bool ShowDebugging = false;

        public int DisplayedTexture = 0;
        public bool ScaleTexture = true;
        public TextureDisplayMode DisplayMode = TextureDisplayMode.Normal;
    };

    public override void OnInspectorGUI()
    {
        if(TextureDisplayMaterial == null)
            TextureDisplayMaterial = new Material(Shader.Find("Hidden/Mosaix/EditorTextureDisplay"));

        Mosaix obj = (Mosaix) target;
        Undo.RecordObject(obj, "Mosaix change");
        
        EditorGUILayout.LabelField("Basic settings", EditorStyles.boldLabel);
        obj.MosaicLayer = EditorGUILayout.LayerField(new GUIContent("Mosaic Layer",
                    "Select the display layer to be mosaiced.\n" +
                    "\n" +
                    "Objects in the selected layer will be mosaiced."), obj.MosaicLayer);
        obj.MosaicBlocks = EditorGUILayout.Slider(new GUIContent("Mosaic Blocks",
                    "Set the number of blocks the mosaic should have.\n" +
                    "\n" +
                    "If Anchor Scaling is enabled, this will be scaled by the distance from the " +
                    "camera to the anchor."), obj.MosaicBlocks, 2, 500);
        
        obj.EditorSettings.ShowMasking = Foldout(obj.EditorSettings.ShowMasking, "Masking");
        if(obj.EditorSettings.ShowMasking)
        {
            obj.TextureMasking = EditorGUILayout.ToggleLeft(new GUIContent("Texture masking",
                        "Enable to turn on texture masking.\n" +
                        "\n" +
                        "Select a masking texture.  The objects will be mosaiced where the texture is white."), obj.TextureMasking);
            if(obj.TextureMasking)
            {
                ++EditorGUI.indentLevel;
                obj.MaskingTexture = (Texture) EditorGUILayout.ObjectField("Masking Texture", obj.MaskingTexture, typeof(Texture), true);
                --EditorGUI.indentLevel;
            }

            obj.SphereMasking = EditorGUILayout.ToggleLeft(new GUIContent("Sphere masking",
                        "Enable to turn on sphere masking.  Objects will be mosaiced only within a sphere."), obj.SphereMasking);
            if(obj.SphereMasking)
            {
                ++EditorGUI.indentLevel;
                obj.MaskingSphere = (GameObject) EditorGUILayout.ObjectField(new GUIContent("Masking Sphere",
                            "Connect a sphere mesh.  Objects sphere will be mosaiced only inside the sphere.\n" +
                            "\n" +
                            "The sphere mesh may be scaled on each axis separately to mosaic within an oblong area."),
                        obj.MaskingSphere, typeof(GameObject), true);
                obj.MaskFade = EditorGUILayout.Slider(new GUIContent("Mask Fade",
                            "The amount to fade the mosaic out around the sphere.\n" +
                            "\n" +
                            "At 0, the mosaic will cut off sharply at the edge of the sphere.\n" +
                            "At 1, the mosaic will fade off for the diameter of the sphere."), obj.MaskFade, 0, 1);
                --EditorGUI.indentLevel;
            }
        }

        obj.EditorSettings.ShowAnchoring = Foldout(obj.EditorSettings.ShowAnchoring, "Anchoring");
        if(obj.EditorSettings.ShowAnchoring)
        {
            ++EditorGUI.indentLevel;
            obj.AnchorTransform = (GameObject) EditorGUILayout.ObjectField("Anchor", obj.AnchorTransform, typeof(GameObject), true);
            obj.FollowAnchor = EditorGUILayout.Toggle(new GUIContent("Follow Anchor",
                        "If enabled, the mosaic blocks will align themselves to the anchor as it moves."),
                        obj.FollowAnchor);

            obj.ScaleMosaicToAnchorDistance = EditorGUILayout.Toggle(new GUIContent("Scale mosaic size",
                    "If enabled, the mosaic will get bigger as the anchor gets closer to the camera.\n" +
                    "\n" +
                    "If the anchor is further than 1 unit from the camera, the mosaic will use smaller " +
                    "blocks, and if it's closer than 1 unit it'll use bigger blocks."),
                    obj.ScaleMosaicToAnchorDistance);

            --EditorGUI.indentLevel;
        }

        obj.EditorSettings.ShowAdvanced = Foldout(obj.EditorSettings.ShowAdvanced, "Advanced settings");
        if(obj.EditorSettings.ShowAdvanced)
        {
            ++EditorGUI.indentLevel;
            obj.ShadowsCastOnMosaic = EditorGUILayout.Toggle(new GUIContent("Shadows Cast On Mosaic",
                        "If on (recommended), non-mosaiced objects will cast shadows on mosaiced objects.\n" +
                        "\n" +
                        "If off, non-mosaiced objects will be hidden with display layers while rendering " +
                        "the objects, and won't cast shadowed on them."), obj.ShadowsCastOnMosaic);
            obj.HighResolutionRender = EditorGUILayout.Toggle(new GUIContent("High Resolution Render",
                        "If on, objects are rendered normally and then downscaled to create the mosaic.\n" +
                        "If off, objects are rendered at the mosaic resolution.\n" +
                        "\n" +
                        "If this is off, alpha and masking won't have a high resolution texture to use," +
                        "and will mask to a blurry texture instead.  Lighting may also be lower quality."),
                    obj.HighResolutionRender);
            obj.Alpha = EditorGUILayout.Slider(new GUIContent("Alpha",
                        "Fade out the mosaic."),
                    obj.Alpha, 0, 1);
            obj.RenderScale = EditorGUILayout.Slider(new GUIContent("Render Scale",
                        "The amount to render outside of the visible area.\n" +
                        "\n" +
                        "At 0, only the viewport is rendered.  At 0.5, 50% extra screen space is rendered." +
                        "A value of 0.1 is recommended.  This gives the mosaic a little extra screen space to " +
                        "sample, which reduces flicker when objects are near the edge of the screen."),
                    obj.RenderScale, 1, 2);
            --EditorGUI.indentLevel;
        }

        obj.EditorSettings.ShowShaders = Foldout(obj.EditorSettings.ShowShaders, "Shaders");

        if(obj.EditorSettings.ShowShaders)
        {
            ++EditorGUI.indentLevel;
            obj.MosaicMaterial = (Material) EditorGUILayout.ObjectField("Mosaic Material", obj.MosaicMaterial, typeof(Material), false);
            obj.ExpandEdgesShader = (Shader) EditorGUILayout.ObjectField("Expand Edges Shader", obj.ExpandEdgesShader, typeof(Shader), false);
            obj.BlitShader = (Shader) EditorGUILayout.ObjectField("Blit Shader", obj.BlitShader, typeof(Shader), false);
            --EditorGUI.indentLevel;
        }

        if(EditorApplication.isPlaying)
        {
            // For development, allow inspecting the various textures used by the shader.
            obj.EditorSettings.ShowDebugging = Foldout(obj.EditorSettings.ShowDebugging, "Debugging");

            if(obj.EditorSettings.ShowDebugging)
            {
                ++EditorGUI.indentLevel;

                obj.ShowMask = EditorGUILayout.ToggleLeft(new GUIContent("Show mask",
                            "Enable to show the texture mask.\n" +
                            "\n" +
                            "This renders the mask in the viewport for debugging."),
                        obj.ShowMask);

                obj.ExpandPasses = EditorGUILayout.IntSlider("Expand Passes", obj.ExpandPasses, 0, 5);

                List<RenderTexture> TexturePasses = obj.GetTexturePasses();
                
                int NumTextures = TexturePasses.Count;
                obj.EditorSettings.DisplayedTexture = EditorGUILayout.IntSlider("Texture", obj.EditorSettings.DisplayedTexture, 0, NumTextures-1);

                obj.EditorSettings.ScaleTexture = EditorGUILayout.Toggle("Scale texture", obj.EditorSettings.ScaleTexture);
                obj.EditorSettings.DisplayMode = (TextureDisplayMode) EditorGUILayout.EnumPopup("Display Mode", obj.EditorSettings.DisplayMode);

                if(TexturePasses.Count != 0)
                {
                    // Wrap this in a BeginHorizontal, so we can use FlexibleSpace to center the texture.
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    
                    // Wrap in a BeginVertical.  This makes the space for the texture stay the same.  If ScaleTexture
                    // is turned off, we want to make the texture display smaller, but not take up less space in the
                    // UI or everything will move around constantly when changing textures.
                    EditorGUILayout.BeginVertical(new GUILayoutOption[] {
                            GUILayout.MinHeight(TexturePasses[0].height),
                            GUILayout.MinWidth(TexturePasses[0].width),
                    });

                    int ScaleTextureIndex = obj.EditorSettings.ScaleTexture? 0:obj.EditorSettings.DisplayedTexture;
                    // Begin the BeginVertical block that will contain only the texture.
                    Rect r = EditorGUILayout.BeginVertical(new GUILayoutOption[] {
                            GUILayout.MinHeight(TexturePasses[ScaleTextureIndex].height),
                            GUILayout.MinWidth(TexturePasses[ScaleTextureIndex].width),
                    });

                    // There's no EditorGUILayout for drawing textures.  Insert a space, to tell layout
                    // about the space we need for the texture display.
                    GUILayout.Space(TexturePasses[ScaleTextureIndex].height);

                    Texture tex = TexturePasses[obj.EditorSettings.DisplayedTexture];

                    // Save the filter mode and switch to nearest neighbor to draw it in the editor.
                    FilterMode SavedFilterMode = tex.filterMode;
                    tex.filterMode = FilterMode.Point;

                    // Note that we don't use DrawTextureAlpha or the default DrawPreviewTexture material.
                    // These seem to perform some kind of unwanted color management to the texture and don't
                    // show us what's really in it (eg. 1.0 alpha outputs as 0.8).  We just use our own
                    // material for all display modes.
                    Material mat = TextureDisplayMaterial;
                    foreach(string keyword in mat.shaderKeywords)
                        mat.DisableKeyword(keyword);
                    switch(obj.EditorSettings.DisplayMode)
                    {
                    case TextureDisplayMode.Normal:
                        mat.EnableKeyword("DISP_NORMAL");
                        break;
                    case TextureDisplayMode.ShowAlphaOnly:
                        mat.EnableKeyword("DISP_ALPHA_ONLY");
                        break;
                    case TextureDisplayMode.ShowWithoutAlpha:
                        mat.EnableKeyword("DISP_WITHOUT_ALPHA");
                        break;
                    case TextureDisplayMode.ShowUnpremultiplied:
                        mat.EnableKeyword("DISP_UNPREMULTIPLY");
                        break;
                    }
                    
                    EditorGUI.DrawPreviewTexture(r, tex, mat);
                    tex.filterMode = SavedFilterMode;
                    
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.EndVertical();
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }
                --EditorGUI.indentLevel;
            }
        }
    }

    static GUIStyle boldFoldout;

    static bool Foldout(bool val, string name)
    {
        if(boldFoldout == null)
        {
            boldFoldout = new GUIStyle(EditorStyles.foldout);
            boldFoldout.fontStyle = FontStyle.Bold;
        }

#if UNITY_5_5_OR_NEWER
        // This version adds the "toggleOnLabelClick", to make foldouts behave the way they always
        // should have.  I don't know why they make people jump hoops like this instead of just making
        // them always behave correctly.
        return EditorGUILayout.Foldout(val, name, true, boldFoldout);
#else
        return EditorGUILayout.Foldout(val, name, boldFoldout);
#endif
    }
}
#endif

