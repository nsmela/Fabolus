using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using g3;

namespace Fabbolus_v15
{
    class Bolus
    {
        //global variables
        private DMesh3 _mesh;
        private DMesh3 _smoothMesh;
        private List<DMesh3> _moldMesh;
        private MeshGeometry3D _displayMesh;

        //initializing methods

        /// <summary>
        /// Initializes the Bolus object
        /// </summary>
        /// <param name="filename"></param>
        public Bolus(string filename)
        {
            //check if filepath is valid
            if (!File.Exists(filename))
                return;

            _mesh = new DMesh3();

            //import the mesh from the filepath
            _mesh = ImportMeshFromFile(filename);

            //store the mesh
            _displayMesh = DMeshToMeshGeometry(_mesh);

            _moldMesh = new List<DMesh3>();
        }

        /// <summary>
        /// Reads a STL file and creates a DMesh object
        /// </summary>
        /// <param name="filename"></param>
        DMesh3 ImportMeshFromFile(string filename)
        {
            //import the mesh from file
            var mesh = StandardMeshReader.ReadMesh(filename);
            _smoothMesh = null;
            _moldMesh = null;
            _displayMesh = DMeshToMeshGeometry(_mesh);

            //check is mesh is good, attempt to fix if not
            OrientationCentre(mesh);

            //load mesh and reset settings
            return mesh;
        }

        void OrientationCentre(DMesh3 mesh)
        {
            double x = mesh.CachedBounds.Center.x * -1;
            double y = mesh.CachedBounds.Center.y * -1;
            double z = mesh.CachedBounds.Center.z * -1;
            MeshTransforms.Translate(mesh, x, y, z);
        }

        //public methods

        /// <summary>
        /// Returns the mesh of the Bolus
        /// </summary>
        /// <returns></returns>
        public MeshGeometry3D Mesh()
        {
            //apply temp transform if exists

            //the Bolus mesh is stored as a DMesh object, needs to be converted for display
            return _displayMesh;
        }

        public MeshGeometry3D Mesh(bool raw)
        {
            if (raw)
                return DMeshToMeshGeometry(_mesh);
            else
                return _displayMesh;
        }

        public MeshGeometry3D PreviewMold(double resolution)
        {
            var mold = new BolusMold(_displayMesh, resolution);
            return mold.Mesh;
        }

        public MeshGeometry3D PreviewMoldWhileRotating(double resolution, Vector3D axis, double angle)
        {
            var mold = new BolusMold(_displayMesh, resolution, axis, angle);
            return mold.Mesh;
        }

        public MeshGeometry3D GenerateMold(double resolution, List<MeshGeometry3D> airholes)
        {
            //generates the outer mold and interior cavity for the bolus
            var mold = new BolusMold(_displayMesh, resolution, true); 
            DMesh3 mesh = MeshGeometryToDMesh(mold.Mesh);

            //generate the airholes for boolean subtraction

            if (airholes.Count > 0)
            {
                DMesh3 booleanMesh = new DMesh3();
                var holes = new DMesh3();
                MeshEditor editor = new MeshEditor(holes);
                foreach (MeshGeometry3D m in airholes)
                    editor.AppendMesh(MeshGeometryToDMesh(m));

                booleanMesh = holes;

                //boolean subtract the airholes from the mold
                _moldMesh = new List<DMesh3>(){
                    BooleanSubtraction(mesh, holes)};
            }

            return DMeshToMeshGeometry(_moldMesh[0]);
        }

