using Fabolus.Core.BolusModel;
using Fabolus.Core.Common;
using g3;
using System.Collections.Generic;
using TriangleNet.Geometry;
using TriangleNet.Meshing;

namespace Fabolus.Core.Mould.Builders;
public sealed record SimpleMouldGenerator {
    private DMesh3 BolusReference { get; set; } //mesh to invert while entirely within
    private double MaxHeight { get; set; } = 100.0;
    private double MinHeight { get; set; } = -100.0;
    private double OffsetXY { get; set; } = 3.0;
    private double OffsetTop { get; set; } = 3.0;
    private double OffsetBottom { get; set; } = 3.0;
    private double Resolution { get; set; } = 1.0;
    private DMesh3[] ToolMeshes { get; set; } = []; // mesh to boolean subtract from the mold

    public static SimpleMouldGenerator New() => new();
    public SimpleMouldGenerator WithBottomOffset(double offset) => this with { OffsetBottom = offset };
    public SimpleMouldGenerator WithBolus(DMesh3 bolus) => this with { BolusReference = bolus };
    public SimpleMouldGenerator WithHeights(double maxHeight, double minHeight) => this with { MaxHeight = maxHeight, MinHeight = minHeight };
    public SimpleMouldGenerator WithOffsets(double offset) => this with { OffsetTop = offset, OffsetBottom = offset, OffsetXY = offset };
    public SimpleMouldGenerator WithResolution(int resolution) => this with { Resolution = resolution };
    public SimpleMouldGenerator WithToolMeshes(DMesh3[] toolMeshes) => this with { ToolMeshes = toolMeshes };
    public SimpleMouldGenerator WithTopOffset(double offset) => this with { OffsetTop = offset };
    public SimpleMouldGenerator WithXYOffsets(double offset) => this with { OffsetXY = offset };

    public DMesh3 BuildMesh() {
        if (BolusReference is null) { throw new NullReferenceException("BuildMesh: Bolus mesh is null"); }
        var mould = BuildPreview();


        return mould;
    }

    public DMesh3 BuildPreview() {
        if (BolusReference is null) { throw new NullReferenceException("BuildPreview: Bolus mesh is null"); }

        var numberOfCells = (int)Math.Ceiling(BolusReference.CachedBounds.MaxDim / Resolution);
        var maxBolusHeight = (float)(BolusReference.CachedBounds.Max.z + BolusReference.CachedBounds.Height);
        var maxHeight = maxBolusHeight + OffsetTop;
        var heightOffset = maxHeight - (maxBolusHeight + OffsetTop);

        var offsetMesh = MoldUtils.OffsetMeshD(BolusReference, OffsetXY);

        return CalculateContour(offsetMesh);
    }

    private List<Vector3d> GetContour(DMesh3 mesh, double resolution = 1.0f, int padding = 3) {
        if (mesh is null) { return null; }

        var spatial = new DMeshAABBTree3(mesh, true);

        //try a hit test for each cube?
        var z = mesh.CachedBounds.Max.z + 2.0f; //where hit tests will start
        var min = new Vector2d(mesh.CachedBounds.Min.x, mesh.CachedBounds.Min.y);
        var max = new Vector2d(mesh.CachedBounds.Max.x, mesh.CachedBounds.Max.y);

        var dimensions = new Index2i(
            (int)((max.x - min.x) / resolution) + padding * 2, //for the negative and plus side of x
            (int)((max.y - min.y) / resolution) + padding * 2
            );

        var map = new bool[dimensions.a, dimensions.b]; //stores the results of hits

        //hit tests to create the boolean map
        var hitRay = new Ray3d(Vector3d.Zero, new Vector3d(0, 0, -1));
        var points = new List<System.Windows.Point>();
        int minX = dimensions.a, minY = dimensions.b;
        for (int x = 0; x < dimensions.a; x++) {
            var xSet = min.x + 0.5f + (x * resolution);
            if (x < minX) { minX = x; }

            for (int y = 0; y < dimensions.b; y++) {
                var ySet = min.y + 0.5f + (y * resolution);
                hitRay.Origin = new Vector3d(xSet, ySet, z);

                var hit = spatial.FindNearestHitTriangle(hitRay);

                if (hit == DMesh3.InvalidID) { continue; }

                map[x, y] = true;

                //used to start the contour

                if (x == minX && y < minY) { minY = y; }
            }
        }

        //use map to make a contour
        var bottom_z = mesh.CachedBounds.Min.z - OffsetBottom + 4.0f;

        var startPoint = new Vector2i(minX, minY);
        var currentPoint = startPoint;
        var nextPoint = startPoint;
        var contour = new List<Vector3d>();

        //track the direction around the mold
        //if subsequent points follw the same direction, no need to add them
        //reduces the contour's points to those only needed
        var direction = GridPosition(0, 1);

        while (true) {
            //currentPoint = nextPoint;
            var nextDirection = NextDirection(map, currentPoint, direction);

            //only add to contour if the new direction is different
            if (nextDirection != direction) contour.Add(new Vector3d(
                (currentPoint.x + 0.5f) * resolution + min.x,
                (currentPoint.y + 0.5) * resolution + min.y,
                bottom_z));

            direction = nextDirection;
            currentPoint += GridPosition(direction);

            //test to see if we should exit the loop
            if (currentPoint == startPoint) {
                contour.Add(contour[0]);//link the last point to the first point
                break;
            }
        }

        return contour;
    }

