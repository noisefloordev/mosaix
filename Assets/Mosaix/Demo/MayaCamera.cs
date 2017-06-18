// Rewritten based on https://gist.githubusercontent.com/JISyed/5017805/raw/aa69ce701f3d5a13a9f87880dee776d2208f71cb/MoveCamera.cs
using UnityEngine;
using UnityEngine.EventSystems;

// TODO Maya zooms exponentially: as you zoom further from the center of interest,
// zooming moves faster.  We only zoom linearly.

public class MayaCamera: MonoBehaviour 
{
    private float TumblingSpeed = 300.0f;
    private float PanningSpeed = 1.5f;
    private Vector3 MousePosition;
    private Vector3 Origin;

    enum MouseMode
    {
        Tumbling,
        Panning,
        Zooming,
        None,
    };
    MouseMode mode = MouseMode.None;

    static bool GetMouseIntersection(Camera camera, out RaycastHit hit)
    {
        Vector2 pos = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
        Vector3 world = new Vector3(pos.x, pos.y, camera.nearClipPlane);

        // Find the closest intersection.
        RaycastHit closest = new RaycastHit();
        bool found = false;
        foreach(RaycastHit ray in Physics.RaycastAll(camera.ScreenPointToRay(world)))
        {
            if(found && closest.distance < ray.distance)
                continue;
            closest = ray;
            found = true;
        }
        hit = closest;
        return found;
    }

    // Keep track of whether we have focus.  There should be a way to just query this and not have
    // to track it manually.
    bool HasFocus = true;
    void OnApplicationFocus(bool b) { HasFocus = b; }

    // This is set by DemoGUI to tell us where the GUI is.
    [System.NonSerialized]
    public Rect IgnoreRect;

    void OnMouseDown()
    {
        Debug.Log("xxx");
    }

    void Update() 
    {
        Camera camera = gameObject.GetComponent<Camera>();

        if(mode == MouseMode.None)
        {
            if(IgnoreRect.Contains(Input.mousePosition))
                return;

            if(Input.GetMouseButtonDown(0))
                mode = MouseMode.Tumbling;
            else if(Input.GetMouseButtonDown(1))
                mode = MouseMode.Zooming;
            else if(Input.GetMouseButtonDown(2))
                mode = MouseMode.Panning;

            if(mode != MouseMode.None)
            {
                MousePosition = Input.mousePosition;

                // Find the object the mouse is over.  If there isn't one, keep the old origin.
                // Maya actually remembers the distance to the most recent click (centerOfInterest)
                // and uses that as the distance to the origin if you click on a point that doesn't
                // intersect anything, but we don't currently do that.
                RaycastHit hit;
                if(GetMouseIntersection(camera, out hit))
                    Origin = hit.point;
            }
        }

        if(mode == MouseMode.None && HasFocus)
        {
            // Handle mouse wheel zooming.  Don't do this when we don't have focus, to reduce this
            // triggering unintentionally while scrolling other editor windows.  We should really
            // check whether the mouse is over the window, but Unity doesn't know how to let us do
            // this.
            float MouseWheelDelta = Input.GetAxis("Mouse ScrollWheel");
            if(MouseWheelDelta != 0)
            {
                bool Forward = MouseWheelDelta > 0;
                float zoom = .1f * (Forward? +1:-1);
                ZoomBy(zoom);
            }
        }
        
        if(mode == MouseMode.Tumbling && !Input.GetMouseButton(0))
            mode = MouseMode.None;
        if(mode == MouseMode.Zooming && !Input.GetMouseButton(1))
            mode = MouseMode.None;
        if(mode == MouseMode.Panning && !Input.GetMouseButton(2))
            mode = MouseMode.None;

        Vector3 distance = camera.ScreenToViewportPoint(Input.mousePosition - MousePosition);
        MousePosition = Input.mousePosition;

        // Rotate camera along X and Y axis
        if(mode == MouseMode.Tumbling)
        {
            Vector3 OriginWorldSpaceRight = transform.localToWorldMatrix.MultiplyVector(Vector3.right);
            transform.RotateAround(Origin, OriginWorldSpaceRight, -distance.y * TumblingSpeed);
            transform.RotateAround(Origin, Vector3.up, distance.x * TumblingSpeed);
        }

        else if(mode == MouseMode.Panning)
        {
            Vector3 move = new Vector3(distance.x * -1 * PanningSpeed, distance.y * -1 * PanningSpeed, 0);
            transform.Translate(move, Space.Self);
        }

        else if(mode == MouseMode.Zooming)
        {
            float zoom = distance.x - distance.y;
            ZoomBy(zoom);
        }
    }

    void ZoomBy(float zoom)
    {
        Vector3 move = zoom * transform.forward; 
        transform.Translate(move, Space.World);
    }
}
