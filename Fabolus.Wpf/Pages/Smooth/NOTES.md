# Notes on Implementing Smoothing

## Poisson Recon
- https://mark-borg.github.io/blog/2017/interop/
- a lot of data types there, each you want access to would require marshalling: https://learn.microsoft.com/en-us/dotnet/framework/interop/interop-marshalling
- start, with the tiniest programs on both ends and then grow them out
- get the c++ app to return an int, then a string, then a custom data type
- then load the library as a dll and work on returning data types
- you'll need to make a .dll from c++ to load in c#
- command lines are parsed here: https://github.com/mkazhdan/PoissonRecon/blob/4ffa838cd3ea4348ac02b2883692d4338312150d/Src/PoissonRecon.cpp#L584
- which lead to Execute: https://github.com/mkazhdan/PoissonRecon/blob/4ffa838cd3ea4348ac02b2883692d4338312150d/Src/PoissonRecon.cpp#L272
- porting is your best option here, and chat can help you with the tricky syntax stuff
- use ChatGPT or CoPilot to convert the code over or read it
  
## Marching Cubes

## Mesh distance map
- use MeshQueries.NearestPointDistance
```
using g3;

// ... (assuming 'mesh', 'point', and 'spatial' as above) ...

int nearestTriangleID = spatial.FindNearestTriangle(point); 

DistPoint3Triangle3 distInfo = new DistPoint3Triangle3(point, mesh.GetTriangle(nearestTriangleID));
distInfo.GetSquared(); // Calculate the squared distance

double distance = Math.Sqrt(distInfo.DistanceSquared); 
Console.WriteLine($"Distance from point to mesh: {distance}");
```
- calculate distance for each point
- if within the compared mesh, make value negative
- set spread for values: -2.0mm to 0 to +2.0mm
- upper and lower thresholds setable
- colour for within (red?), normal (green?) and excessive (blue?)
- calculate float value from 0 to 1.0 (-2 to 2)
- set float for each point and export as texture coordinates
- create color gradient at start or when settings change

## Laplacian Smoothing

### Detecting smooth surfaces

using g3; // Core geometry3sharp namespace
using System;
using System.Collections.Generic;
using System.Linq; // Required for ToArray()

/// <summary>
/// Contains utility methods for analyzing DMesh3 objects.
/// </summary>
public static class MeshAnalysis
{
    /// <summary>
    /// Classifies vertices of a mesh based on the dihedral angle between adjacent faces.
    /// Vertices are classified as 'sharp' if they belong to any edge where the
    /// dihedral angle is greater than or equal to the specified threshold.
    /// Otherwise, they are classified as 'smooth'. Boundary edge vertices are
    /// typically classified as sharp by default in this implementation.
    /// </summary>
    /// <param name="mesh">The input mesh (DMesh3).</param>
    /// <param name="sharpEdgeAngleDegrees">The angle threshold in degrees. Edges with a dihedral angle
    /// greater than or equal to this value are considered sharp.</param>
    /// <returns>A tuple containing two integer arrays:
    ///  - smoothVerts: Array of vertex IDs classified as smooth.
    ///  - sharpVerts: Array of vertex IDs classified as sharp.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if the input mesh is null.</exception>
    public static (int[] smoothVerts, int[] sharpVerts) ClassifyVerticesByFaceAngle(
        DMesh3 mesh,
        double sharpEdgeAngleDegrees)
    {
        // --- Input Validation ---
        if (mesh == null)
        {
            throw new ArgumentNullException(nameof(mesh), "Input mesh cannot be null.");
        }

        // --- Precomputation ---
        // Ensure face normals are available for dihedral angle calculation
        if (!mesh.HasTriNormals)
        {
            Console.WriteLine("Computing triangle normals..."); // Optional log
            mesh.ComputeTriangleNormals();
        }

        // Convert the angle threshold from degrees to radians for calculations
        double sharpEdgeAngleRadians = sharpEdgeAngleDegrees * MathUtil.Deg2Rad;

        // --- Data Structures ---
        // Use HashSets for efficient storage and lookup of unique vertex IDs.
        // This automatically handles cases where a vertex belongs to multiple edges.
        HashSet<int> sharpVertSet = new HashSet<int>();
        HashSet<int> smoothVertSet = new HashSet<int>();

        // --- Edge Iteration and Classification ---
        // Iterate through every edge in the mesh
        foreach (int eid in mesh.EdgeIndices())
        {
            Index2i edgeV = mesh.GetEdgeV(eid); // Get the vertex IDs of the edge

            // Handle boundary edges: They don't have two faces to compare.
            // We'll classify their vertices as sharp by default.
            if (mesh.IsBoundaryEdge(eid))
            {
                sharpVertSet.Add(edgeV.a);
                sharpVertSet.Add(edgeV.b);
                continue; // Move to the next edge
            }

            // Get the two faces (triangles) adjacent to this interior edge
            Index2i edgeT = mesh.GetEdgeFaces(eid);

            // Basic sanity check (should generally pass for valid interior edges)
            if (!mesh.IsTriangle(edgeT.a) || !mesh.IsTriangle(edgeT.b))
            {
                 Console.WriteLine($"Warning: Skipping edge {eid} due to invalid adjacent triangle indices.");
                 sharpVertSet.Add(edgeV.a); // Classify as sharp if topology is weird
                 sharpVertSet.Add(edgeV.b);
                 continue;
            }

            // Get the precomputed normal vectors for the two adjacent faces
            Vector3d normalA = mesh.GetTriNormal(edgeT.a);
            Vector3d normalB = mesh.GetTriNormal(edgeT.b);

            // Calculate the cosine of the angle between the normals using the dot product.
            // Normals are expected to be unit length, so no division by magnitude is needed.
            // Clamp the dot product to the valid range [-1, 1] to prevent potential
            // domain errors with Math.Acos due to floating-point inaccuracies.
            double dot = MathUtil.Clamp(normalA.Dot(normalB), -1.0, 1.0);

            // Calculate the dihedral angle (the angle between the faces) in radians
            double dihedralAngleRad = Math.Acos(dot);

            // --- Classification Logic ---
            // Compare the calculated angle with the threshold
            if (dihedralAngleRad >= sharpEdgeAngleRadians)
            {
                // If the angle is sharp, add both edge vertices to the sharp set
                sharpVertSet.Add(edgeV.a);
                sharpVertSet.Add(edgeV.b);
            }
            else
            {
                // If the angle is smooth, add both edge vertices to the smooth set
                // Note: They might be reclassified as sharp later if they also belong to a sharp edge.
                smoothVertSet.Add(edgeV.a);
                smoothVertSet.Add(edgeV.b);
            }
        }

        // --- Finalization ---
        // Ensure sharp classification takes precedence. If a vertex was ever added
        // to the sharp set (because it belongs to *any* sharp edge), remove it
        // from the smooth set.
        smoothVertSet.ExceptWith(sharpVertSet);

        // Convert the HashSets to integer arrays for the return value
        int[] sharpVertsArray = sharpVertSet.ToArray();
        int[] smoothVertsArray = smoothVertSet.ToArray();

        // Return the classified vertex ID arrays
        return (smoothVertsArray, sharpVertsArray);
    }