        public List<MeshGeometry3D> GenerateMold(double resolution, List<MeshGeometry3D> airholes, List<double> z_slices)
        {
            //merges airholes and bolus mesh for subtraction later
            var bolus = new DMesh3();
            if (airholes.Count > 0)
            {
                //airholes
                var holes = new DMesh3();
                MeshEditor editor = new MeshEditor(holes);
                foreach (MeshGeometry3D m in airholes)
                    editor.AppendMesh(MeshGeometryToDMesh(m));


                //boolean union the airholes and the bolus
                if (_smoothMesh != null)
                    bolus = BooleanUnion(_smoothMesh, holes);
                else
                    bolus = BooleanUnion(_mesh, holes);
            }
            else
                if (_smoothMesh != null)
                bolus = _smoothMesh;
            else
                bolus = _mesh;

            //generates the outer mold and interior cavity for the bolus
            var _molds = new List<MeshGeometry3D>();
            _moldMesh = new List<DMesh3>();
            var molds = new BolusMold(_displayMesh, resolution, z_slices).Meshes;

            //subtract the bolus from the mold
            for(int i = 0; i <= z_slices.Count; i++)
            {
                _moldMesh.Add(BooleanSubtraction(MeshGeometryToDMesh(molds[i]), bolus));
                _molds.Add(DMeshToMeshGeometry(_moldMesh[i]));
            }

            return _molds;
        }

        public void Transform(Vector3D axis, double angle)
        {
            Vector3d _axis = new Vector3d(axis.X, axis.Y, axis.Z);
            Quaterniond rotation = new Quaterniond(_axis, angle);

            MeshTransforms.Rotate(_mesh, new Vector3d(0, 0, 0), rotation);
            if (_smoothMesh != null)
            {
                MeshTransforms.Rotate(_smoothMesh, new Vector3d(0, 0, 0), rotation);
                _displayMesh = DMeshToMeshGeometry(_smoothMesh);
            }
            else
                _displayMesh = DMeshToMeshGeometry(_mesh);
        }

        public void ResetRotations()
        {
            Vector3d origin = new Vector3d(0, 0, 0);

            //x axis reset
            Quaterniond rotation = new Quaterniond(new Vector3d(1, 0, 0), 0);
            MeshTransforms.Rotate(_mesh, origin, rotation);

            if (_smoothMesh != null)
                MeshTransforms.Rotate(_smoothMesh, new Vector3d(0, 0, 0), rotation);

            //y axis reset
            rotation = new Quaterniond(new Vector3d(0, 1, 0), 0);
            MeshTransforms.Rotate(_mesh, origin, rotation);

            if (_smoothMesh != null)
                MeshTransforms.Rotate(_smoothMesh, new Vector3d(0, 0, 0), rotation);

            //z axis reset
            rotation = new Quaterniond(new Vector3d(0, 0, 1), 0);
            MeshTransforms.Rotate(_mesh, origin, rotation);

            if (_smoothMesh != null)
                MeshTransforms.Rotate(_smoothMesh, new Vector3d(0, 0, 0), rotation);

            if (_smoothMesh != null)
                _displayMesh = DMeshToMeshGeometry(_smoothMesh);
            else
                _displayMesh = DMeshToMeshGeometry(_mesh);
        }

        public String DisplayVolumes()
        {
            string text = "";
            text += "   Imported model: " + (CalculateVolume(_mesh)).ToString("0.00") + " mL\r\n";

            if (_smoothMesh != null)
                text += "   Smoothed Model: " + (CalculateVolume(_smoothMesh)).ToString("0.00") + " mL\r\n";

            if (_moldMesh != null && _moldMesh.Count > 0)
                text += "   Mold Model: " + (CalculateVolume(_moldMesh[0])).ToString("0.00") + " mL\r\n";
            return text;
        }

