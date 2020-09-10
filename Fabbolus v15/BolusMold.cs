using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Media3D;
using Microsoft.Win32;
using HelixToolkit.Wpf;

namespace Fabbolus_v15
{
    struct Slice
    {
        public double Floor;
        public double Ceiling;

        public Slice(double low, double high) {
            this.Floor = low;
            this.Ceiling = high;
        }
    }

    class BolusMold
    {
        public MeshGeometry3D Mesh;
        public List<MeshGeometry3D> Meshes;

        public BolusMold(MeshGeometry3D Mesh, double VoxelSize, bool AddMesh = false)
        {
            var points = GetContour(Mesh.Positions, VoxelSize);
            if (AddMesh)
            {
                Mesh = InvertNormals(Mesh);
                this.Mesh = GetMold(points, Mesh, true);
            }
            else
                this.Mesh = GetMold(points, Mesh);
        }

        public BolusMold(MeshGeometry3D Mesh, double VoxelSize, List<double> z_slices, bool alignmentLip = false)
        {
            if (Mesh != null)
            {
                Meshes = new List<MeshGeometry3D>();
                var points = GetContour(Mesh.Positions, VoxelSize); //get a list of points that surround the mesh
                points = SortPoints(points);
                var result = CuttingEarsTriangulator.Triangulate(points);  //a list of triangles, needs to be meshed
                points.Add(points[0]);// to allow loops to fully enclose the mesh


                //variables for the mesh heights
                double z_low = Mesh.Positions.Min(p => p.Z) - 2.4; //lowest vertex's z, minus an offset to make it lower
                double z_high = Mesh.Bounds.SizeZ / 2 + 3; //the wall height stops 3 mm below the slice's top

                //setup the lists for slicing
                List<Slice> slices = new List<Slice>();

                List<double> low_slice = new List<double>();
                List<double> high_slice = new List<double>();
                low_slice.Add(z_low);
                for(int i = 0; i < z_slices.Count; i++) {
                    low_slice.Add(z_slices[i]);
                    high_slice.Add(z_slices[i]);
                }
                high_slice.Add(z_high);

                for(int i = 0; i < low_slice.Count; i++) {
                    slices.Add(new Slice(low_slice[i], high_slice[i]));
                }

                //create the simple contoured meshes for each slice
                foreach(Slice slice in slices) {
                    //building the mesh
                    MeshBuilder mb = new MeshBuilder() {
                        CreateNormals = false,
                        CreateTextureCoordinates = false
                    };

                    //mesh the bottom using result, from the cutting ears triangulator
                    for (int t = 2; t < result.Count; t += 3) {
                        Point3D p0 = new Point3D(points[result[t]].X, points[result[t]].Y, slice.Floor);
                        Point3D p1 = new Point3D(points[result[t - 1]].X, points[result[t - 1]].Y, slice.Floor);
                        Point3D p2 = new Point3D(points[result[t - 2]].X, points[result[t - 2]].Y, slice.Floor);

                        mb.AddTriangle(p0, p1, p2);
                    }

                    //creating the triangles for the walls
                    for (int i = 1; i < points.Count; i++) {
                        Point3D p0 = new Point3D(points[i - 1].X, points[i - 1].Y, slice.Floor);
                        Point3D p1 = new Point3D(points[i].X, points[i].Y, slice.Floor);
                        Point3D p2 = new Point3D(points[i - 1].X, points[i - 1].Y, slice.Ceiling);

                        mb.AddTriangle(p0, p1, p2);

                        p0 = new Point3D(points[i].X, points[i].Y, slice.Floor);
                        p1 = new Point3D(points[i].X, points[i].Y, slice.Ceiling);
                        p2 = new Point3D(points[i - 1].X, points[i - 1].Y, slice.Ceiling);

                        mb.AddTriangle(p0, p1, p2);
                    }

                    //create higher surface
                    //point order is reversed to reverse the normals created
                    for (int t = 2; t < result.Count; t += 3) {
                        Point3D p0 = new Point3D(points[result[t - 2]].X, points[result[t - 2]].Y, slice.Ceiling);
                        Point3D p1 = new Point3D(points[result[t - 1]].X, points[result[t - 1]].Y, slice.Ceiling);
                        Point3D p2 = new Point3D(points[result[t]].X, points[result[t]].Y, slice.Ceiling);

                        mb.AddTriangle(p0, p1, p2);
                    }


                    this.Meshes.Add(mb.ToMesh());
                }

            }
            
        }
        