    // --- Example of how to use the function ---
    /*
    public static void ExampleUsage(DMesh3 myMesh)
    {
        if (myMesh == null || myMesh.VertexCount == 0)
        {
            Console.WriteLine("Mesh is null or empty.");
            return;
        }

        // Ensure the mesh indices are compact for potentially better performance downstream
        // Although not strictly required for this function, it's often good practice.
        if (!myMesh.IsCompact)
        {
             Console.WriteLine("Compacting mesh...");
             myMesh.CompactInPlace();
        }


        double angleThresholdDegrees = 45.0; // Example: Consider edges >= 45 degrees as sharp

        try
        {
            // Call the classification function
            var (smoothVertices, sharpVertices) = ClassifyVerticesByFaceAngle(myMesh, angleThresholdDegrees);

            // Print the results
            Console.WriteLine($"Classification Results (Threshold: {angleThresholdDegrees}°):");
            Console.WriteLine($"- Smooth Vertices: {smoothVertices.Length}");
            // Console.WriteLine($"  IDs: {string.Join(", ", smoothVertices)}"); // Uncomment to print IDs
            Console.WriteLine($"- Sharp Vertices: {sharpVertices.Length}");
            // Console.WriteLine($"  IDs: {string.Join(", ", sharpVertices)}"); // Uncomment to print IDs

            // --- Potential Next Steps ---
            // You could use these vertex ID arrays to:
            // 1. Set constraints for mesh smoothing (e.g., fix sharp vertices)
            //    LaplacianMeshSmoother smoother = new LaplacianMeshSmoother(myMesh);
            //    VertexConstraintSet constraints = new VertexConstraintSet();
            //    foreach (int vid in sharpVertices) {
            //        constraints.Add(vid, new VertexConstraint(true)); // Fixed constraint
            //    }
            //    smoother.Constraint = constraints;
            //    smoother.ConstraintMode = LaplacianMeshSmoother.ConstraintModes.Fixed;
            //    // ... configure other smoother parameters and run smoother.SolveAndUpdateMesh();

            // 2. Visualize the sharp edges/vertices differently.
            // 3. Perform selective mesh operations based on sharpness.
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred during vertex classification: {ex.Message}");
        }
    }
    */
}