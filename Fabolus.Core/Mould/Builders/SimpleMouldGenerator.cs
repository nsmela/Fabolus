using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes;
using Fabolus.Core.Mould.Utils;
using g3;
using gs;

namespace Fabolus.Core.Mould.Builders;
/// <summary>
/// Generates a mould based on the inflated silhouette of the mesh and then extruded along the z axis to encase the entire supplied mesh.
/// </summary>
public sealed record SimpleMouldGenerator : MouldGenerator {
    public double MaxHeight { get; private set; } = 10.0;
    public double MinHeight { get; private set; } = 0.0;


    public static SimpleMouldGenerator New() => new();
    public SimpleMouldGenerator WithBottomOffset(double offset) => this with { OffsetBottom = offset };
    public SimpleMouldGenerator WithBolus(MeshModel bolus) => this with { BolusReference = bolus };
    public SimpleMouldGenerator WithOffsets(double offset) => this with { OffsetTop = offset, OffsetBottom = offset, OffsetXY = offset };
    public SimpleMouldGenerator WithCalculationResolution(int resolution) => this with { CalculationResolution = resolution };
    public SimpleMouldGenerator WithContourResolution(double resolution) => this with { ContourResolution = resolution };
    public SimpleMouldGenerator WithToolMeshes(MeshModel[] toolMeshes) => this with { ToolMeshes = toolMeshes.Select( tm => tm.Mesh).ToArray() };
    public SimpleMouldGenerator WithTopOffset(double offset) => this with { OffsetTop = offset };
    public SimpleMouldGenerator WithXYOffsets(double offset) => this with { OffsetXY = offset };

    public override Result<MeshModel> Build() {
        if (BolusReference is null) { throw new NullReferenceException("Build: Bolus mesh is null"); }

        MaxHeight = BolusReference.CachedBounds.Max.z + OffsetTop;
        MinHeight = BolusReference.CachedBounds.Min.z - OffsetBottom;

        //generate the inflated mesh
        var offsetMesh = MouldUtils.OffsetMeshD(BolusReference, OffsetXY);

        //create the mould
        var result = BooleanOperators.Subtraction(CalculateContour(offsetMesh), BolusReference);

        if (result.IsFailure) { return Result<MeshModel>.Fail(result.Errors); }
        if (ToolMeshes is null || ToolMeshes.Count() == 0) { return Result<MeshModel>.Pass(new MeshModel(result.Mesh)); }

        MeshEditor toolsEditor = new(new DMesh3());
        toolsEditor.Join(ToolMeshes);

        var reply = BooleanOperators.Subtraction(result.Mesh, toolsEditor.Mesh);
        return new Result<MeshModel> { Mesh = new MeshModel(reply.Mesh), IsSuccess = reply.IsSuccess, Errors = reply.Errors};
    }

    private List<Vector3d> GetContour(DMesh3 mesh, int padding = 3) {
        if (mesh is null) { return null; }

        var spatial = new DMeshAABBTree3(mesh, true);

        //try a hit test for each cube?
        var z = mesh.CachedBounds.Max.z + 2.0f; //where hit tests will start
        var min = new Vector2d(mesh.CachedBounds.Min.x, mesh.CachedBounds.Min.y);
        var max = new Vector2d(mesh.CachedBounds.Max.x, mesh.CachedBounds.Max.y);

        var dimensions = new Index2i(
            (int)((max.x - min.x) / ContourResolution) + padding * 2, //for the negative and plus side of x
            (int)((max.y - min.y) / ContourResolution) + padding * 2
        );

        var map = new bool[dimensions.a, dimensions.b]; //stores the results of hits

        //hit tests to create the boolean map
        var hitRay = new Ray3d(Vector3d.Zero, new Vector3d(0, 0, -1));
        var points = new List<System.Windows.Point>();
        int minX = dimensions.a, minY = dimensions.b;
        for (int x = 0; x < dimensions.a; x++) {
            var xSet = min.x + 0.5f + (x * ContourResolution);
            if (x < minX) { minX = x; }

            for (int y = 0; y < dimensions.b; y++) {
                var ySet = min.y + 0.5f + (y * ContourResolution);
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
                (currentPoint.x + 0.5f) * ContourResolution + min.x,
                (currentPoint.y + 0.5) * ContourResolution + min.y,
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

    private static int GridPosition(Vector2i position) => GridPosition(position.x, position.y);

    private static int GridPosition(int x, int y) {
        if (x == -1) {
            switch (y) {
                case -1: return 7;
                case 0: return 8;
                case 1: return 1;
            }
        }
        if (x == 0) {
            switch (y) {
                case -1: return 6;
                case 0: return 9; //sent the current point, which means we need some point to start. this is most likely to be empty
                case 1: return 2;
            }
        }
        if (x == 1) {
            switch (y) {
                case -1: return 5;
                case 0: return 4;
                case 1: return 3;
            }
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
        var contour = GetContour(mesh);

        //create polygon
        DMesh3 result = new();

        int n = contour.Count();
        Vector3d[] bottomLoop = contour.Select(v => new Vector3d(v.x, v.y, MinHeight)).ToArray();
        var bottomLoopIndices = new int[n];
        for (int i = 0; i < n; i++) {
            bottomLoopIndices[i] = result.AppendVertex(bottomLoop[i]);
        }

        //extend edge loop
        var z_offset = 20.0;
        Vector3d[] upperLoop = bottomLoop.Select(v => new Vector3d(v.x, v.y, MaxHeight)).ToArray();
        var upperLoopIndices = new int[n];
        for (int i = 0; i < n; i++) {
            upperLoopIndices[i] = result.AppendVertex(upperLoop[i]);
        }

        MeshEditor editor = new(result);
        editor.StitchLoop(bottomLoopIndices, upperLoopIndices);

        //cap the ends
        AutoHoleFill hole = new(editor.Mesh, EdgeLoop.FromVertices(editor.Mesh, upperLoopIndices));
        hole.Apply();
        Array.Reverse(bottomLoopIndices); //to reverse the normals of the triangles created
        hole.FillLoop = EdgeLoop.FromVertices(editor.Mesh, bottomLoopIndices);
        hole.Apply();

        return editor.Mesh;
    }

}