        public BolusMold(MeshGeometry3D mesh, double voxelSize, Vector3D axis, double angle) {
            //rotate the incoming mesh with the axis and angle provided
            Matrix3D m = Matrix3D.Identity;
            Quaternion q = new Quaternion(axis, angle);
            m.Rotate(q);

            Point3DCollection positions = new Point3DCollection();
            foreach (Point3D p in mesh.Positions)
                positions.Add(m.Transform(p));

            //used for boundries for the mesh after transformation
            double zMax = 0;
            double zMin = 0;
            foreach (Point3D p in positions) {
                if (p.Z > zMax)
                    zMax = p.Z;
                if (p.Z < zMin)
                    zMin = p.Z;
            }

            //make the points 2d
            var points = GetContour(positions, voxelSize);

            //create mesh
            this.Mesh = GetMold(points, zMax, zMin);

        }
        /*
        //creates the mold as well as cutting it into sections
        public BolusMold(MeshGeometry3D mesh, double voxelSize, int slices)
        {
            //create a 2D array of the mesh using the voxelSize for the size of each square
            //check each point to calculate which grid cell it would belong to

            int xLength = (int) (mesh.Bounds.SizeX / voxelSize); //number of squares in the x axis
            int yLength = (int)(mesh.Bounds.SizeY / voxelSize); //number of squares in the y axis
            bool[,] grid = new bool[xLength + 2, yLength + 2]; //extras added for a wider contour

            //bottom left most point in the grid as a reference for all future calculations
            double starting_x = mesh.Bounds.Size.X - voxelSize;
            double starting_y = mesh.Bounds.Size.Y - voxelSize;

            //inspect each point's x and y to determine which cell it belongs to
            foreach(Point3D point in mesh.Positions)
            {
                int cell_x = (int)((point.X - starting_x) / voxelSize); 
                int cell_y = (int)((point.Y - starting_y) / voxelSize);
                grid[cell_x, cell_y] = true;
            }

            bool filled = false; //used as part of a method for recursion

            //check for false squares within a contour and fill them
            for (int y = 0; y < grid.GetLength(1); y++)
                for (int x = 0; x < grid.GetLength(0); x++)
                    if (!grid[x,y]) 
                    {
                        int adjacent_true_cells = 0;

                        //left
                        if (x != 0 && grid[x - 1, y])
                            adjacent_true_cells++;

                        //right
                        if (x < grid.GetLength(0) - 1 && grid[x + 1, y])
                            adjacent_true_cells++;

                        //bottom
                        if (y != 0 && grid[x, y - 1])
                            adjacent_true_cells++;

                        //top
                        if (y < grid.GetLength(1) - 1 && grid[x, y + 1])
                            adjacent_true_cells++;

                        if (adjacent_true_cells > 2)
                        {
                            filled = true;
                            grid[x, y] = true;
                        }

                    }

            //mesh the bottom layer

            //turn the bool grid array into an int array. the value notes what meshing method needs to be used on that square
            
            // 0 - do not mesh
            // * 1 - full mesh 
            // * 2 - triangle top left
            // * 3 - triangle top right
            // * 4 - triangle bottom right
            // * 5 - triangle bottom left
             
            var moldMesh = new MeshGeometry3D();

            int[,] contourGrid = new int[grid.GetLength(0), grid.GetLength(1)];
            for (int y = 0; y < grid.GetLength(1); y++)
                for (int x = 0; x < grid.GetLength(0); x++)
                {
                    int flag = 0;

                    bool left = (x != 0 && grid[x - 1, y]);
                    bool right = (x < grid.GetLength(0) - 1 && grid[x + 1, y]);
                    bool down = (y != 0 && grid[x, y - 1]);
                    bool up = (y < grid.GetLength(1) - 1 && grid[x, y + 1]);

                    //flag to fill the square
                    if (left && right && down && up)
                        flag = 1;
                    else if (left && up)
                        flag = 2;
                    else if (up && right)
                        flag = 3;
                    else if (down && right)
                        flag = 4;
                    else if (down && left)
                        flag = 5;

                    //create the mesh
                }

            //add the verticies
            //connect triangles from those verticies

            //mesh the top layer

                    //subtract the original mesh from the mold using Boolean subtraction

                    //set Mesh to the resulting mesh
        }
        */

