using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;

// Apply a mosaic to objects in a display layer.
//
// This renders the object to a texture, and then replaces the material on the object with
// one that samples the prerendered texture.  This works better with MSAA, since the object
// is being drawn normally (just with a different texture sampler) instead of comping the
// mosaic on top.
//
// This could also be made to replace a specific material on the objects, to apply a mosaic
// to a specific material but leave the rest of the mesh alone.
public class Mosaix: MonoBehaviour
{
    // The render layer for objects to mosaic.
    public int MosaicLayer;

    // The minimum number of mosaic blocks we want.  This will be adjusted upwards to keep the
    // blocks square.
    public int MosaicBlocks = 16;
    
    // If the expand pass doesn't fill the layer, this is the fallback color that will be visible.
    public Color DefaultColor = new Color(0,0,0,1);

    // If true, we'll render the mosaic texture with other objects casting shadows.  Turning this off may
    // be faster, but will result in the mosaic having no shadowing, which can cause it to be too bright
    // depending on the scene.
    public bool ShadowsCastOnMosaic = true;

    // If true, we'll render the mosaiced objects at full resolution, and then downscale that
    // texture to the low resolution mosaic texture.  This has a couple benefits:
    // 
    // - Lighting matches much better at high resolution.  Lowering the resolution loses shadow
    // details, which can make the mosaic texture not mesh as well.
    // - We have a full resolution texture, which allows us to mask the mosaic.
    //
    // If false, we'll render directly at the lower resolution, which is faster.
    public bool HighResolutionRender = true;

    public float Alpha = 1;

    public enum MaskMode
    {
        None,
        Sphere,
        Texture,
    };

    public MaskMode MaskingMode;

    // MaskMode.Sphere only:

    // A sphere to define where to mosaic.  This sphere can be scaled, stretched and rotated
    // to adjust the shape of the mosaic.
    public GameObject MaskingSphere;

    // How far to fade the mosaic out around MaskingSphere.  At 0, the mosaic cuts off sharply.
    // At 1, we fade for the same size as MaskingSphere: if it's 10 world units, the fade ends
    // 20 world units away.
    public float MaskFade = 0.1f;

    // MaskMode.Texture only:
    public Texture MaskingTexture;

    // This shader copies the outermost edge of opaque pixels outwards.
    public Shader ExpandEdgesShader;

    // This material calls MosaicShader, which samples our low-resolution mosaic texture in screen space.
    // It can also be another material that calls the mosaic pass.
    public Material MosaicMaterial;

    // The premultiply shader takes a regular texture and convert it to a premultiplied one.  This
    // allows for much higher-quality downscaling, without bleeding black from empty pixels.
    public Shader PremultiplyShader;

    // The real camera on the object this script is attached to.
    private Camera ThisCamera;

    // The camera we use to draw the isolated texture.  This is done with a separate camera that matches the
    // target camera, so we don't accidentally change settings on the main camera without putting them back.
    // A matching camera to this one:
    private Camera MosaicCamera;

    public RenderTexture LowResolutionTexture1;
    public RenderTexture LowResolutionTexture2;

    // If HighResolutionRender is true, this is an array of textures, starting at the full window
    // size and incrementing towards the mosaic size.  The final texture will be one step larger
    // than LowResolutionTexture.
    public RenderTexture[] HighResolutionTextures;

    private Material ExpandEdgesMaterial;
    private Material PremultiplyMaterial;

    private Dictionary<Renderer,Material[]> SavedMaterials = new Dictionary<Renderer,Material[]>();

    void Reset()
    {
        // When the script is added in the editor, set the default shaders and mosaic material.
        ExpandEdgesShader = Shader.Find("Hidden/Mosaix/ExpandEdges");
        PremultiplyShader = Shader.Find("Hidden/Mosaix/Premultiply");
        MosaicMaterial = (Material) AssetDatabase.LoadAssetAtPath("Assets/Mosaix/Shaders/Mosaic.mat", typeof(Material));
    }

