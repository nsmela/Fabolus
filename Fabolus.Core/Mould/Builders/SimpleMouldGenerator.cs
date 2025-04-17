using g3;

namespace Fabolus.Core.Mould.Builders;
public sealed record SimpleMouldGenerator {
    private DMesh3? BolusReference { get; set; } //mesh to invert while entirely within
    private double MaxHeight { get; set; } = 100.0;
    private double MinHeight { get; set; } = -100.0;
    private double OffsetXY { get; set; } = 3.0;
    private double OffsetTop { get; set; } = 3.0;
    private double OffsetBottom { get; set; } = 3.0;
    private double Resolution { get; set; } = 3.0;
    private DMesh3[] ToolMeshes { get; set; } = []; // mesh to boolean subtract from the mold

    public static SimpleMouldGenerator New() => new();
    public SimpleMouldGenerator WithBottomOffset(double offset) => this with { OffsetBottom = offset };
    public SimpleMouldGenerator WithBolus(DMesh3 bolusMesh) => this with { BolusReference = bolusMesh };
    public SimpleMouldGenerator WithHeights(double maxHeight, double minHeight) => this with { MaxHeight = maxHeight, MinHeight = minHeight };
    public SimpleMouldGenerator WithOffsets(double offset) => this with { OffsetTop = offset, OffsetBottom = offset, OffsetXY = offset };
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
        var contour = GetContour(BolusReference);
        var mesh = ContourToMesh(contour);

        return MoldUtils.OffsetMeshD(BolusReference, OffsetXY);
    }

    private List<Vector3d> GetContour(DMesh3 mesh) {
        if (mesh is null) { return []; }

        var spatial = new DMeshAABBTree3(mesh);
        spatial.Build();

        //try a hit test for each cube?
        var z = mesh.CachedBounds.Max.z + 2.0; //where hit tests will start
        var min = new Vector2d(mesh.CachedBounds.Min.x, mesh.CachedBounds.Min.y);
        var max = new Vector2d(mesh.CachedBounds.Max.x, mesh.CachedBounds.Max.y);

        var padding = 3;
        var dimensions = new Index2i(
            (int)((max.x - min.x) / Resolution) + padding * 2, //for the negative and plus side of x
            (int)((max.y - min.y) / Resolution) + padding * 2
        );

        var map = new bool[dimensions.a, dimensions.b]; //stores the results of hits

        //hit tests to create the boolean map
        var hitRay = new Ray3d(Vector3d.Zero, new Vector3d(0, 0, -1));
        var points = new List<System.Windows.Point>();
        int minX = dimensions.a, minY = dimensions.b;
        for (int x = 0; x < dimensions.a; x++) {
            var xSet = min.x + 0.5f + (x * Resolution);
            if (x < minX) { minX = x; }

            for (int y = 0; y < dimensions.b; y++) {
                var ySet = min.y + 0.5f + (y * Resolution);
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
                (currentPoint.x + 0.5f) * Resolution + min.x,
                (currentPoint.y + 0.5f) * Resolution + min.y,
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
        var verts = new List<Vector2d>();
        contour.ForEach(v => verts.Add(new Vector2d(v.x, v.y)));

        var polygon = new Polygon2d(verts);
        var generator = new TriangulatedPolygonGenerator {
            Polygon = new(polygon)
        }.Generate();
        
        return generator.MakeDMesh();
        
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

        //cycle clockwise until a new spot is hit
        for (int i = 0; i < 9; i++) {
            position++;
            if (position > 8) position = 1;

            pVector = GridPosition(position);
            pX = x + pVector.x;
            pY = y + pVector.y;

            //check if map is true or false at this position
            if (pX >= 0 && pY >= 0 && map[pX, pY]) return position;

        }

        return -1;
    }

}