    private DMesh3 ContourToMesh(List<Vector3d> contour) {
        //get silhouette
        var silhouette = new MeshSilhouette(BolusReference);
        silhouette.Compute(Vector3d.AxisZ);

        //create polygon
        Vector3d[] bottomLoop = contour.Select(v => new Vector3d(v.x, v.y, MinHeight)).ToArray();
        DMesh3 mesh = new();

        int n = bottomLoop.Count();
        var bottomLoopIndices = new int[n];
        for(int i = 0; i < n; i++) {
            bottomLoopIndices[i] = mesh.AppendVertex(bottomLoop[i]);
        }

        var z_offset = 20.0;
        Vector3d[] upperLoop = bottomLoop.Select(v => new Vector3d(v.x, v.y, MaxHeight)).ToArray();
        var upperLoopIndices = new int[n];
        for (int i = 0; i < n; i++) {
            upperLoopIndices[i] = mesh.AppendVertex(upperLoop[i]);
        }

        MeshEditor editor = new(mesh);
        editor.StitchLoop(bottomLoopIndices, upperLoopIndices);

        return editor.Mesh;
    }

    private static int GridPosition(Vector2i position) => GridPosition(position.x, position.y);

    private static int GridPosition(int x, int y) {
        if (x == -1) {
            if (y == -1) return 7;
            if (y == 0) return 8;
            if (y == 1) return 1;
        }
        if (x == 0) {
            if (y == -1) return 6;
            if (y == 0) return 9; //sent the current point, which means we need some point to start. this is most likely to be empty
            if (y == 1) return 2;
        }
        if (x == 1) {
            if (y == -1) return 5;
            if (y == 0) return 4;
            if (y == 1) return 3;
        }

        return 0;
    }

    private static Vector2i GridPosition(int position) {
        switch (position) {
            case 1: return new Vector2i(-1, 1);
            case 2: return new Vector2i(0, 1);
            case 3: return new Vector2i(1, 1);
            case 4: return new Vector2i(1, 0);
            case 5: return new Vector2i(1, -1);
            case 6: return new Vector2i(0, -1);
            case 7: return new Vector2i(-1, -1);
            case 8: return new Vector2i(-1, 0);
            default: return new Vector2i(0, 0);
        }
    }

    /// <summary>
    /// Using GridPosition, determine the next spot on the grid
    /// </summary>
    /// <param name="map"></param>
    /// <param name="currentPoint"></param>
    /// <param name="lastPoint">int value that represents the direction to go</param>
    /// <returns></returns>
    private int NextDirection(bool[,] map, Vector2i currentPoint, int direction) {
        int x = currentPoint.x;
        int y = currentPoint.y;

        //invert the value to start from the last position
        Vector2i pVector = GridPosition(direction);
        pVector *= -1;
        var position = GridPosition(pVector);
        int pX, pY;
        int sizeX = map.GetLength(0);
        int sizeY = map.GetLength(1);

        //cycle clockwise until a new spot is hit
        for (int i = 0; i < 9; i++) {
            position++;
            if (position > 8) { position = 1; }

            pVector = GridPosition(position);
            pX = x + pVector.x;
            pY = y + pVector.y;

            //check if map is true or false at this position
            if (pX < 0 || pX >= sizeX)  { continue; }
            if (pY < 0 || pY >= sizeY) { continue; }
            if (map[pX, pY]) { return position; }

        }

        return -1;
    }

    private DMesh3 CalculateContour(DMesh3 mesh) {
        //get the edges around the mesh
        var contour = GetContour(mesh, Resolution);

        var zHeight = (float)(MaxHeight) + OffsetTop;
        var zBottom = contour[0].z - OffsetBottom;

        //create polygon
        DMesh3 result = new();

        int n = contour.Count();
        Vector3d[] bottomLoop = contour.Select(v => new Vector3d(v.x, v.y, MinHeight)).ToArray();
        var bottomLoopIndices = new int[n];
        for (int i = 0; i < n; i++) {
            bottomLoopIndices[i] = result.AppendVertex(bottomLoop[i]);
        }

        var z_offset = 20.0;
        Vector3d[] upperLoop = bottomLoop.Select(v => new Vector3d(v.x, v.y, MaxHeight)).ToArray();
        var upperLoopIndices = new int[n];
        for (int i = 0; i < n; i++) {
            upperLoopIndices[i] = result.AppendVertex(upperLoop[i]);
        }

        MeshEditor editor = new(result);
        editor.StitchLoop(bottomLoopIndices, upperLoopIndices);

        return editor.Mesh;
    }

}