        /// <summary>
        /// uses marching cubes to help smooth the mesh after using the remesher
        /// experimental
        /// </summary>
        /// <param name="edgeLength"></param>
        /// <param name="smoothSpeed"></param>
        /// <param name="iterations"></param>
        /// <param name="cells"></param>
        public void Smooth(double edgeLength, double smoothSpeed, double iterations, double cells)
        {
            //Use the Remesher class to do a basic remeshing
            DMesh3 mesh = new DMesh3(_mesh);
            Remesher r = new Remesher(mesh);
            r.PreventNormalFlips = true;
            r.SetTargetEdgeLength(edgeLength);
            r.SmoothSpeedT = smoothSpeed;
            r.SetProjectionTarget(MeshProjectionTarget.Auto(mesh));
            for (int k = 0; k < iterations; k++)
                r.BasicRemeshPass();

            //marching cubes
            int num_cells = (int)cells;
            if (cells > 0)
            {
                double cell_size = mesh.CachedBounds.MaxDim / num_cells;

                MeshSignedDistanceGrid sdf = new MeshSignedDistanceGrid(mesh, cell_size);
                sdf.Compute();

                var iso = new DenseGridTrilinearImplicit(sdf.Grid, sdf.GridOrigin, sdf.CellSize);

                MarchingCubes c = new MarchingCubes();
                c.Implicit = iso;
                c.Bounds = mesh.CachedBounds;
                c.CubeSize = c.Bounds.MaxDim / cells;
                c.Bounds.Expand(3 * c.CubeSize);

                c.Generate();

                _smoothMesh = c.Mesh;
            }
            else
                _smoothMesh = mesh;

            _displayMesh = DMeshToMeshGeometry(_smoothMesh);
            _moldMesh = null;

        }

        //returns the size of the mesh
        public double MeshSize()
        {
            return _mesh.CachedBounds.MaxDim;
        }

        /// <summary>
        /// Returns the highest z point
        /// </summary>
        /// <returns></returns>
        public double Ceiling()
        {
            return _displayMesh.Bounds.SizeZ / 2;
        }

        public double Floor() {
            return _displayMesh.Bounds.Z;
        }

        public System.Windows.Point MinXY() {
            return new System.Windows.Point(_displayMesh.Bounds.X, _displayMesh.Bounds.Y);
        }

        public System.Windows.Point MaxXY() {
            return new System.Windows.Point(_displayMesh.Bounds.SizeX / 2, _displayMesh.Bounds.SizeY / 2);
        }

        public void ExportMesh(string filename)
        {
            //if there is a mold generated, save that one
            if (_moldMesh != null && _moldMesh.Count > 0) {
                if (_moldMesh.Count == 1) { //if the mold wasn't sliced
                    StandardMeshWriter.WriteMesh(filename, _moldMesh[0], WriteOptions.Defaults);
                    return;
                }
                else {
                    for(int i = 0; i < _moldMesh.Count; i++) {
                        string file = filename.Substring(0, filename.Length - 4);
                        file += "_" + (i+1) + ".stl";
                        StandardMeshWriter.WriteMesh(file, _moldMesh[i], WriteOptions.Defaults);
                    }
                    return;
                }
            }
            else {
                DMesh3 mesh = new DMesh3();

                if (_moldMesh != null)
                    mesh = _moldMesh[0];
                else if (_smoothMesh != null)
                    mesh = _smoothMesh;
                else
                    mesh = _mesh;

                StandardMeshWriter.WriteMesh(filename, mesh, WriteOptions.Defaults);
            }
        }

        //experimental
        public List<DMesh3> SliceMold(DMesh3 mesh_to_slice, int number_of_slices)
        {
            var meshes = new List<MeshGeometry3D>();

            //convert mesh to DMesh
            DMesh3 mesh = mesh_to_slice;

            //create cube for slicing
            double z_height = mesh.GetBounds().Max.z - mesh.GetBounds().Min.z;
            double slice_interval = (double)(z_height / number_of_slices);
            double x_size = mesh.GetBounds().Depth;
            double y_size = mesh.GetBounds().Width;
            double low_z = mesh.GetBounds().Min.z;
            

            //boolean intersection each mesh
            var sliced_meshes = new List<DMesh3>();
            for (int i = 0; i < number_of_slices; i++)
            {
                //create box
                Vector3d centre = new Vector3d(0, 0, low_z + slice_interval/2 + i*(slice_interval));
                Vector3d extend = new Vector3d(
                    x_size,
                    y_size,
                    slice_interval/2);

                ImplicitBox3d box = new ImplicitBox3d() { Box = new Box3d(centre, extend) };

                //boolean overlap 
                BoundedImplicitFunction3d meshA = meshToImplicitF(mesh, 64, 0.2f);

                //take the difference of the bolus mesh minus the tools
                ImplicitIntersection3d mesh_result = new ImplicitIntersection3d { A = meshA, B = box };

                //calculate the boolean mesh
                MarchingCubes c = new MarchingCubes();
                c.Implicit = mesh_result;
                c.RootMode = MarchingCubes.RootfindingModes.LerpSteps;
                c.RootModeSteps = 5;
                c.Bounds = mesh_result.Bounds();
                c.CubeSize = c.Bounds.MaxDim / 256;
                c.Bounds.Expand(3 * c.CubeSize);
                c.Generate();
                MeshNormals.QuickCompute(c.Mesh);

                int triangleCount = c.Mesh.TriangleCount / 3;
                Reducer r = new Reducer(c.Mesh);
                r.ReduceToTriangleCount(triangleCount);

                sliced_meshes.Add(c.Mesh);
            }

            return sliced_meshes;
        }

