using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ExamplePlayer : MonoBehaviour
{
    public GameObject observer;
    VisibilityManager visibilityManager;
    public Material detectedMaterial;
    MeshRenderer meshrenderer;
    Material defaultMaterial;
    // Start is called before the first frame update
    void Start()
    {
        meshrenderer = GetComponent<MeshRenderer>();
        defaultMaterial = meshrenderer.material;
        visibilityManager = observer.GetComponent<VisibilityManager>();
    }

    // Update is called once per frame
    void Update()
    {
        if(visibilityManager.TestVisibility(transform.position) != -1)
        {
            meshrenderer.material = detectedMaterial;
            Debug.Log("DETECTED");
        }
        else
        {
            meshrenderer.material = defaultMaterial;
        }
    }
}
