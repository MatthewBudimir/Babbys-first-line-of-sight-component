using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;
using NUnit.Framework;


public class VisibilityManagerTestSuite
{
    private VisibilityManager visibilityManager;
    [UnityTest]
    public IEnumerator SegmentsClockwiseNE()
    {
        //Instantiate objects
        //GameObject gameObject = MonoBehaviour.Instantiate(Resources.Load<GameObject>("Prefabs/VisibilityManager"));
        visibilityManager = new VisibilityManager();
        //Create segments
        Vector3[] segment = { new Vector3(1,4), new Vector3(2,0,2) };
        //Create origin
        Vector3 origin = new Vector3(1,0,1);
        //Give segments to program

        //get response
        

        //Assert.IsTrue(visibilityManager.Isclockwise(segment, origin));

        yield return new WaitForSeconds(0.1f);
        Object.Destroy(visibilityManager.gameObject);

    }

}