        //private methods
        /// <summary>
        /// Converts a DMesh3 object to a MeshGeometry3D object
        /// DMesh3 is used within the Bolus class to perform calculations
        /// MeshGeometry3D is used by Helix-Toolkit to display
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private MeshGeometry3D DMeshToMeshGeometry(DMesh3 value)
        {
            if (value != null)
            {
                //compacting the DMesh to the indices are true
                var mesh_copy = new DMesh3(value, true);
                MeshGeometry3D mesh = new MeshGeometry3D();

                //calculate positions
                var vertices = value.Vertices();
                foreach (var vert in vertices)
                    mesh.Positions.Add(new Point3D(vert.x, vert.y, vert.z));

                //calculate faces
                var vID = value.VertexIndices().ToArray();
                var faces = value.Triangles();
                foreach (Index3i f in faces)
                {
                    mesh.TriangleIndices.Add(Array.IndexOf(vID, f.a));
                    mesh.TriangleIndices.Add(Array.IndexOf(vID, f.b));
                    mesh.TriangleIndices.Add(Array.IndexOf(vID, f.c));
                }

                return mesh;
            }
            else
                return null;
        }

        private DMesh3 MeshGeometryToDMesh(MeshGeometry3D mesh)
        {
            List<Vector3d> vertices = new List<Vector3d>();
            foreach (Point3D point in mesh.Positions)
                vertices.Add(new Vector3d(point.X, point.Y, point.Z));

            List<Vector3f> normals = new List<Vector3f>();
            foreach (Point3D normal in mesh.Normals)
                normals.Add(new Vector3f(normal.X, normal.Y, normal.Z));

            if (normals.Count() == 0)
                normals = null;

            List<Index3i> triangles = new List<Index3i>();
            for (int i = 0; i < mesh.TriangleIndices.Count; i += 3)
                triangles.Add(new Index3i(mesh.TriangleIndices[i], mesh.TriangleIndices[i + 1], mesh.TriangleIndices[i + 2]));

            //converting the meshes to use Implicit Surface Modeling
            return DMesh3Builder.Build(vertices, triangles, normals);
        }

        public DMesh3 BooleanSubtraction(DMesh3 mesh1, DMesh3 mesh2)
        {
            BoundedImplicitFunction3d meshA = meshToImplicitF(mesh1, 128, 0.2f);
            BoundedImplicitFunction3d meshB = meshToImplicitF(mesh2, 128, 0.2f);

            //take the difference of the bolus mesh minus the tools
            ImplicitDifference3d mesh = new ImplicitDifference3d() { A = meshA, B = meshB };

            //calculate the boolean mesh
            MarchingCubes c = new MarchingCubes();
            c.Implicit = mesh;
            c.RootMode = MarchingCubes.RootfindingModes.LerpSteps;
            c.RootModeSteps = 5;
            c.Bounds = mesh.Bounds();
            c.CubeSize = c.Bounds.MaxDim / 128;
            c.Bounds.Expand(3 * c.CubeSize);
            c.Generate();
            MeshNormals.QuickCompute(c.Mesh);

            //int triangleCount = c.Mesh.TriangleCount / 2;
            //Reducer r = new Reducer(c.Mesh);
            //r.ReduceToTriangleCount(triangleCount);
            return c.Mesh;
        }

