using System.Collections;
using System.Collections.Generic;
using UnityEngine;


//#### BIG TODO LIST:
/*
 * Figure out how to properly implement active segment initialization instead of this O(n^2) nonsense
 * implement Polygon mesh generation
 * Implement Visibility polygon lookup (with angle ranges if desired)
 * 
     */

public class VisibilityManagerv2 : MonoBehaviour
{
    Transform[] wallTransforms;
    List<Segment> segments;
    List<Segment> outlines;
    List<Point> points;
    List<VisionArc> visionArcs;
    int lastHitId;
    public Transform player;
    private List<RaySegmentPair> collisions; // List of collisions we use to figure out visibility polygon.
    //Compute shader for calculating intersections
    public ComputeShader intersectCompute;
    //Result for shader (Temporary)
    public RenderTexture result;
    public float cornerThreshold = 0.05f;
    public MeshFilter meshFilter;

    class Point
    {
        public Vector2 coor;
        public float theta; //theta angle
        public float Rsqr; //R^2 value, used to break ties when 2 points have the same angle.
        public int segmentId;
        public Point sibling;
        public Point(Vector2 v, int segId)
        {
            coor = v;
            theta = 0; //unassigned until relative point is given.
            Rsqr = 0; // ditto
            segmentId = segId;
        }
        public void setSibling(Point p)
        {
            sibling = p;
        }
        public Vector3 toVector3()
        {
            return new Vector3(coor.x, 0, coor.y);
        }
        public void recalculateTheta(Vector2 origin)
        {
            theta = Mathf.Atan2(coor.y - origin.y, coor.x - origin.x);
            theta = theta < 0 ? theta + (2 * Mathf.PI) : theta;
        }
    }

    class Segment
    {
        private static int segmentCount = 0; 
        public Point A; // first point
        public Point B; // second point
        public bool active; //whether or not we're active or not
        public int id;
        public Segment(Vector2 start,Vector2 end)
        {
            id = segmentCount;
            segmentCount++;
            A = new Point(start, id);
            B = new Point(end, id);

            A.setSibling(B);
            B.setSibling(A);

            active = false;
        }
        public static void resetCount()
        {
            segmentCount = 0;
        }
    }
    
    struct RaySegmentPair
    {
        public Vector2 A1; // A1 is also where we write our collision to.
        public Vector2 A2; // coefficients are output here, y > 1 indicates a miss.
        public Vector2 B1; // Later outputs denominator when solving for intersection: See "LineIntersectComputeShader.shader for detail"
        public Vector2 B2; // B2.x is our storage for distance when we output //B2.y stores distance to from collision to segment edges.
        public float id; //Id of segmentB. Used later for simplifying geometry.
        public float theta; //Angle of collision
    }

    struct VisionArc
    {
        public float startingAngle;
        public float endingAngle;
        public float startingDistSqr;
        public float endingDistSqr;
        public Vector3 point1;
        public Vector3 point2;
    }

    RaySegmentPair createRaySegmentPair(Vector2 A1,Vector2 A2,Vector2 B1,Vector2 B2,float id,float theta)
    {
        RaySegmentPair pair = new RaySegmentPair();
        pair.A1 = A1;
        pair.A2 = A2; 
        pair.B1 = B1;
        pair.B2 = B2;
        pair.id = id;
        pair.theta = theta;
        return pair;
    }

    List<int> FindClosestIntersect(RaySegmentPair[] data)
    {
        Vector2 target = new Vector2(data[0].A2.x, data[0].A2.y);
        float distToTarget = Vector2.Distance(data[0].A2, data[0].A1);
        distToTarget *= distToTarget; //All distances are squared because square roots are expensive and don't change result of comparison

        int floatSize = sizeof(float);
        ComputeBuffer buffer = new ComputeBuffer(data.Length, 10 * floatSize);
        buffer.SetData(data);
        int kernel = intersectCompute.FindKernel("CalculateIntersection2D");
        intersectCompute.SetBuffer(kernel, "Result", buffer);
        intersectCompute.Dispatch(kernel, data.Length, 1, 1);
        buffer.GetData(data);
        buffer.Dispose();
        
        List<int> hitsIndeces = new List<int>();
        hitsIndeces.Add(-1); // we're gonna replace this with our closest index when the time comes.
        bool closestDefined = false;
        int closestIndex = -1;
        int targetIndex = -1;
        //First Pass: Find closest hit that isn't hitting a corner, record the corner hits as we go.
        for (int i = 0; i < data.Length; i++)
        {
            //If we have found a solid hit:
            if (closestDefined)
            {
                if (manhattanDist(target, data[i].A1) < cornerThreshold && data[i].A2.y <= 1.01f && data[i].A2.y >= -0.01f && data[i].A2.x > 0)
                {
                    targetIndex = i;
                }
                //If this is the closer than our current closest hit.
                if (data[i].B2.x < data[closestIndex].B2.x)
                {
                    //Check coefficients
                    if (data[i].A2.y <= 1.00f && data[i].A2.y >= -0.00f && data[i].A2.x > 0)
                    {

                        //Check if this is a collision with a corner.
                        if (Vector2.Distance(data[i].A1, target) > cornerThreshold)// (data[i].B2.y < cornerThreshold)
                        {
                            closestIndex = i;
                        }
                    }
                }
                
            }
            else
            {
                if (manhattanDist(target, data[i].A1) < cornerThreshold && data[i].A2.y <= 1.01f && data[i].A2.y >= -0.01f && data[i].A2.x > 0)
                {
                    targetIndex = i;
                }
                //If we haven't found a solid hit:
                if (data[i].A2.y <= 1.00f && data[i].A2.y >= -0.00f && data[i].A2.x > 0)
                {
                    //Coefficients are correct
                    //Check if this is a collision with a corner
                    if (Vector2.Distance(data[i].A1, target) > cornerThreshold) //(data[i].B2.y < cornerThreshold)
                    {
                        closestIndex = i;
                        closestDefined = true;
                    }
                    else
                    {
                        targetIndex = i;
                    }
                }
            }
        }

        //if(closestIndex != 0)
        //{
        //    targetIndex = 0;
        //}
        //else
        //{
        //    targetIndex = 1;
        //}
        //data[targetIndex].A1 = target;
        //data[targetIndex].B2.x = distToTarget;
        //Finalize processing on hits.
        if(closestIndex == -1)
        {
            hitsIndeces[0] = targetIndex;
        }
        else
        {
            hitsIndeces[0] = closestIndex;
            if (data[targetIndex].B2.x < data[closestIndex].B2.x)
            {
                hitsIndeces.Add(targetIndex);
            }
        }
        
        return hitsIndeces;
    }

    
    float manhattanDist(Vector2 A, Vector2 B)
    {
        return Mathf.Abs((A.x - B.x) + Mathf.Abs((A.y - B.y)));
    }