    static HashSet<Mosaix> EnabledMosaixScripts = new HashSet<Mosaix>();
    void OnEnable()
    {
        EnabledMosaixScripts.Add(this);

        // If more than one of these scripts is attached to the same layer on the same camera, our
        // material replacement in OnPreRender and OnPostRender won't behave correctly.  This is
        // because if multiple scripts have PreRender and PostRenders, Unity calls them in linear
        // order instead of as a stack.  That is, instead of
        //
        // Script1.PreRender Script2.PreRender Script2.PostRender Script1.PostRender
        //
        // it'll call 
        // Script1.PreRender Script2.PreRender Script1.PostRender Script2.PostRender
        //
        // This makes it hard to push and pop render state properly.
        //
        // This probably isn't too useful and if it's happened it's probably unintentional.  Warn about
        // it, since it can cause confusing results.
        foreach(Mosaix script in EnabledMosaixScripts)
        {
            if(script == this || MosaicLayer != script.MosaicLayer || ThisCamera != script.ThisCamera ||
                MosaicLayer != script.MosaicLayer)
                continue;

            Debug.Log("Warning: Multiple Mosaix scripts have been applied to the same display layer " +
                    LayerMask.LayerToName(MosaicLayer) + " on camera " + ThisCamera + ".  This may not work.");
        }
    }

    void OnDisable()
    {
        EnabledMosaixScripts.Remove(this);
    }

    void Start()
    {
        ThisCamera = gameObject.GetComponent<Camera>();
        if(ThisCamera == null)
        {
            Debug.LogError("This script must be attached to a camera.");
            return;
        }

        // Create materials for our shaders.
        ExpandEdgesMaterial = new Material(ExpandEdgesShader);
        PremultiplyMaterial = new Material(PremultiplyShader);

        // Make a copy of the mosaic material.  We may be connected to a material used by multiple
        // instances of this script, and we need the properties we set to not affect the others.
        MosaicMaterial = new Material(MosaicMaterial);

        // Dynamically create a camera to render with.  We'll make this a child of this camera to keep
        // it from cluttering the scene, but this doesn't really matter.
        GameObject MosaicCameraGameObject = new GameObject("Mosaic camera (" + LayerMask.LayerToName(MosaicLayer) + ")");
        MosaicCameraGameObject.transform.SetParent(this.transform);
        MosaicCamera = MosaicCameraGameObject.AddComponent<Camera>();

        // Disable the helper camera, so we can render it manually, and disable its GameObject so it doesn't
        // show up in the editor viewport.
        MosaicCamera.enabled = false;
        MosaicCameraGameObject.SetActive(false);

        SetupTextures();
    }

    void OnDestroy()
    {
        // Make sure we destroy our textures immediately.
        ReleaseTextures();

        // Destroy the camera we created.
        if(MosaicCamera != null)
        {
            Destroy(MosaicCamera);
            MosaicCamera = null;
        }
    }    