        /// <summary>
        /// Inverts the mesh's normals.  
        /// </summary>
        /// <param name="mesh">The mesh to be dupkicated and itsnormals inverted</param>
        /// <returns>MeshGeometry3D with the normals flipped</returns>
        private MeshGeometry3D InvertNormals(MeshGeometry3D mesh)
        {
            MeshBuilder inverted_mesh = new MeshBuilder(true, false);

            for (int t = 2; t < mesh.TriangleIndices.Count; t += 3)
            {
                Point3D p0 = new Point3D(mesh.Positions[mesh.TriangleIndices[t]].X, mesh.Positions[mesh.TriangleIndices[t]].Y, mesh.Positions[mesh.TriangleIndices[t]].Z);
                Point3D p1 = new Point3D(mesh.Positions[mesh.TriangleIndices[t - 1]].X, mesh.Positions[mesh.TriangleIndices[t - 1]].Y, mesh.Positions[mesh.TriangleIndices[t - 1]].Z);
                Point3D p2 = new Point3D(mesh.Positions[mesh.TriangleIndices[t - 2]].X, mesh.Positions[mesh.TriangleIndices[t - 2]].Y, mesh.Positions[mesh.TriangleIndices[t - 2]].Z);

                inverted_mesh.AddTriangle(p0, p1, p2);
            }

            return inverted_mesh.ToMesh();
        }

        /// <summary>
        /// Gets 2D shape of the mesh. Checks all verticies regardless of z axis. Uses a grid-based approach: 
        /// breaks an area up into x-y grid and if it detects one or more points in that square, it is considered filled
        /// The countour is around these filled squares
        /// </summary>
        /// <param name="points">The points from a mesh</param>
        /// <param name="size">The resolution size</param>
        /// <returns>List of 2D points for the contour</returns>
        private List<Point> GetContour(Point3DCollection points, double size)
        {
            if (points.Count > 0)
            {
                double offset = 3; //used to give some space at the start of the contour boxes
                double bottom_x, top_x, bottom_y, top_y;

                bottom_x = points.Min(p => p.X);
                top_x = points.Max(p => p.X) - bottom_x;
                bottom_y = points.Min(p => p.Y);
                top_y = points.Max(p => p.Y) - bottom_y;

                double gridsize = size;
                double start_x = bottom_x - gridsize - offset;
                double start_y = bottom_y - gridsize - offset;

                int x_grid = Convert.ToInt16((top_x - start_x) / gridsize) + 1;
                int y_grid = Convert.ToInt16((top_y - start_y) / gridsize) + 1;
                bool[,] contourgrid = new bool[x_grid, y_grid];

                //finds which box areas contain the model's positions
                for (int y = 0; y < y_grid; y++)
                {
                    double lowY = start_y + y * gridsize;
                    double highY = lowY + gridsize;
                    Point3DCollection points_y = new Point3DCollection();
                    foreach (Point3D p in points)
                        if (p.Y > lowY && p.Y < highY)
                            points_y.Add(p);

                    for (int x = 0; x < x_grid; x++)
                    {
                        double lowX = start_x + x * gridsize;
                        double highX = lowX + gridsize;
                        foreach (Point3D p in points_y)
                            if (p.X > lowX && p.X < highX)
                            {
                                contourgrid[x, y] = true;
                                break;
                            }
                    }

                }

                FillHoles(contourgrid);

                //create contour points by finding boxes that are adjacent to the edge of the good boxes
                List<Point> contourPointsList = new List<Point>();
                for (int y = 0; y < y_grid; y++)
                    for (int x = 0; x < x_grid; x++)
                    {
                        int sides = AdjacentGoodBoxes(x, y, contourgrid);
                        if (sides > 0 && sides < 4 && !contourgrid[x, y])//beside a good box and not a good box itself
                        {
                            Point point = new Point(start_x + x * gridsize + gridsize / 2, start_y + y * gridsize + gridsize / 2);
                            contourPointsList.Add(point);
                        }
                    }

                return contourPointsList;

            }

            return null;
        }

