using UnityEngine;

public class DemoGUI: MonoBehaviour 
{
    bool Anchoring = false;
    bool AnchorScaling = false;
    bool ShowSphere = true;
    bool ShowOutlines = false;
    float MosaicBlocks = 10;

    public Mosaix MosaixComponent;
    public GameObject SphereMask;
    public Material MosaicMaterial, MosaicWithOutlineMaterial;

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(0, 0, Screen.width, Screen.height));
            GUILayout.BeginHorizontal();
            GUILayout.Space(50); // left padding
                GUILayout.BeginVertical();
                    GUILayout.FlexibleSpace(); // bottom align
                    GUILayout.Label("Hold ALT to navigate with the mouse", GUI.skin.box);
                    GUILayout.Space(50); // bottom padding
                GUILayout.EndVertical();
            GUILayout.Space(50); // right padding
            GUILayout.EndHorizontal();
        GUILayout.EndArea();

        GUILayout.BeginArea(new Rect(0, 0, Screen.width, Screen.height));
            GUILayout.Space(10); // top padding
            GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if(GUILayout.Button("Open on GitHub"))
                    Application.OpenURL("https://github.com/unity-effects/mosaix");
            GUILayout.Space(10); // right padding
            GUILayout.EndHorizontal();
        GUILayout.EndArea();

        GUIStyle PaddedBox = new GUIStyle(GUI.skin.box); 
        PaddedBox.padding = new RectOffset(10,10,10,10);

        GUILayout.BeginArea(new Rect(10, 10, Screen.width-20, Screen.height-20));
            GUILayout.BeginVertical(PaddedBox, GUILayout.MinWidth(250));
            ControlWindow();
            GUILayout.EndVertical();
        GUILayout.EndArea();

        Refresh();
    }

    void ControlWindow()
    {
        GUILayout.Label("Teapot Mosaic Controls");

        MosaixComponent.enabled = GUILayout.Toggle(MosaixComponent.enabled, "Enable mosaic");

        GUILayout.BeginHorizontal();
        GUILayout.Label("Blocks");
        MosaicBlocks = GUILayout.HorizontalSlider(MosaicBlocks, 2, 100);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Alpha");
        MosaixComponent.Alpha = GUILayout.HorizontalSlider(MosaixComponent.Alpha, 0, 1);
        GUILayout.EndHorizontal();

        ShowOutlines = GUILayout.Toggle(ShowOutlines, "Outlines");

        Anchoring = GUILayout.Toggle(Anchoring, "Anchoring");
        if(Anchoring)
        {
            BeginGroup();
            AnchorScaling = GUILayout.Toggle(AnchorScaling, "Anchor scaling");
            EndGroup();
        }

        MosaixComponent.SphereMasking = GUILayout.Toggle(MosaixComponent.SphereMasking, "Sphere masking");
        if(MosaixComponent.SphereMasking)
        {
            BeginGroup();

            ShowSphere = GUILayout.Toggle(ShowSphere, "Show sphere");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Sphere fade");
            MosaixComponent.MaskFade = GUILayout.HorizontalSlider(MosaixComponent.MaskFade, 0, 1);
            GUILayout.EndHorizontal();

            Vector3 SphereScale = SphereMask.transform.localScale;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Scale X");
            SphereScale.x = GUILayout.HorizontalSlider(SphereScale.x, 0.25f, 2);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Scale Y");
            SphereScale.y = GUILayout.HorizontalSlider(SphereScale.y, 0.25f, 2);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Scale Z");
            SphereScale.z = GUILayout.HorizontalSlider(SphereScale.z, 0.25f, 2);
            GUILayout.EndHorizontal();
            SphereMask.transform.localScale = SphereScale;

            EndGroup();
        }

        MosaixComponent.TextureMasking = GUILayout.Toggle(MosaixComponent.TextureMasking, "Texture masking");
        MosaixComponent.ShowMask = GUILayout.Toggle(MosaixComponent.ShowMask, "Show mask");
    }

    int level = 0;
    void BeginGroup()
    {
        ++level;
        GUILayout.BeginHorizontal();
        GUILayout.Space(level*15); // left padding
        GUILayout.BeginVertical();
    }

    void EndGroup()
    {
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        --level;
    }

    void Refresh()
    {
        MosaixComponent.MosaicBlocks = MosaicBlocks;
        Renderer SphereMaskRenderer = SphereMask.GetComponent<Renderer>();
        SphereMaskRenderer.enabled = MosaixComponent.SphereMasking && ShowSphere;
        
        if(Anchoring)
            MosaixComponent.AnchorTransform = SphereMask.gameObject;
        else
            MosaixComponent.AnchorTransform = null;

        MosaixComponent.ScaleMosaicToAnchorDistance = AnchorScaling;
        if(Anchoring && AnchorScaling)
            MosaixComponent.MosaicBlocks *= 5;

        // This shows how you can apply your own effects on top of the mosaic, such as having toon
        // outlines that aren't mosaiced.
        MosaixComponent.MosaicMaterial = ShowOutlines? MosaicWithOutlineMaterial:MosaicMaterial;
    }
}
