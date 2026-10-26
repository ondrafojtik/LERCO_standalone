using UnityEngine;
using static Unity.Collections.AllocatorManager;

namespace UnityVolumeRendering
{
    [ExecuteInEditMode]
    public class SlicingPlane : MonoBehaviour
    {
        public VolumeRenderedObject targetObject;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock block;

        private void Start()
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }
        private void Update()
        {

            if (block == null) block = new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(block);
            Transform volumeTransform = targetObject != null ? targetObject.volumeContainerObject.transform : transform.parent;
            block.SetMatrix("_parentInverseMat", volumeTransform.worldToLocalMatrix);
            block.SetMatrix("_planeMat", transform.localToWorldMatrix);
            meshRenderer.SetPropertyBlock(block);



            //meshRenderer.sharedMaterial.SetMatrix("_parentInverseMat", transform.parent.worldToLocalMatrix);
            
            //Transform volumeTransform = targetObject != null ? targetObject.volumeContainerObject.transform : transform.parent;
            //meshRenderer.sharedMaterial.SetMatrix("_parentInverseMat", volumeTransform.worldToLocalMatrix);
            //meshRenderer.sharedMaterial.SetMatrix("_planeMat", transform.localToWorldMatrix); // TODO: allow changing scale




        }
    }
}