    private void SetupTextures()
    {
        int Width = ThisCamera.pixelWidth;
        int Height = ThisCamera.pixelHeight;

        // int Factor = 16;
        // int HorizontalMosaicBlocks = Width/Factor;
        // int VerticalMosaicBlocks = Height/Factor;
        int HorizontalMosaicBlocks = (int) Math.Max(1, MosaicBlocks);
        int VerticalMosaicBlocks = HorizontalMosaicBlocks;

        // MosaicBlocks is the number of mosaic blocks we want to display.  However, the screen is probably not
        // square and we want the mosaic blocks to be square.  Adjust the number of blocks to fix this.
        // Use the larger of the two sizes, so the blocks are square.
        float AspectRatio = ((float) Width) / Height;
        if(Width < Height)
        {
            // The screen is taller than it is wide.  Decrease the number of blocks horizontally.
            HorizontalMosaicBlocks = (int) Math.Round(VerticalMosaicBlocks * AspectRatio);
        }
        else
        {
            // The screen is wider than it is tall.  Decrease the number of blocks vertically.
            VerticalMosaicBlocks = (int) Math.Round(HorizontalMosaicBlocks / AspectRatio);
        }

        // There's no point to these being higher than the display resolution.
        HorizontalMosaicBlocks = Math.Min(HorizontalMosaicBlocks, ThisCamera.pixelWidth);
        VerticalMosaicBlocks = Math.Min(VerticalMosaicBlocks, ThisCamera.pixelHeight);

        HorizontalMosaicBlocks = Math.Max(HorizontalMosaicBlocks, 1);
        VerticalMosaicBlocks = Math.Max(VerticalMosaicBlocks, 1);

        // If the render targets are already created and the resolution we want them to be hasn't changed,
        // don't recreate them.
        if(LowResolutionTexture1 != null &&
            LowResolutionTexture1.width == HorizontalMosaicBlocks &&
            LowResolutionTexture1.height == VerticalMosaicBlocks)
            return;

        ReleaseTextures();

        if(HighResolutionRender)
        {
            // We'll render to the full resolution, then downscale to the mosaic resolution.  
            // If we created a POT texture we could do this by creating mipmaps, but we want
            // the full resolution texture to match the screen resolution so we can sample
            // cleanly.
            List<RenderTexture> Textures = new List<RenderTexture>();
            int CurrentWidth = Width, CurrentHeight = Height;
            bool First = true;
            do
            {
                // We only need a depth buffer for the first texture, since we render into it.  We only
                // blit into the others.
                RenderTexture texture = new RenderTexture(CurrentWidth, CurrentHeight, First? 24:0);
                Textures.Add(texture);

                // The first pass only premultiplies alpha and doesn't downscale, so downscaling always
                // happens on premultiplied alpha.
                if(!First)
                {
                    CurrentWidth /= 2;
                    CurrentHeight /= 2;
                }

                First = false;
            }
            while(CurrentWidth > HorizontalMosaicBlocks && CurrentHeight > VerticalMosaicBlocks);
            HighResolutionTextures = Textures.ToArray();
        }

        LowResolutionTexture1 = new RenderTexture(HorizontalMosaicBlocks, VerticalMosaicBlocks, 24);
        LowResolutionTexture2 = new RenderTexture(HorizontalMosaicBlocks, VerticalMosaicBlocks, 24);

        // Draw the low-resolution texture with nearest neighbor sampling.
        LowResolutionTexture1.filterMode = FilterMode.Point;
        LowResolutionTexture2.filterMode = FilterMode.Point;
    }

    private void ReleaseTextures()
    {
        if(HighResolutionTextures != null)
        {
            foreach(RenderTexture texture in HighResolutionTextures)
                texture.Release();
            HighResolutionTextures = null;
        }

        if(LowResolutionTexture1 != null)
        {
            LowResolutionTexture1.Release();
            LowResolutionTexture1 = null;
        }

        if(LowResolutionTexture2 != null)
        {
            LowResolutionTexture2.Release();
            LowResolutionTexture2 = null;
        }
    }

    List<GameObject> FindGameObjectsInLayer(int layer, bool excluded=false)
    {
        GameObject[] goArray = FindObjectsOfType(typeof(GameObject)) as GameObject[];
        List<GameObject> results = new List<GameObject>();
        foreach(GameObject go in goArray)
        {
            if((!excluded && go.layer == layer) || (excluded && go.layer != layer))
                results.Add(go);
        }
        return results;
    }