        public DMesh3 BooleanUnion(DMesh3 mesh1, DMesh3 mesh2)
        {
            BoundedImplicitFunction3d meshA = meshToImplicitF(mesh1, 128, 0.2f);
            BoundedImplicitFunction3d meshB = meshToImplicitF(mesh2, 128, 0.2f);

            //take the difference of the bolus mesh minus the tools
            var mesh = new ImplicitUnion3d() { A = meshA, B = meshB };

            //calculate the boolean mesh
            MarchingCubes c = new MarchingCubes();
            c.Implicit = mesh;
            c.RootMode = MarchingCubes.RootfindingModes.LerpSteps;
            c.RootModeSteps = 5;
            c.Bounds = mesh.Bounds();
            c.CubeSize = c.Bounds.MaxDim / 96;
            c.Bounds.Expand(3 * c.CubeSize);
            c.Generate();
            MeshNormals.QuickCompute(c.Mesh);

            //int triangleCount = c.Mesh.TriangleCount / 2;
            //Reducer r = new Reducer(c.Mesh);
            //r.ReduceToTriangleCount(triangleCount);
            return c.Mesh;
        }

        internal void ClearMold()
        {
            _moldMesh = null;
        }

        /// <summary>
        /// calculates volume of a triangle. signed so that negative volumes exist, easing the calculation
        /// </summary>
        private double SignedVolumeOfTriangle(Point3D p1, Point3D p2, Point3D p3)
        {
            var v321 = p3.X * p2.Y * p1.Z;
            var v231 = p2.X * p3.Y * p1.Z;
            var v312 = p3.X * p1.Y * p2.Z;
            var v132 = p1.X * p3.Y * p2.Z;
            var v213 = p2.X * p1.Y * p3.Z;
            var v123 = p1.X * p2.Y * p3.Z;
            return (1.0f / 6.0f) * (-v321 + v231 + v312 - v132 - v213 + v123);
        }

        private double CalculateVolume(DMesh3 mesh)
        {
            double volume = 0f;

            if (mesh != null)
            {
                Point3D p1, p2, p3;
                foreach (var triangle in mesh.Triangles())
                {
                    var v = new Vector3d(mesh.GetVertex(triangle.a));
                    p1 = new Point3D(v.x, v.y, v.z);

                    v = new Vector3d(mesh.GetVertex(triangle.b));
                    p2 = new Point3D(v.x, v.y, v.z);

                    v = new Vector3d(mesh.GetVertex(triangle.c));
                    p3 = new Point3D(v.x, v.y, v.z);

                    volume += SignedVolumeOfTriangle(p1, p2, p3);
                }

            }

            return volume / 1000;
        }

        /// <summary>
        /// Adds more cells and positives to the bitmap used for mold generation
        /// </summary>
        /// <param name="bmp"></param>
        /// <returns></returns>
        private Bitmap3 BitmapOffset(Bitmap3 bmp)
        {
            Bitmap3 mold_bmp = new Bitmap3(new Vector3i(bmp.Dimensions.x + 2, bmp.Dimensions.y + 2, bmp.Dimensions.z + 2)); //extra offsets to expand positive cells
            foreach (Vector3i idx in bmp.Indices())
            {
                //needs to be positive for use to be interested
                if (!bmp.Get(idx))
                    continue;

                //for x
                mold_bmp.Set(new Vector3i(idx.x - 1 , idx.y, idx.z), true);
                mold_bmp.Set(new Vector3i(idx.x + 1, idx.y, idx.z), true);

                //for y
                mold_bmp.Set(new Vector3i(idx.x, idx.y - 1, idx.z), true);
                mold_bmp.Set(new Vector3i(idx.x, idx.y + 1, idx.z), true);

                //for z
                mold_bmp.Set(new Vector3i(idx.x, idx.y, idx.z - 1), true);
                mold_bmp.Set(new Vector3i(idx.x, idx.y, idx.z + 1), true);
            }

            return mold_bmp;
        }