        //determines how many filled boxes are surrounding the box
        private int AdjacentGoodBoxes(int x, int y, bool[,] contourgrid)
        {
            int sides = 0;

            //left 
            if (x != 0)
                if (contourgrid[x - 1, y])
                    sides++;

            //top
            if (y < contourgrid.GetLength(1) - 1)
                if (contourgrid[x, y + 1])
                    sides++;

            //right
            if (x < contourgrid.GetLength(0) - 1)
                if (contourgrid[x + 1, y])
                    sides++;

            //bottom
            if (y != 0)
                if (contourgrid[x, y - 1])
                    sides++;

            return sides;
        }

        /// <summary>
        /// Finds any empty squares within the contour's grid map and labels them as filled
        /// </summary>
        /// <param name="contourgrid"></param>
        private void FillHoles(bool [,] contourgrid)
        {
            bool filled = false;
            int xMax = contourgrid.GetLength(0);
            int yMax = contourgrid.GetLength(1);

            //fill any empty spaces with 3 or more good adjacent boxes
            for (int y = 0; y < yMax; y++)
                for (int x = 0; x < xMax; x++)
                    if (!contourgrid[x, y])
                    {
                        int sides = AdjacentGoodBoxes(x, y, contourgrid);
                        if (sides > 2)
                        {
                            filled = true;
                            contourgrid[x, y] = true;
                        }
                    }

            //if one or more empty spaces were filled, check again
            if (filled)
                FillHoles(contourgrid);
        }

        //calculates a mold box to contour around the input mesh
        private MeshGeometry3D GetMold(List<Point> points, MeshGeometry3D mesh, bool AddMesh = false)
        {
            if (mesh != null && points != null && points.Count > 0 && mesh.Positions.Count > 0) {
                double z_height = mesh.Positions.Max(p => p.Z) + 4; //highest point for the mold
                double offsetZ = mesh.Positions.Min(p => p.Z) - 3;

                //the final contour
                MeshBuilder mb = new MeshBuilder() {
                    CreateNormals = false,
                    CreateTextureCoordinates = false};

                points = SortPoints(points);

                //create contour face
                var result = CuttingEarsTriangulator.Triangulate(points);

                //create lower surface
                for (int t = 2; t < result.Count; t += 3)
                {
                    Point3D p0 = new Point3D(points[result[t]].X, points[result[t]].Y, offsetZ);
                    Point3D p1 = new Point3D(points[result[t - 1]].X, points[result[t - 1]].Y, offsetZ);
                    Point3D p2 = new Point3D(points[result[t - 2]].X, points[result[t - 2]].Y, offsetZ);

                    mb.AddTriangle(p0, p1, p2);
                }

                //create higher surface
                //point order is reversed to reverse the normals created
                double upper_offsetZ = mesh.Positions.Max(p => p.Z) + 4;
                for (int t = 2; t < result.Count; t += 3)
                {
                    Point3D p0 = new Point3D(points[result[t - 2]].X, points[result[t - 2]].Y, upper_offsetZ);
                    Point3D p1 = new Point3D(points[result[t - 1]].X, points[result[t - 1]].Y, upper_offsetZ);
                    Point3D p2 = new Point3D(points[result[t]].X, points[result[t]].Y, upper_offsetZ);

                    mb.AddTriangle(p0, p1, p2);
                }

                points.Add(points[0]);

                //create walls
                for (int t = 1; t < points.Count; t++)
                {
                    Point3D p0 = new Point3D(points[t - 1].X, points[t - 1].Y, offsetZ);
                    Point3D p1 = new Point3D(points[t].X, points[t].Y, offsetZ);
                    Point3D p2 = new Point3D(points[t - 1].X, points[t - 1].Y, upper_offsetZ);

                    mb.AddTriangle(p0, p1, p2);

                    p0 = new Point3D(points[t].X, points[t].Y, upper_offsetZ);
                    p1 = new Point3D(points[t - 1].X, points[t - 1].Y, upper_offsetZ);
                    p2 = new Point3D(points[t].X, points[t].Y, offsetZ); ;

                    mb.AddTriangle(p0, p1, p2);
                }

                if(AddMesh)
                    mb.Append(mesh);

                return mb.ToMesh();
            }

            return null;
        }

