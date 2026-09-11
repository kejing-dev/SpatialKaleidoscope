using UnityEngine;

namespace Kaleido
{
    /// <summary>Everything a scene needs from the world: dome geometry, anchors, camera, layers.</summary>
    public class SceneContext
    {
        public Transform DomeRoot;       // world origin, never rotates (PRD: dome does not spin)
        public Mesh DomeMesh;            // inward sphere, radius KaleidoConfig.DomeRadius
        public Transform ObjectAnchor;   // 2 m in front of the initial view point, eye height
        public Camera MainCamera;
        public HeadTilt HeadTilt;
        public int CellLayer;            // layer for 2D physics cells (rendered by helper cameras only)
        public int BulbLayer;            // layer for the ray-marched proxy (helper cameras only)
        public bool IsEditorCapture;     // true when driven by the editor capture tool (mono rendering)

        int _cellSlots;
        /// <summary>Each physics cell lives at a distinct world offset so two cells never interact.</summary>
        public Vector3 AllocateCellOffset()
        {
            _cellSlots++;
            return new Vector3(1000f + _cellSlots * 50f, -500f, 0f);
        }
    }
}