        class PlaneIntersectionTraversal : DMeshAABBTree3.TreeTraversal 
         { 
             public DMesh3 Mesh; 
             public double Z; 
             public List<int> triangles = new List<int>(); 
             public PlaneIntersectionTraversal(DMesh3 mesh, double z)
             { 
                 this.Mesh = mesh; 
                 this.Z = z; 
                 this.NextBoxF = (box, depth) => { 
                     return (Z >= box.Min.z && Z <= box.Max.z); 
                 }; 
                 this.NextTriangleF = (tID) => { 
                     AxisAlignedBox3d box = Mesh.GetTriBounds(tID); 
                     if (Z >= box.Min.z && z <= box.Max.z) 
                         triangles.Add(tID); 
                 }; 
             } 
         }

        // meshToImplicitF() generates a narrow-band distance-field and
        // returns it as an implicit surface, that can be combined with other implicits                       
        Func<DMesh3, int, double, BoundedImplicitFunction3d> meshToImplicitF = (meshIn, numcells, max_offset) => {
            double meshCellsize = meshIn.CachedBounds.MaxDim / numcells;
            MeshSignedDistanceGrid levelSet = new MeshSignedDistanceGrid(meshIn, meshCellsize);
            levelSet.ExactBandWidth = (int)(max_offset / meshCellsize) + 1;
            levelSet.Compute();
            return new DenseGridTrilinearImplicit(levelSet.Grid, levelSet.GridOrigin, levelSet.CellSize);
        };

        // generateMeshF() meshes the input implicit function at
        // the given cell resolution, and writes out the resulting mesh    
        DMesh3 generatMeshF(BoundedImplicitFunction3d root, int numcells)
        {
            MarchingCubes c = new MarchingCubes();
            c.Implicit = root;
            c.RootMode = MarchingCubes.RootfindingModes.LerpSteps;      // cube-edge convergence method
            c.RootModeSteps = 5;                                        // number of iterations
            c.Bounds = root.Bounds();
            c.CubeSize = c.Bounds.MaxDim / numcells;
            c.Bounds.Expand(3 * c.CubeSize);                            // leave a buffer of cells
            c.Generate();
            MeshNormals.QuickCompute(c.Mesh);                           // generate normals
            return c.Mesh;   // write mesh
        }

        //creates surface normals for the triangle
        Vector3D CalculateSurfaceNormal(Point3D p1, Point3D p2, Point3D p3)
        {
            Vector3D v1 = new Vector3D(0, 0, 0);             // Vector 1 (x,y,z) & Vector 2 (x,y,z)
            Vector3D v2 = new Vector3D(0, 0, 0);
            Vector3D normal = new Vector3D(0, 0, 0);

            // Finds The Vector Between 2 Points By Subtracting
            // The x,y,z Coordinates From One Point To Another.

            // Calculate The Vector From Point 2 To Point 1
            v1.X = p1.X - p2.X;                  // Vector 1.x=Vertex[0].x-Vertex[1].x
            v1.Y = p1.Y - p2.Y;                  // Vector 1.y=Vertex[0].y-Vertex[1].y
            v1.Z = p1.Z - p2.Z;                  // Vector 1.z=Vertex[0].y-Vertex[1].z
                                                 // Calculate The Vector From Point 3 To Point 2
            v2.X = p2.X - p3.X;                  // Vector 1.x=Vertex[0].x-Vertex[1].x
            v2.Y = p2.Y - p3.Y;                  // Vector 1.y=Vertex[0].y-Vertex[1].y
            v2.Z = p2.Z - p3.Z;                  // Vector 1.z=Vertex[0].y-Vertex[1].z

            // Compute The Cross Product To Give Us A Surface Normal
            normal.X = v1.Y * v2.Z - v1.Z * v2.Y;   // Cross Product For Y - Z
            normal.Y = v1.Z * v2.X - v1.X * v2.Z;   // Cross Product For X - Z
            normal.Z = v1.X * v2.Y - v1.Y * v2.X;   // Cross Product For X - Y

            normal.Normalize();

            return normal;
        }
    }
}