        //calculates a mold box to contour around the input mesh
        private MeshGeometry3D GetMold(List<Point> points, double zMax, double zMin)
        {
            if (points != null && points.Count > 0) {
                double z_height = zMax + 4; //highest point for the mold
                double offsetZ = zMin - 3;

                //the final contour
                MeshBuilder mb = new MeshBuilder() {
                    CreateNormals = false,
                    CreateTextureCoordinates = false};

                points = SortPoints(points);

                //create contour face
                var result = CuttingEarsTriangulator.Triangulate(points);

                //create lower surface
                for (int t = 2; t < result.Count; t += 3)
                {
                    Point3D p0 = new Point3D(points[result[t]].X, points[result[t]].Y, offsetZ);
                    Point3D p1 = new Point3D(points[result[t - 1]].X, points[result[t - 1]].Y, offsetZ);
                    Point3D p2 = new Point3D(points[result[t - 2]].X, points[result[t - 2]].Y, offsetZ);

                    mb.AddTriangle(p0, p1, p2);
                }

                //create higher surface
                //point order is reversed to reverse the normals created
                double upper_offsetZ = z_height;
                for (int t = 2; t < result.Count; t += 3)
                {
                    Point3D p0 = new Point3D(points[result[t - 2]].X, points[result[t - 2]].Y, upper_offsetZ);
                    Point3D p1 = new Point3D(points[result[t - 1]].X, points[result[t - 1]].Y, upper_offsetZ);
                    Point3D p2 = new Point3D(points[result[t]].X, points[result[t]].Y, upper_offsetZ);

                    mb.AddTriangle(p0, p1, p2);
                }

                points.Add(points[0]);

                //create walls
                for (int t = 1; t < points.Count; t++)
                {
                    Point3D p0 = new Point3D(points[t - 1].X, points[t - 1].Y, offsetZ);
                    Point3D p1 = new Point3D(points[t].X, points[t].Y, offsetZ);
                    Point3D p2 = new Point3D(points[t - 1].X, points[t - 1].Y, upper_offsetZ);

                    mb.AddTriangle(p0, p1, p2);

                    p0 = new Point3D(points[t].X, points[t].Y, upper_offsetZ);
                    p1 = new Point3D(points[t - 1].X, points[t - 1].Y, upper_offsetZ);
                    p2 = new Point3D(points[t].X, points[t].Y, offsetZ); ;

                    mb.AddTriangle(p0, p1, p2);
                }

                return mb.ToMesh();
            }

            return null;
        }
        //finds the closest point around a contour
        private List<Point> SortPoints(List<Point> contourpoints)
        {
            //creating a list that will eventually have entries removed to speed things up
            List<Point> points = new List<Point>();
            foreach (Point p in contourpoints)
                points.Add(new Point(p.X, p.Y));

            List<Point> result = new List<Point>();

            Point start = new Point(points[0].X, points[0].Y);
            result.Add(start);
            points.RemoveAt(0);

            //cycle through and reorganize points into result
            List<double> distances = new List<double>();
            List<int> indexes = new List<int>();

            while (points.Count > 0)
            {
                distances.Clear();
                indexes.Clear();

                foreach (Point p in points)
                {
                    double distance = GetDistance(result[result.Count - 1], p);
                    distances.Add(distance);
                    indexes.Add(points.FindIndex(x => x == p));
                }

                int index = indexes[distances.FindIndex(d => d == distances.Min())];
                result.Add(points[index]);
                points.RemoveAt(index);
            }

            return result;
        }

        //distance from one 2D point to another
        private double GetDistance(Point p1, Point p2)
        {
            return (p1.X - p2.X) * (p1.X - p2.X) + (p1.Y - p2.Y) * (p1.Y - p2.Y);
        }
    }
}