    void OnPreRender()
    {
        // Update the render targets if the window has been resized.
        SetupTextures();

        // Match the helper camera to the main camera.
        MosaicCamera.CopyFrom(ThisCamera);

        // The background color is only visible if we're not doing any expand passes.  Otherwise, it'll
        // be visible as the default result from ExpandEdges.shader.
        MosaicCamera.backgroundColor = new Color(DefaultColor.r, DefaultColor.g, DefaultColor.b, 0);

        //
        // Render the mosaic texture.
        //

        // Hiding other layers with the layer mask prevents them from casting shadows too.  If
        // ShadowsCastOnMosaic is enabled, hide the objects that aren't being mosaiced
        // by setting them to ShadowsOnly, so we draw only our mosaiced object but it still has shadows
        // cast by other objects.  If it's false, be less intrusive and faster by just setting the
        // cullingMask.  This can look wrong if the whole scene is in shadow, because the mosaiced
        // object will be brighter.
        Dictionary<Renderer,ShadowCastingMode> DisabledRenderers = new Dictionary<Renderer,ShadowCastingMode>();
        if(ShadowsCastOnMosaic)
        {
            List<GameObject> NonMosaicObjects = FindGameObjectsInLayer(MosaicLayer, true);
            foreach(GameObject go in NonMosaicObjects)
            {
                Renderer r = go.GetComponent<Renderer>();
                if(r == null)
                    continue;

                DisabledRenderers[r] = r.shadowCastingMode;
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
        } else {
            MosaicCamera.cullingMask =  (1 << MosaicLayer);
        }

        // Match the projection matrix to the main camera, so we render the same thing even though
        // the aspect ratio isn't exactly the same.
        MosaicCamera.projectionMatrix = ThisCamera.projectionMatrix;

        MosaicCamera.renderingPath = ThisCamera.renderingPath;
        MosaicCamera.clearFlags = CameraClearFlags.SolidColor;
        MosaicCamera.targetTexture = HighResolutionRender? HighResolutionTextures[0]:LowResolutionTexture1;

        MosaicCamera.Render();

        if(HighResolutionRender)
        {
            // We rendered at full resolution.  Blit the image to progressively smaller textures to
            // cleanly downscale it.
            for(int i = 0; i < HighResolutionTextures.Length - 1; ++i)
            {
                // The first pass premultiplies alpha.
                if(i == 0)
                    Graphics.Blit(HighResolutionTextures[i], HighResolutionTextures[i+1], PremultiplyMaterial);
                else
                    Graphics.Blit(HighResolutionTextures[i], HighResolutionTextures[i+1]);
            }
            Graphics.Blit(HighResolutionTextures[HighResolutionTextures.Length-1], LowResolutionTexture1);
        }
        else
        {
            // We rendered at mosaic resolution.  Blit the image once to premultiply.
            Graphics.Blit(LowResolutionTexture1, LowResolutionTexture2, PremultiplyMaterial);
            RenderTexture tmp = LowResolutionTexture1;
            LowResolutionTexture1 = LowResolutionTexture2;
            LowResolutionTexture2 = tmp;
        }

        // Now that we're done rendering the mosaic texture, undo any changes we just made to shadowCastingMode.
        foreach(KeyValuePair<Renderer,ShadowCastingMode> SavedShadowMode in DisabledRenderers)
            SavedShadowMode.Key.shadowCastingMode = SavedShadowMode.Value;

        ExpandMosaic();

        MosaicMaterial.SetTexture("MosaicTex", LowResolutionTexture1);

        // Disable the masking shaders.  We'll enable the correct one below.
        MosaicMaterial.DisableKeyword("SPHERE_MASKING");
        MosaicMaterial.DisableKeyword("TEXTURE_MASKING");

        // Set up fading if we have a high resolution render and a fading control.
        if(HighResolutionRender)
        {
            // We're rendering in high resolution, so fading and masking is supported.  Enable FADING
            // to have the shader sample both the low- and high-resolution textures, which enables
            // alpha.
            MosaicMaterial.EnableKeyword("FADING");
            MosaicMaterial.SetTexture("HighResTex", HighResolutionTextures[1]);
            MosaicMaterial.SetFloat("Alpha", Alpha);
            MosaicMaterial.SetTexture("MaskTex", MaskingTexture);

            // Select whether we're using the texture masking shader, sphere masking, or no masking.
            if(MaskingMode == MaskMode.Texture)
            {
                MosaicMaterial.EnableKeyword("TEXTURE_MASKING");
            }
            else if(MaskingMode == MaskMode.Sphere)
            {
                // MaskSizeInner is how big the mosaic circle should be around MaskingSphere.  Within this
                // distance, the mosaic is 100%.  MaskSizeOuter is the size of the fade-out.  At 0, the
                // mosaic cuts out abruptly.  At 1, it fades out over one world space unit.
                //
                // The transparency of the mosaic scales distance so MaskSizeInner is 1 (100%) and MaskSizeOuter
                // is 0 (0%).  If the distance is less than MaskSizeInner it'll be above 1 and clamped.  This
                // is simply:
                //
                // f = (dist - MaskSizeOuter) / (MaskSizeInner - MaskSizeOuter);
                //
                // To remove the division from the fragment shader, we pass in MaskScaleFactor,
                // which is 1 / (MaskSizeInner - MaskSizeOuter).
                //
                // If the fade is zero, nudge it up slightly to avoid division by zero.
                float MaskSizeInner = 1;
                float MaskSizeOuter = MaskSizeInner + MaskFade;
                if(MaskFade == 0)
                    MaskSizeOuter += 0.0001f;

                MosaicMaterial.SetFloat("MaskSizeOuter", MaskSizeOuter);
                float MaskSizeFactor = 1.0f / (MaskSizeInner - MaskSizeOuter);
                MosaicMaterial.SetFloat("MaskSizeFactor", MaskSizeFactor);
                Matrix4x4 mat = MaskingSphere.transform.worldToLocalMatrix;

                // Halve the size of the mask, since the distance from the center to the edge of
                // the mask sphere is 0.5, not 1:
                mat = Matrix4x4.TRS(
                        Vector3.zero,
                        Quaternion.AngleAxis(0, new Vector3(1,0,0)),
                        new Vector3(2,2,2)) * mat;

                MosaicMaterial.SetMatrix("MaskMatrix", mat);

                MosaicMaterial.EnableKeyword("SPHERE_MASKING");
            }
        }
        else
        {
            // We don't have a high-res texture, so disable fading.
            MosaicMaterial.DisableKeyword("FADING");
        }

        // Find the objects that we're mosaicing, and switch them to the mosaic shader, which
        // will sample the prerendered texture we just made.  This will happen during regular
        // rendering.
        List<GameObject> MosaicObjects = FindGameObjectsInLayer(MosaicLayer);
      
        foreach(GameObject go in MosaicObjects)
        {
            // Find objects in our layer that have a renderer.
            Renderer renderer = go.GetComponent<Renderer>();
            if(renderer == null)
                continue;
            
            // Save the original materials so we can restore them in OnPostRender.
            SavedMaterials[renderer] = renderer.materials;

            // Replace all materials on this object with ours.
            Material[] ReplacementMaterials = new Material[renderer.materials.Length];
            for(int i = 0; i < renderer.materials.Length; ++i)
                ReplacementMaterials[i] = MosaicMaterial;
            renderer.materials = ReplacementMaterials;
        }
    }

    void ExpandMosaic()
    {
        // The low resolution texture is missing some pixels right at the edge, where texture sampling
        // sampled outside of opaque objects.  It's also possible for pixels well inside objects to not
        // sample anything, if the mosaic is extremely coarse, though this usually only happens in extreme
        // cases (eg. a 4x4 mosaic covering the entire screen).
        //
        // ExpandEdgesMaterial looks at each transparent pixel and searches the pixels immediately surrounding
        // it for an opaque pixel.  Each pass will fill in another pixel outwards.  We usually don't need more
        // than one or two passes, but these should be fairly cheap since it's operating on the low resolution
        // mosaic texture.
        //
        // Each pass looks like this, with transparent pixels being set to an arbitrary neighboring pixel's
        // color:
        //
        // ...... 1111.. 11111. 111111
        // .11... 1111.. 111112 111112
        // ...... 111122 111122 111122
        // ....2. ...222 331222 331222
        // ...... .33222 333222 333222
        // ..3... .333.. 333332 333332
        //
        // This is the UV step the shader needs to advance to see the pixel adjacent to itself.  This is used
        // to find a pixel inside the layer if it samples a pixel with no color.
        Vector4 PixelUVStep = new Vector4(1.0f / LowResolutionTexture1.width, 1.0f / LowResolutionTexture1.height, 0, 0);

        ExpandEdgesMaterial.SetVector("PixelUVStep", PixelUVStep);
        for(int pass = 0; pass < 4; ++pass)
        {
            Graphics.Blit(LowResolutionTexture1, LowResolutionTexture2, ExpandEdgesMaterial);
            RenderTexture tmp = LowResolutionTexture1;
            LowResolutionTexture1 = LowResolutionTexture2;
            LowResolutionTexture2 = tmp;
        }
    }

    void OnPostRender()
    {
        // Restore the original materials.
        foreach(KeyValuePair<Renderer,Material[]> SavedMat in SavedMaterials)
            SavedMat.Key.materials = SavedMat.Value;
        SavedMaterials.Clear();

        // Discard the textures we rendered, since we don't need them anymore.
        if(LowResolutionTexture1 != null)
            LowResolutionTexture1.DiscardContents();
        if(LowResolutionTexture2 != null)
            LowResolutionTexture1.DiscardContents();
        foreach(RenderTexture texture in HighResolutionTextures)
            texture.DiscardContents();
    }
};