    float sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }
    bool PointInTriangle(Vector2 pt, Vector2 v1, Vector2 v2, Vector2 v3)
    {
        float d1, d2, d3;
        bool has_neg, has_pos;

        d1 = sign(pt, v1, v2);
        d2 = sign(pt, v2, v3);
        d3 = sign(pt, v3, v1);

        has_neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        has_pos = (d1 > 0) || (d2 > 0) || (d3 > 0);

        return !(has_neg && has_pos);
    }

    void debugDrawCollision(Vector2 v, float y, Color col)
    {
        Debug.DrawLine(player.position, new Vector3(v.x, y, v.y), Color.gray);
        Debug.DrawLine(new Vector3(v.x, y, v.y) + new Vector3(0.1f,0,0), new Vector3(v.x, y, v.y) + new Vector3(-0.1f, 0, 0), col);
        Debug.DrawLine(new Vector3(v.x, y, v.y) + new Vector3(0, 0, 0.1f), new Vector3(v.x, y, v.y) + new Vector3(0, 0, -0.1f), col);

    }
    void debugDrawEdge(Vector2 a, Vector2 b, float y, Color col)
    {
        //Debug.DrawLine(player.position, new Vector3(v.x, y, v.y), Color.gray);
        //Debug.DrawLine(new Vector3(v.x, y, v.y) + new Vector3(0.1f, 0, 0), new Vector3(v.x, y, v.y) + new Vector3(-0.1f, 0, 0), col);
        //Debug.DrawLine(new Vector3(v.x, y, v.y) + new Vector3(0, 0, 0.1f), new Vector3(v.x, y, v.y) + new Vector3(0, 0, -0.1f), col);
        Debug.DrawLine(new Vector3(a.x, y, a.y), new Vector3(b.x, y, b.y), col);
    }


    void debugDrawVisionArc(VisionArc arc, Vector3 pos, Color col)  
    {
        //Vector3 first = new Vector3(Mathf.Sqrt(arc.startingDistSqr) * Mathf.Cos(arc.startingAngle),
        //                            0,
        //                            Mathf.Sqrt(arc.startingAngle) * Mathf.Sin(arc.startingAngle));
        //first += pos;
        //Vector3 second = new Vector3(Mathf.Sqrt(arc.endingDistSqr) * Mathf.Cos(arc.endingAngle),
        //                            0,
        //                            Mathf.Sqrt(arc.endingDistSqr) * Mathf.Sin(arc.endingAngle));
        //second += pos;
        Vector3 first = arc.point1;//  + pos;
        Vector3 second = arc.point2;// + pos;
        Debug.DrawLine(pos, first, col);
        Debug.DrawLine(pos, second, col);
        Debug.DrawLine(first, second, col);
    }

    void Start()
    {
        player.hasChanged = true;
        lastHitId = -1;
        segments = new List<Segment>();
        outlines = new List<Segment>();
        points = new List<Point>();
        wallTransforms = GetComponentsInChildren<Transform>();
        /*
         *** IMPORTANT USAGE DETAIL ***
         * This process is designed to work with environmental elements made from cubes.
         * For a more in depth version of this you'd need to implement convex hull
         */

        //CREATE OUTLINES: USED PURELY FOR DISPLAYING DEBUG DRAWINGS.
        Vector3[] segment;
        for (int i = 0; i < wallTransforms.Length; i++)
        {
            //Debug.Log("PROCESSING");
            //For each element, define 4 segments that define the boundries of the cube:
            //Segment1
            segment = new Vector3[2];
            segment[0] = wallTransforms[i].TransformPoint(new Vector3(-0.5f, 0, 0.5f));
            segment[1] = wallTransforms[i].TransformPoint(new Vector3(0.5f, 0, 0.5f));
            outlines.Add(new Segment(new Vector2(segment[0].x, segment[0].z), new Vector2(segment[1].x, segment[1].z)));
            //Segment2
            segment[0] = wallTransforms[i].TransformPoint(new Vector3(0.5f, 0, 0.5f));
            segment[1] = wallTransforms[i].TransformPoint(new Vector3(0.5f, 0, -0.5f));
            outlines.Add(new Segment(new Vector2(segment[0].x, segment[0].z), new Vector2(segment[1].x, segment[1].z)));
            //Segment3
            segment[0] = wallTransforms[i].TransformPoint(new Vector3(0.5f, 0, -0.5f));
            segment[1] = wallTransforms[i].TransformPoint(new Vector3(-0.5f, 0, -0.5f));
            outlines.Add(new Segment(new Vector2(segment[0].x, segment[0].z), new Vector2(segment[1].x, segment[1].z)));
            //Segment4
            segment[0] = wallTransforms[i].TransformPoint(new Vector3(-0.5f, 0, -0.5f));
            segment[1] = wallTransforms[i].TransformPoint(new Vector3(-0.5f, 0, 0.5f));
            outlines.Add(new Segment(new Vector2(segment[0].x, segment[0].z), new Vector2(segment[1].x, segment[1].z)));
        }
        //Reset the counter so that our outlines don't influence our unique id's
        Segment.resetCount();
        //###### Build a box around the obstacles using your transform.
        segment = new Vector3[2];
        segment[0] = wallTransforms[0].TransformPoint(new Vector3(-0.5f, 0, 0.5f));
        segment[1] = wallTransforms[0].TransformPoint(new Vector3(0.5f, 0, 0.5f));
        segments.Add(new Segment(new Vector2(segment[0].x, segment[0].z), new Vector2(segment[1].x, segment[1].z)));
        //Segment2
        segment[0] = wallTransforms[0].TransformPoint(new Vector3(0.5f, 0, 0.5f));
        segment[1] = wallTransforms[0].TransformPoint(new Vector3(0.5f, 0, -0.5f));
        segments.Add(new Segment(new Vector2(segment[0].x, segment[0].z), new Vector2(segment[1].x, segment[1].z)));
        //Segment3
        segment[0] = wallTransforms[0].TransformPoint(new Vector3(0.5f, 0, -0.5f));
        segment[1] = wallTransforms[0].TransformPoint(new Vector3(-0.5f, 0, -0.5f));
        segments.Add(new Segment(new Vector2(segment[0].x, segment[0].z), new Vector2(segment[1].x, segment[1].z)));
        //Segment4
        segment[0] = wallTransforms[0].TransformPoint(new Vector3(-0.5f, 0, -0.5f));
        segment[1] = wallTransforms[0].TransformPoint(new Vector3(-0.5f, 0, 0.5f));
        segments.Add(new Segment(new Vector2(segment[0].x, segment[0].z), new Vector2(segment[1].x, segment[1].z)));
        //Skip your own transform:
        for (int i =1; i< wallTransforms.Length; i++)
        {
            //Debug.Log("PROCESSING");
            //For each element, define 4 segments that define the boundries of the cube:
            //Segment1
            segment = new Vector3[2];
            //Segment3
            segment[0] = wallTransforms[i].TransformPoint(new Vector3(-0.5f, 0, 0.5f));
            segment[1] = wallTransforms[i].TransformPoint(new Vector3(0.5f, 0, -0.5f));
            segments.Add(new Segment(new Vector2(segment[0].x, segment[0].z), new Vector2(segment[1].x, segment[1].z)));
            //Segment4
            segment[0] = wallTransforms[i].TransformPoint(new Vector3(0.5f, 0, 0.5f));
            segment[1] = wallTransforms[i].TransformPoint(new Vector3(-0.5f, 0, -0.5f));
            segments.Add(new Segment(new Vector2(segment[0].x, segment[0].z), new Vector2(segment[1].x, segment[1].z)));
        }
        for (int i = 0; i < segments.Count; i++)
        {
            points.Add(segments[i].A);
            points.Add(segments[i].B);
        }
    }

    // Update is called once per frame
    void Update()
    {
        
        //Recalculate Collisions if the player has moved.
        if (player.hasChanged)
        {
            // VERSION 2:
            //lastHitId = -1;
            ////Every frame the active segments and collisions are unique.
            //Dictionary<int, Segment> activeSegments = new Dictionary<int, Segment>();
            //collisions = new List<RaySegmentPair>();
            //visionArcs = new List<VisionArc>();
            //VisionArc VA = new VisionArc();
            //VisionArc finalArc = new VisionArc();
            //int finalArcId = -1;
            //List<RaySegmentPair> tempCollisionBuffer = new List<RaySegmentPair>(); // Used for first collision (we need more context to identify order of points)
            //Vector2 origin = new Vector2(player.position.x, player.position.z);


            ////Points are to be processed in order of angle relative to player.
            //for (int i = 0; i < points.Count; i++)
            //{
            //    points[i].recalculateTheta(origin);
            //}
            //points.Sort((a, b) => a.theta.CompareTo(b.theta));
            ////At this point we know the starting angle of our first vision arc:
            //VA.startingAngle = points[0].theta;

            ////Starting segments include all segments that span across our starting point.
            //List<Segment> startingSegments = segments.FindAll(x => angleInRange(x.A.theta,x.B.theta,points[0].theta) == 0);

            ////Define active segments:
            //foreach(Segment seg in startingSegments)
            //{
            //    activeSegments.Add(seg.id, seg);
            //}


            ////Iterate through points.
            //for (int i = 0; i < points.Count; i++)
            //{
            //    Debug.Log(activeSegments.Count);
            //    bool segmentProcessed = false;
            //    if (activeSegments.ContainsKey(points[i].segmentId))
            //    {
            //        //Then we need to remove this segment at the end.
            //        segmentProcessed = true;
            //    }
            //    else
            //    {
            //        activeSegments.Add(points[i].segmentId, segments[points[i].segmentId]);
            //    }

            //    //Compare this ray to every active segment
            //    //Create segment pairs to be sent to GPU for collision checking.
            //    int j = 0;
            //    RaySegmentPair[] segmentPairs = new RaySegmentPair[activeSegments.Count];
            //    int targetIndex = 0;

            //    foreach (Segment s in activeSegments.Values)
            //    {
            //        if(points[i].segmentId == s.id)
            //        {
            //            targetIndex = j;
            //        }
            //        segmentPairs[j] = createRaySegmentPair(origin, points[i].coor, s.A.coor, s.B.coor, s.id,points[i].theta);
            //        j++;
            //    }

            //    List<int> hitIndeces = FindClosestIntersect(segmentPairs);
            //    //If this is the first point, we have special measures:
            //    if(i == 0)
            //    {
            //        //First point to be processed requires special measures since we don't have any vision arcs yet.
            //        if(hitIndeces.Count > 1)
            //        {
            //            //We have 2 points, load them into the buffer
            //            tempCollisionBuffer.Add(segmentPairs[hitIndeces[0]]);
            //            tempCollisionBuffer.Add(segmentPairs[hitIndeces[1]]);
            //        }
            //        else
            //        {
            //            //We only have 1 point, add it to the buffer.
            //            tempCollisionBuffer.Add(segmentPairs[hitIndeces[0]]);
            //        }
            //    }
            //    else if(i == 1) //This is our second collision check, Make sense of our first collision using the second.
            //    {
            //        if(hitIndeces.Count > 1)
            //        {
            //            //Figure out if our second collision --the target index not the dead on collision--
            //            //has the same id as either of our points.
            //            //We want to match the closest collision if possible
            //            if(tempCollisionBuffer.Count > 1)
            //            {
            //                if ((int)segmentPairs[hitIndeces[1]].id/2 == tempCollisionBuffer[1].id/2)
            //                {
            //                    // Our target indexes match so they form the first segment.
            //                    //Our other point must form our final segment:
            //                    finalArc.endingAngle = tempCollisionBuffer[0].theta;
            //                    finalArc.endingDistSqr = tempCollisionBuffer[0].B2.x;
            //                    finalArc.point2 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
            //                    finalArcId = (int)tempCollisionBuffer[0].id;
            //                    //Establish arc start:
            //                    VA.startingAngle = tempCollisionBuffer[1].theta;
            //                    VA.startingDistSqr = tempCollisionBuffer[1].B2.x;
            //                    VA.point1 = new Vector3(tempCollisionBuffer[1].A1.x, 0, tempCollisionBuffer[1].A1.y);
            //                    //Establish arc end:
            //                    VA.endingAngle = segmentPairs[hitIndeces[1]].theta;
            //                    VA.endingDistSqr = segmentPairs[hitIndeces[1]].B2.x;
            //                    VA.point2 = new Vector3(segmentPairs[hitIndeces[1]].A1.x, 0, segmentPairs[hitIndeces[1]].A1.y);

            //                    //Commit arc to memory.
            //                    visionArcs.Add(VA);

            //                    //Establish next arc start:
            //                    VA.startingAngle = segmentPairs[hitIndeces[0]].theta;
            //                    VA.startingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
            //                    VA.point1 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
            //                    //Record last hit id:
            //                    lastHitId = (int)segmentPairs[hitIndeces[0]].id;
            //                }//if the id's of the two targets don't match then check to see if our segment is being occluded by a new segment:
            //                 //Occlusion would give us a primary collision matching our target id:
            //                else if((int)segmentPairs[hitIndeces[0]].id/2 == (int)tempCollisionBuffer[1].id/2)
            //                {
            //                    //
            //                    finalArc.endingAngle = tempCollisionBuffer[0].theta;
            //                    finalArc.endingDistSqr = tempCollisionBuffer[0].B2.x;
            //                    finalArc.point2 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
            //                    finalArcId = (int)tempCollisionBuffer[0].id;
            //                    //Establish arc start at first segment start:
            //                    VA.startingAngle = tempCollisionBuffer[1].theta;
            //                    VA.startingDistSqr = tempCollisionBuffer[1].B2.x;
            //                    VA.point1 = new Vector3(tempCollisionBuffer[1].A1.x, 0, tempCollisionBuffer[1].A1.y);
            //                    //Establish arc end at solid hit on occluding segment
            //                    VA.endingAngle = segmentPairs[hitIndeces[0]].theta;
            //                    VA.endingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
            //                    VA.point2 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
            //                    //Commit arc to memory.
            //                    visionArcs.Add(VA);
            //                    //Establish next arc start:
            //                    VA.startingAngle = segmentPairs[hitIndeces[1]].theta;
            //                    VA.startingDistSqr = segmentPairs[hitIndeces[1]].B2.x;
            //                    VA.point1 = new Vector3(segmentPairs[hitIndeces[1]].A1.x, 0, segmentPairs[hitIndeces[1]].A1.y);
            //                    //Record last hit id:
            //                    lastHitId = (int)segmentPairs[hitIndeces[1]].id;

            //                }//If not being occluded, then the next point could be another occluder
            //                else
            //                {
            //                    //Crash?
            //                    finalArc.endingAngle = tempCollisionBuffer[1].theta;
            //                    finalArc.endingDistSqr = tempCollisionBuffer[1].B2.x;
            //                    finalArc.point2 = new Vector3(tempCollisionBuffer[1].A1.x, 0, tempCollisionBuffer[1].A1.y);
            //                    finalArcId = (int)tempCollisionBuffer[1].id;
            //                    //Establish arc start at first segment start:
            //                    VA.startingAngle = tempCollisionBuffer[0].theta;
            //                    VA.startingDistSqr = tempCollisionBuffer[0].B2.x;
            //                    VA.point1 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
            //                    //Establish arc end at solid hit on occluding segment
            //                    VA.endingAngle = segmentPairs[hitIndeces[0]].theta;
            //                    VA.endingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
            //                    VA.point2 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
            //                    //Commit arc to memory.
            //                    visionArcs.Add(VA);
            //                    //Establish next arc start:
            //                    VA.startingAngle = segmentPairs[hitIndeces[1]].theta;
            //                    VA.startingDistSqr = segmentPairs[hitIndeces[1]].B2.x;
            //                    VA.point1 = new Vector3(segmentPairs[hitIndeces[1]].A1.x, 0, segmentPairs[hitIndeces[1]].A1.y);
            //                    //Record last hit id:
            //                    lastHitId = (int)segmentPairs[hitIndeces[1]].id;
            //                }
            //            }
            //            else //We started off with only 1 collision..
            //            {
            //                //Check if we're being occluded
            //                if ((int)segmentPairs[hitIndeces[0]].id/2 == (int)tempCollisionBuffer[0].id/2)
            //                {

            //                    finalArc.endingAngle = tempCollisionBuffer[0].theta;
            //                    finalArc.endingDistSqr = tempCollisionBuffer[0].B2.x;
            //                    finalArc.point2 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
            //                    finalArcId = (int)tempCollisionBuffer[0].id;

            //                    VA.startingAngle = tempCollisionBuffer[0].theta;
            //                    VA.startingDistSqr = tempCollisionBuffer[0].B2.x;
            //                    VA.point1 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
            //                    //Establish arc end at solid hit on occluding segment
            //                    VA.endingAngle = segmentPairs[hitIndeces[0]].theta;
            //                    VA.endingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
            //                    VA.point2 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
            //                    //Commit arc to memory.
            //                    visionArcs.Add(VA);
            //                    //Establish next arc start:
            //                    VA.startingAngle = segmentPairs[hitIndeces[1]].theta;
            //                    VA.startingDistSqr = segmentPairs[hitIndeces[1]].B2.x;
            //                    VA.point1 = new Vector3(segmentPairs[hitIndeces[1]].A1.x, 0, segmentPairs[hitIndeces[1]].A1.y);
            //                    //Record last hit id:
            //                    lastHitId = (int)segmentPairs[hitIndeces[1]].id;
            //                }
            //                else
            //                {
            //                    //We've hit the end of the segment
            //                    finalArc.endingAngle = tempCollisionBuffer[0].theta;
            //                    finalArc.endingDistSqr = tempCollisionBuffer[0].B2.x;
            //                    finalArc.point2 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
            //                    finalArcId = (int)tempCollisionBuffer[0].id;

            //                    VA.startingAngle = tempCollisionBuffer[0].theta;
            //                    VA.startingDistSqr = tempCollisionBuffer[0].B2.x;
            //                    VA.point1 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
            //                    //Establish arc end at solid hit on occluding segment
            //                    VA.endingAngle = segmentPairs[hitIndeces[1]].theta;
            //                    VA.endingDistSqr = segmentPairs[hitIndeces[1]].B2.x;
            //                    VA.point2 = new Vector3(segmentPairs[hitIndeces[1]].A1.x, 0, segmentPairs[hitIndeces[1]].A1.y);
            //                    //Commit arc to memory.
            //                    visionArcs.Add(VA);
            //                    //Establish next arc start:
            //                    VA.startingAngle = segmentPairs[hitIndeces[0]].theta;
            //                    VA.startingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
            //                    VA.point1 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
            //                    //Record last hit id:
            //                    lastHitId = (int)segmentPairs[hitIndeces[0]].id;
            //                }
            //            }
            //        }
            //        else
            //        {
            //            //We only have 1 collision
            //            if(tempCollisionBuffer.Count > 1)
            //            {
            //                //Compare our 1 recent collision to our 2 previous hits
            //                if((int)tempCollisionBuffer[1].id /2  == (int)segmentPairs[hitIndeces[0]].id/2)//If our target has the same id as what we've hit...
            //                {
            //                    //If our new collision matches our new point then our segment is obscuring something.
            //                    finalArc.endingAngle = tempCollisionBuffer[0].theta;
            //                    finalArc.endingDistSqr = tempCollisionBuffer[0].B2.x;
            //                    finalArc.point2 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
            //                    finalArcId = (int)tempCollisionBuffer[0].id;

            //                    VA.startingAngle = tempCollisionBuffer[1].theta;
            //                    VA.startingDistSqr = tempCollisionBuffer[1].B2.x;
            //                    VA.point1 = new Vector3(tempCollisionBuffer[1].A1.x, 0, tempCollisionBuffer[1].A1.y);
            //                    //Establish arc end at solid hit on occluding segment
            //                    VA.endingAngle = segmentPairs[hitIndeces[0]].theta;
            //                    VA.endingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
            //                    VA.point2 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
            //                    //Commit arc to memory.
            //                    visionArcs.Add(VA);
            //                    //Establish next arc start:
            //                    VA.startingAngle = segmentPairs[hitIndeces[0]].theta;
            //                    VA.startingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
            //                    VA.point1 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
            //                    //Record last hit id:
            //                    lastHitId = (int)segmentPairs[hitIndeces[0]].id;
            //                }
            //                else //it must match our first point right? 
            //                {

            //                    finalArc.endingAngle = tempCollisionBuffer[1].theta;
            //                    finalArc.endingDistSqr = tempCollisionBuffer[1].B2.x;
            //                    finalArc.point2 = new Vector3(tempCollisionBuffer[1].A1.x, 0, tempCollisionBuffer[1].A1.y);
            //                    finalArcId = (int)tempCollisionBuffer[0].id;

            //                    VA.startingAngle = tempCollisionBuffer[0].theta;
            //                    VA.startingDistSqr = tempCollisionBuffer[0].B2.x;
            //                    VA.point1 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
            //                    //Establish arc end at solid hit on occluding segment
            //                    VA.endingAngle = segmentPairs[hitIndeces[0]].theta;
            //                    VA.endingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
            //                    VA.point2 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
            //                    //Commit arc to memory.
            //                    visionArcs.Add(VA);
            //                    //Establish next arc start:
            //                    VA.startingAngle = segmentPairs[hitIndeces[0]].theta;
            //                    VA.startingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
            //                    VA.point1 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
            //                    //Record last hit id:
            //                    lastHitId = (int)segmentPairs[hitIndeces[0]].id;
            //                }
            //            }
            //            else // We have 1 collision to start with and we just got 1 collision
            //            {
            //                //Only 1 way this can be:
            //                finalArc.endingAngle = tempCollisionBuffer[0].theta;
            //                finalArc.endingDistSqr = tempCollisionBuffer[0].B2.x;
            //                finalArc.point2 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
            //                finalArcId = (int)tempCollisionBuffer[0].id;

            //                VA.startingAngle = tempCollisionBuffer[0].theta;
            //                VA.startingDistSqr = tempCollisionBuffer[0].B2.x;
            //                VA.point1 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
            //                //Establish arc end at solid hit on occluding segment
            //                VA.endingAngle = segmentPairs[hitIndeces[0]].theta;
            //                VA.endingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
            //                VA.point2 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
            //                //Commit arc to memory.
            //                visionArcs.Add(VA);
            //                //Establish next arc start:
            //                VA.startingAngle = segmentPairs[hitIndeces[0]].theta;
            //                VA.startingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
            //                VA.point1 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
            //                //Record last hit id:
            //                lastHitId = (int)segmentPairs[hitIndeces[0]].id;
            //            }
            //        }
            //    }
            //    else //This isn't one of our nightmare bootstrapping cases. Proceed as normal
            //    {
            //        if (hitIndeces.Count > 1) //if we have multiple collisions.
            //        {

            //            if ((int)segmentPairs[hitIndeces[1]].id / 2 == lastHitId /2)
            //            {
            //                //We're occluding something:
            //                VA.endingAngle = segmentPairs[hitIndeces[1]].theta;
            //                VA.endingDistSqr = segmentPairs[hitIndeces[1]].B2.x;
            //                VA.point2 = new Vector3(segmentPairs[hitIndeces[1]].A1.x, 0, segmentPairs[hitIndeces[1]].A1.y);
            //                visionArcs.Add(VA);
            //                VA.startingAngle = segmentPairs[hitIndeces[0]].theta;
            //                VA.startingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
            //                VA.point1 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
            //                //Record last hit id:
            //                lastHitId = (int)segmentPairs[hitIndeces[0]].id;
            //            }
            //            else//We are being occluded.
            //            {
            //                VA.endingAngle = segmentPairs[hitIndeces[0]].theta;
            //                VA.endingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
            //                VA.point2 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
            //                visionArcs.Add(VA);
            //                VA.startingAngle = segmentPairs[hitIndeces[1]].theta;
            //                VA.startingDistSqr = segmentPairs[hitIndeces[1]].B2.x;
            //                VA.point1 = new Vector3(segmentPairs[hitIndeces[1]].A1.x, 0, segmentPairs[hitIndeces[1]].A1.y);
            //                //Record last hit id:
            //                lastHitId = (int)segmentPairs[hitIndeces[1]].id;
            //            }
            //        }
            //        else
            //        {
            //            //If we hit the same segment, we didn't gain any information (ignore this case if this is the last point for safety)
            //            if(i != points.Count -1 && lastHitId / 2 == (int)segmentPairs[hitIndeces[0]].id / 2 && segmentPairs[hitIndeces[0]].id > 3)
            //            {
            //                //No new information gained, ignore triangle.
            //                continue;
            //            }
            //            //we only have 1 collision.
            //            VA.endingAngle = segmentPairs[hitIndeces[0]].theta;
            //            VA.endingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
            //            VA.point2 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
            //            visionArcs.Add(VA);
            //            VA.startingAngle = segmentPairs[hitIndeces[0]].theta;
            //            VA.startingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
            //            VA.point1 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
            //            //Record last hit id:
            //            lastHitId = (int)segmentPairs[hitIndeces[0]].id;
            //        }
            //    }

            //    //Remove segment if this is the second time we've encountered this segment.
            //    if (segmentProcessed)
            //    {
            //        activeSegments.Remove(points[i].segmentId);
            //    }
            //}
            ////Final Stitch up
            //VA.endingAngle = finalArc.endingAngle;
            //VA.endingDistSqr = finalArc.endingDistSqr;
            //VA.point2 = finalArc.point2;
            //visionArcs.Add(VA);

            //player.hasChanged = false;
            //########################################### VERSION 2 ####################################
            lastHitId = -1;
            //Every frame the active segments and collisions are unique.
            Dictionary<int, Segment> activeSegments = new Dictionary<int, Segment>();
            collisions = new List<RaySegmentPair>();
            visionArcs = new List<VisionArc>();
            VisionArc VA = new VisionArc();
            VisionArc finalArc = new VisionArc();
            int finalArcId = -1;
            List<RaySegmentPair> tempCollisionBuffer = new List<RaySegmentPair>(); // Used for first collision (we need more context to identify order of points)
            Vector2 origin = new Vector2(player.position.x, player.position.z);


            //Points are to be processed in order of angle relative to player.
            for (int i = 0; i < points.Count; i++)
            {
                points[i].recalculateTheta(origin);
            }
            points.Sort((a, b) => a.theta.CompareTo(b.theta));
            //At this point we know the starting angle of our first vision arc:
            VA.startingAngle = points[0].theta;

            //for (int i = 0; i < points.Count; i++)
            //{
            //    Debug.Log(string.Format("Point:{0} Angle:{1}", i, points[i].theta));
            //}
            //Initialize active segments: Points that start after 1.5Rad and end before 0.5R

            List<Segment> startingSegments = segments.FindAll(x => (x.A.theta > points[0].theta && x.B.theta < points[0].theta) || (x.A.theta < points[0].theta && x.B.theta > points[0].theta));

            foreach (Segment seg in segments) //instead of starting segments
            {
                activeSegments.Add(seg.id, seg);
            }

            //Iterate through points.
            for (int i = 0; i < points.Count; i++)
            {
                Debug.Log(points[i].theta / Mathf.PI);
                bool segmentProcessed = false;
                if (activeSegments.ContainsKey(points[i].segmentId))
                {
                    //Then we need to remove this segment at the end.
                    segmentProcessed = true;
                }
                else
                {
                    //activeSegments.Add(points[i].segmentId, segments[points[i].segmentId]);
                }

                //Compare this ray to every active segment
                //Create segment pairs:
                int j = 0;
                RaySegmentPair[] segmentPairs = new RaySegmentPair[activeSegments.Count];
                int targetIndex = 0;
                foreach (Segment s in activeSegments.Values)
                {
                    if (points[i].segmentId == s.id)
                    {
                        targetIndex = j;
                    }
                    segmentPairs[j] = createRaySegmentPair(origin, points[i].coor, s.A.coor, s.B.coor, s.id, points[i].theta);
                    j++;
                }

                List<int> hitIndeces = FindClosestIntersect(segmentPairs);
                //If this is the first point, we have special measures:
                if (i == 0)
                {
                    //First point to be processed requires special measures since we don't have any vision arcs yet.
                    if (hitIndeces.Count > 1)
                    {
                        //We have 2 points, load them into the buffer
                        tempCollisionBuffer.Add(segmentPairs[hitIndeces[0]]);
                        tempCollisionBuffer.Add(segmentPairs[hitIndeces[1]]);
                    }
                    else
                    {
                        //We only have 1 point, add it to the buffer.
                        tempCollisionBuffer.Add(segmentPairs[hitIndeces[0]]);
                    }
                }
                else if (i == 1) //This is our second collision check, Make sense of our first collision using the second.
                {
                    if (hitIndeces.Count > 1)
                    {
                        //Figure out if our second collision --the target index not the dead on collision--
                        //has the same id as either of our points.
                        //We want to match the closest collision if possible
                        if (tempCollisionBuffer.Count > 1)
                        {
                            if ((int)segmentPairs[hitIndeces[1]].id / 2 == tempCollisionBuffer[1].id / 2)
                            {
                                // Our target indexes match so they form the first segment.
                                //Our other point must form our final segment:
                                finalArc.endingAngle = tempCollisionBuffer[0].theta;
                                finalArc.endingDistSqr = tempCollisionBuffer[0].B2.x;
                                finalArc.point2 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
                                finalArcId = (int)tempCollisionBuffer[0].id;
                                //Establish arc start:
                                VA.startingAngle = tempCollisionBuffer[1].theta;
                                VA.startingDistSqr = tempCollisionBuffer[1].B2.x;
                                VA.point1 = new Vector3(tempCollisionBuffer[1].A1.x, 0, tempCollisionBuffer[1].A1.y);
                                //Establish arc end:
                                VA.endingAngle = segmentPairs[hitIndeces[1]].theta;
                                VA.endingDistSqr = segmentPairs[hitIndeces[1]].B2.x;
                                VA.point2 = new Vector3(segmentPairs[hitIndeces[1]].A1.x, 0, segmentPairs[hitIndeces[1]].A1.y);

                                //Commit arc to memory.
                                visionArcs.Add(VA);

                                //Establish next arc start:
                                VA.startingAngle = segmentPairs[hitIndeces[0]].theta;
                                VA.startingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
                                VA.point1 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
                                //Record last hit id:
                                lastHitId = (int)segmentPairs[hitIndeces[0]].id;
                            }//if the id's of the two targets don't match then check to see if our segment is being occluded by a new segment:
                             //Occlusion would give us a primary collision matching our target id:
                            else if ((int)segmentPairs[hitIndeces[0]].id / 2 == (int)tempCollisionBuffer[1].id / 2)
                            {
                                //
                                finalArc.endingAngle = tempCollisionBuffer[0].theta;
                                finalArc.endingDistSqr = tempCollisionBuffer[0].B2.x;
                                finalArc.point2 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
                                finalArcId = (int)tempCollisionBuffer[0].id;
                                //Establish arc start at first segment start:
                                VA.startingAngle = tempCollisionBuffer[1].theta;
                                VA.startingDistSqr = tempCollisionBuffer[1].B2.x;
                                VA.point1 = new Vector3(tempCollisionBuffer[1].A1.x, 0, tempCollisionBuffer[1].A1.y);
                                //Establish arc end at solid hit on occluding segment
                                VA.endingAngle = segmentPairs[hitIndeces[0]].theta;
                                VA.endingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
                                VA.point2 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
                                //Commit arc to memory.
                                visionArcs.Add(VA);
                                //Establish next arc start:
                                VA.startingAngle = segmentPairs[hitIndeces[1]].theta;
                                VA.startingDistSqr = segmentPairs[hitIndeces[1]].B2.x;
                                VA.point1 = new Vector3(segmentPairs[hitIndeces[1]].A1.x, 0, segmentPairs[hitIndeces[1]].A1.y);
                                //Record last hit id:
                                lastHitId = (int)segmentPairs[hitIndeces[1]].id;

                            }//If not being occluded, then the next point could be another occluder
                            else
                            {
                                //Crash?
                                finalArc.endingAngle = tempCollisionBuffer[1].theta;
                                finalArc.endingDistSqr = tempCollisionBuffer[1].B2.x;
                                finalArc.point2 = new Vector3(tempCollisionBuffer[1].A1.x, 0, tempCollisionBuffer[1].A1.y);
                                finalArcId = (int)tempCollisionBuffer[1].id;
                                //Establish arc start at first segment start:
                                VA.startingAngle = tempCollisionBuffer[0].theta;
                                VA.startingDistSqr = tempCollisionBuffer[0].B2.x;
                                VA.point1 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
                                //Establish arc end at solid hit on occluding segment
                                VA.endingAngle = segmentPairs[hitIndeces[0]].theta;
                                VA.endingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
                                VA.point2 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
                                //Commit arc to memory.
                                visionArcs.Add(VA);
                                //Establish next arc start:
                                VA.startingAngle = segmentPairs[hitIndeces[1]].theta;
                                VA.startingDistSqr = segmentPairs[hitIndeces[1]].B2.x;
                                VA.point1 = new Vector3(segmentPairs[hitIndeces[1]].A1.x, 0, segmentPairs[hitIndeces[1]].A1.y);
                                //Record last hit id:
                                lastHitId = (int)segmentPairs[hitIndeces[1]].id;
                            }
                        }
                        else //We started off with only 1 collision..
                        {
                            //Check if we're being occluded
                            if ((int)segmentPairs[hitIndeces[0]].id / 2 == (int)tempCollisionBuffer[0].id / 2)
                            {

                                finalArc.endingAngle = tempCollisionBuffer[0].theta;
                                finalArc.endingDistSqr = tempCollisionBuffer[0].B2.x;
                                finalArc.point2 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
                                finalArcId = (int)tempCollisionBuffer[0].id;

                                VA.startingAngle = tempCollisionBuffer[0].theta;
                                VA.startingDistSqr = tempCollisionBuffer[0].B2.x;
                                VA.point1 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
                                //Establish arc end at solid hit on occluding segment
                                VA.endingAngle = segmentPairs[hitIndeces[0]].theta;
                                VA.endingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
                                VA.point2 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
                                //Commit arc to memory.
                                visionArcs.Add(VA);
                                //Establish next arc start:
                                VA.startingAngle = segmentPairs[hitIndeces[1]].theta;
                                VA.startingDistSqr = segmentPairs[hitIndeces[1]].B2.x;
                                VA.point1 = new Vector3(segmentPairs[hitIndeces[1]].A1.x, 0, segmentPairs[hitIndeces[1]].A1.y);
                                //Record last hit id:
                                lastHitId = (int)segmentPairs[hitIndeces[1]].id;
                            }
                            else
                            {
                                //We've hit the end of the segment
                                finalArc.endingAngle = tempCollisionBuffer[0].theta;
                                finalArc.endingDistSqr = tempCollisionBuffer[0].B2.x;
                                finalArc.point2 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
                                finalArcId = (int)tempCollisionBuffer[0].id;

                                VA.startingAngle = tempCollisionBuffer[0].theta;
                                VA.startingDistSqr = tempCollisionBuffer[0].B2.x;
                                VA.point1 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
                                //Establish arc end at solid hit on occluding segment
                                VA.endingAngle = segmentPairs[hitIndeces[1]].theta;
                                VA.endingDistSqr = segmentPairs[hitIndeces[1]].B2.x;
                                VA.point2 = new Vector3(segmentPairs[hitIndeces[1]].A1.x, 0, segmentPairs[hitIndeces[1]].A1.y);
                                //Commit arc to memory.
                                visionArcs.Add(VA);
                                //Establish next arc start:
                                VA.startingAngle = segmentPairs[hitIndeces[0]].theta;
                                VA.startingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
                                VA.point1 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
                                //Record last hit id:
                                lastHitId = (int)segmentPairs[hitIndeces[0]].id;
                            }
                        }
                    }
                    else
                    {
                        //We only have 1 collision
                        if (tempCollisionBuffer.Count > 1)
                        {
                            //Compare our 1 recent collision to our 2 previous hits
                            if ((int)tempCollisionBuffer[1].id / 2 == (int)segmentPairs[hitIndeces[0]].id / 2)//If our target has the same id as what we've hit...
                            {
                                //If our new collision matches our new point then our segment is obscuring something.
                                finalArc.endingAngle = tempCollisionBuffer[0].theta;
                                finalArc.endingDistSqr = tempCollisionBuffer[0].B2.x;
                                finalArc.point2 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
                                finalArcId = (int)tempCollisionBuffer[0].id;

                                VA.startingAngle = tempCollisionBuffer[1].theta;
                                VA.startingDistSqr = tempCollisionBuffer[1].B2.x;
                                VA.point1 = new Vector3(tempCollisionBuffer[1].A1.x, 0, tempCollisionBuffer[1].A1.y);
                                //Establish arc end at solid hit on occluding segment
                                VA.endingAngle = segmentPairs[hitIndeces[0]].theta;
                                VA.endingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
                                VA.point2 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
                                //Commit arc to memory.
                                visionArcs.Add(VA);
                                //Establish next arc start:
                                VA.startingAngle = segmentPairs[hitIndeces[0]].theta;
                                VA.startingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
                                VA.point1 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
                                //Record last hit id:
                                lastHitId = (int)segmentPairs[hitIndeces[0]].id;
                            }
                            else //it must match our first point right? 
                            {

                                finalArc.endingAngle = tempCollisionBuffer[1].theta;
                                finalArc.endingDistSqr = tempCollisionBuffer[1].B2.x;
                                finalArc.point2 = new Vector3(tempCollisionBuffer[1].A1.x, 0, tempCollisionBuffer[1].A1.y);
                                finalArcId = (int)tempCollisionBuffer[0].id;

                                VA.startingAngle = tempCollisionBuffer[0].theta;
                                VA.startingDistSqr = tempCollisionBuffer[0].B2.x;
                                VA.point1 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
                                //Establish arc end at solid hit on occluding segment
                                VA.endingAngle = segmentPairs[hitIndeces[0]].theta;
                                VA.endingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
                                VA.point2 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
                                //Commit arc to memory.
                                visionArcs.Add(VA);
                                //Establish next arc start:
                                VA.startingAngle = segmentPairs[hitIndeces[0]].theta;
                                VA.startingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
                                VA.point1 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
                                //Record last hit id:
                                lastHitId = (int)segmentPairs[hitIndeces[0]].id;
                            }
                        }
                        else // We have 1 collision to start with and we just got 1 collision
                        {
                            //Only 1 way this can be:
                            finalArc.endingAngle = tempCollisionBuffer[0].theta;
                            finalArc.endingDistSqr = tempCollisionBuffer[0].B2.x;
                            finalArc.point2 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
                            finalArcId = (int)tempCollisionBuffer[0].id;

                            VA.startingAngle = tempCollisionBuffer[0].theta;
                            VA.startingDistSqr = tempCollisionBuffer[0].B2.x;
                            VA.point1 = new Vector3(tempCollisionBuffer[0].A1.x, 0, tempCollisionBuffer[0].A1.y);
                            //Establish arc end at solid hit on occluding segment
                            VA.endingAngle = segmentPairs[hitIndeces[0]].theta;
                            VA.endingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
                            VA.point2 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
                            //Commit arc to memory.
                            visionArcs.Add(VA);
                            //Establish next arc start:
                            VA.startingAngle = segmentPairs[hitIndeces[0]].theta;
                            VA.startingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
                            VA.point1 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
                            //Record last hit id:
                            lastHitId = (int)segmentPairs[hitIndeces[0]].id;
                        }
                    }
                }
                else //This isn't one of our nightmare bootstrapping cases. Proceed as normal
                {
                    if (hitIndeces.Count > 1) //if we have multiple collisions.
                    {

                        if ((int)segmentPairs[hitIndeces[1]].id / 2 == lastHitId / 2)
                        {
                            //We're occluding something:
                            VA.endingAngle = segmentPairs[hitIndeces[1]].theta;
                            VA.endingDistSqr = segmentPairs[hitIndeces[1]].B2.x;
                            VA.point2 = new Vector3(segmentPairs[hitIndeces[1]].A1.x, 0, segmentPairs[hitIndeces[1]].A1.y);
                            visionArcs.Add(VA);
                            VA.startingAngle = segmentPairs[hitIndeces[0]].theta;
                            VA.startingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
                            VA.point1 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
                            //Record last hit id:
                            lastHitId = (int)segmentPairs[hitIndeces[0]].id;
                        }
                        else//We are being occluded.
                        {
                            VA.endingAngle = segmentPairs[hitIndeces[0]].theta;
                            VA.endingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
                            VA.point2 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
                            visionArcs.Add(VA);
                            VA.startingAngle = segmentPairs[hitIndeces[1]].theta;
                            VA.startingDistSqr = segmentPairs[hitIndeces[1]].B2.x;
                            VA.point1 = new Vector3(segmentPairs[hitIndeces[1]].A1.x, 0, segmentPairs[hitIndeces[1]].A1.y);
                            //Record last hit id:
                            lastHitId = (int)segmentPairs[hitIndeces[1]].id;
                        }
                    }
                    else
                    {
                        //If we hit the same segment, we didn't gain any information (ignore this case if this is the last point for safety)
                        if (i != points.Count - 1 && lastHitId / 2 == (int)segmentPairs[hitIndeces[0]].id / 2 && segmentPairs[hitIndeces[0]].id > 3)
                        {
                            //No new information gained, ignore triangle.
                            continue;
                        }
                        //we only have 1 collision.
                        VA.endingAngle = segmentPairs[hitIndeces[0]].theta;
                        VA.endingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
                        VA.point2 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
                        visionArcs.Add(VA);
                        VA.startingAngle = segmentPairs[hitIndeces[0]].theta;
                        VA.startingDistSqr = segmentPairs[hitIndeces[0]].B2.x;
                        VA.point1 = new Vector3(segmentPairs[hitIndeces[0]].A1.x, 0, segmentPairs[hitIndeces[0]].A1.y);
                        //Record last hit id:
                        lastHitId = (int)segmentPairs[hitIndeces[0]].id;
                    }
                }

                //Remove segment if this is the second time we've encountered this segment.
                if (segmentProcessed)
                {
                    //activeSegments.Remove(points[i].segmentId);
                }
            }
            //Final Stitch up
            VA.endingAngle = finalArc.endingAngle;
            VA.endingDistSqr = finalArc.endingDistSqr;
            VA.point2 = finalArc.point2;
            visionArcs.Add(VA);

            player.hasChanged = false;
        }

        


        //Draw segments used for collision detection:
        for (int i = 0; i < segments.Count; i++)
        {
            Debug.DrawLine(segments[i].A.toVector3(), segments[i].B.toVector3(), Color.green);
        }
        //Draw outlines of objects
        for (int i = 0; i < outlines.Count; i++)
        {
            Debug.DrawLine(outlines[i].A.toVector3(), outlines[i].B.toVector3(), Color.white);
        }
        //Draw things
        for (int i = 0; i < collisions.Count; i++)
        {
            if(i == 0 || i == collisions.Count -1)
            {
                debugDrawCollision(collisions[i].A1, player.position.y, Color.magenta);
            }
            else
            {
                debugDrawCollision(collisions[i].A1, player.position.y, Color.yellow);
            }
            Debug.DrawLine(player.position, new Vector3(collisions[i].A1.x, player.position.y, collisions[i].A1.y), Color.Lerp(Color.red, Color.blue, (float)i / collisions.Count));
        }
        //Render vision arcs in use:
        for (int i = 0; i < visionArcs.Count; i++)
        {
            debugDrawVisionArc(visionArcs[i], player.position, new Color(1, 0.64f, 0));
            debugDrawVisionArc(visionArcs[i], player.position, Color.grey);
            //debugDrawVisionArc(visionArcs[i], player.position,Color.Lerp(Color.red,Color.blue,(float)i/visionArcs.Count));
        }
        //See if the point we hit with our mouse Raycast is in one of our vision arcs.
        RaycastHit hit;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out hit))
        {
            Transform objectHit = hit.transform;
            int arcHit = TestVisibility(hit.point);
            if (arcHit != -1)
            {
                debugDrawVisionArc(visionArcs[arcHit], player.position, Color.red);
            }
            Debug.DrawLine(player.position, hit.point);
        }
    }
    bool TestVisionArc(Vector3 point, VisionArc VA)
    {
        //Calculate angle of point:
        //Vector3 p = point + player.position;
        float theta = Mathf.Atan2(point.z - player.position.z, point.x - player.position.x);
        theta = theta < 0 ? theta + 2*Mathf.PI : theta;
        float arcStart;
        float arcEnd;
        float startDist;
        float endDist;
        Vector2 p = new Vector2(point.x - player.position.x, point.z - player.position.z);
        float dist = p.sqrMagnitude;
        //dist = (p.x * p.x) + (p.y * p.y); // ### not needed ###
        arcStart = VA.startingAngle < 0 ?  2*Mathf.PI + VA.startingAngle : VA.startingAngle;
        arcEnd =   VA.endingAngle   < 0 ?  2*Mathf.PI + VA.endingAngle   : VA.endingAngle;
        startDist = VA.startingDistSqr;
        endDist = VA.endingDistSqr;
        Vector2 root = new Vector2(player.position.x, player.position.z);
        Vector2 P1 = new Vector2(VA.point1.x,VA.point1.z) - root;
        Vector2 P2 = new Vector2(VA.point2.x,VA.point2.z) - root;
        //Sort angles into ascending order if needed
        //Debug.DrawLine(new Vector3(P1.x,0,P1.y), Vector3.zero);

        if (arcStart > arcEnd)
        {
            //Swap angles.
            float tempfloat = arcStart;
            arcStart = arcEnd;
            arcEnd = tempfloat;
            //Swap dists
            endDist = VA.startingDistSqr;
            startDist = VA.endingDistSqr;

            //Swap arc points
            P1 = new Vector2(VA.point2.x, VA.point2.z) - root;
            P2 = new Vector2(VA.point1.x, VA.point1.z) - root;
        }

        Debug.DrawLine(player.position, point);
        
        if(arcEnd - arcStart > Mathf.PI)
        {
            //Use interior angle
            if(theta > arcEnd || theta < arcStart)
            {
                Debug.Log(string.Format("{0}   {1}   {2}", arcStart, theta, arcEnd));
                return PointInTriangle(p, P1, P2, Vector2.zero);
                
            }
        }
        else if(arcStart < theta && arcEnd > theta)
        {
            //float t = (theta - arcStart) / (arcEnd - arcStart);
            //Debug.Log(string.Format("Angle:{0}, Ratio:{1}",t,startDist/endDist));
            Debug.Log(string.Format("{0}   {1}   {2}", arcStart, theta, arcEnd));
            return PointInTriangle(p, P1, P2, Vector2.zero);


        }
        return false;
    }

    int angleInRange(float A, float B, float C)
    {
        //Function for binary searching arcs.
        //Make the smaller value the lower bound.
        float angleStart = A > B ? B : A;
        float angleEnd = A > B ? A : B;
        if (angleEnd - angleStart > Mathf.PI)
        {
            //Our range exist across the seam.
            if (C > angleEnd || C < angleStart)
            {
                return 0;
            }
            else if (C < angleEnd && C > angleStart)
            {
                return 1;
            }
            else
            {
                return -1;
            }
        }
        else
        {
            //Standard test:
            if (C < angleEnd && C > angleStart)
            {
                return 0;
            }
            else if (C < angleStart)
            {
                return -1;
            }
            else
            {
                return 1;
            }
        }
    }

    int TestVisibility(Vector3 point)
    {
        //TOOD: Put in safety check if we somehow don't find an arc that works
        //Find out which vision arc we should test:
        int index = visionArcs.Count / 2;
        int test = 0;
        float theta = Mathf.Atan2(point.z - player.position.z, point.x - player.position.x);
        theta = theta < 0 ? theta + (2 * Mathf.PI) : theta;

        //Handle Edge case of searching
        if(angleInRange(visionArcs[visionArcs.Count-1].startingAngle, visionArcs[visionArcs.Count - 1].endingAngle,theta)== 0)
        {
            if (TestVisionArc(point, visionArcs[visionArcs.Count-1]))
            {
                return visionArcs.Count-1;
            }
        }
        //Iterative binary search:
        int beg = 0;
        int end = visionArcs.Count - 1;

        Color[] colours =
        {
            Color.red,
            Color.yellow,
            Color.green,
            Color.cyan,
            Color.blue,
            Color.magenta
        };
        int colCount = -1;
        while (beg <= end)
        {
            colCount++;
            index = (beg + end) / 2;
            float arcStart = visionArcs[index].startingAngle < 0 ? 2 * Mathf.PI + visionArcs[index].startingAngle : visionArcs[index].startingAngle;
            float arcEnd = visionArcs[index].endingAngle < 0 ? 2 * Mathf.PI + visionArcs[index].endingAngle : visionArcs[index].endingAngle;
            debugDrawVisionArc(visionArcs[index], player.position, colours[(int)Mathf.Min(colCount, colours.Length - 1)]);
            test = angleInRange(arcStart,arcEnd,theta);
            if(test == 0)
            {
                debugDrawVisionArc(visionArcs[index], player.position, Color.blue);
                Vector2 A = new Vector2(player.transform.position.x, player.transform.position.z);
                Vector2 B = new Vector2(visionArcs[index].point1.x, visionArcs[index].point1.z);
                Vector2 C = new Vector2(visionArcs[index].point2.x, visionArcs[index].point2.z);
                if(TestVisionArc(point,visionArcs[index]))
                {
                    return index;
                }
                else
                {
                    return -1;
                }
            }
            else if(test == 1)
            {
                beg = index + 1;
            }
            else
            {
                end = index - 1;
            }
        }
        
        return -1;
    }
}
