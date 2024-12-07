using g3;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using Bolus = Fabolus.Core.BolusModel.Bolus;

namespace Fabolus.Core.Smoothing;

public record struct PoissonSettings(int Depth, float Scale, int SamplesPerNode, float EdgeLength);

public class PoissonSmoothing {
    private static string BASE_DIRECTORY = AppDomain.CurrentDomain.BaseDirectory + @"\Files\";
    private static string TEMP_FOLDER = BASE_DIRECTORY + @"\temp\";
    private static string RECONSTRUCTOR_FILE_PATH = BASE_DIRECTORY + @"PoissonRecon.exe";

    private void ErrorMessage(string title, string message) {
        MessageBox.Show(message, title);
    }

    public void Initialize(Fabolus.Core.BolusModel.Bolus bolus) {
        var mesh = bolus.Mesh;

        if (mesh is null || mesh.TriangleCount == 0) { 
            ErrorMessage("Error initializing smoothing", $"Poisson Smoothing needs a valid DMesh3 object to initialize");
            return;
        }

        //create output file
        //prevents multiple calls to the same thing
        //preemptively save the bolus as a temp file to save time smoothing with poisson

        //check the poisson reconstructor exists
        if (!File.Exists(RECONSTRUCTOR_FILE_PATH)) {
            ErrorMessage("Poisson Reconstructor was not found!", $"File not found at {RECONSTRUCTOR_FILE_PATH}");
            return;
        }

        //create temp folder where exe is located
        Directory.CreateDirectory(TEMP_FOLDER);

        string exportFile = TEMP_FOLDER + @"temp.ply";
        SaveDMeshToPLYFile(mesh, exportFile);

        if (!File.Exists(exportFile)) {
            MessageBox.Show(string.Format("Failed to write temp ply file at {0}!", exportFile), "Developer");
            return;
        }
    }
    public Fabolus.Core.BolusModel.Bolus Smooth(PoissonSettings settings) {
        //run poisson reconstructor
        ExecutePoisson(TEMP_FOLDER + @"temp.ply", TEMP_FOLDER + @"temp_smooth", settings.Depth, settings.Scale, settings.SamplesPerNode);

        //load new mesh from ply in folder
        var result = ReadPLYFileToDMesh(TEMP_FOLDER + @"temp_smooth.ply");


        //reduce the mesh size 
        // MeshRefinement.Remesh(result, EdgeLength); //for some reason, this is creating more triangles than positions
        //cull the excess 

        return new Fabolus.Core.BolusModel.Bolus(result);
    }

    private static void ExecutePoisson(string inputFile, string outputFile, int depth, float scale, int samples) {
        if (!File.Exists(inputFile)) { throw new FileNotFoundException("Poisson failed to find inital file", inputFile); }

        //Use ProcessStartInfo class
        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.CreateNoWindow = true;
        startInfo.UseShellExecute = true;
        startInfo.WindowStyle = ProcessWindowStyle.Hidden;
        startInfo.FileName = RECONSTRUCTOR_FILE_PATH;
        startInfo.Arguments = string.Format(@" --in ""{0}"" --out ""{1}"" --depth {2} --scale {3} --samplesPerNode {4}",
            inputFile, //in
            outputFile, //out
            depth.ToString(), //depth
            scale.ToString(), //scale
            samples.ToString()); //samples

        //send the command
        try {
            using (Process exeProcess = Process.Start(startInfo)) {
                exeProcess.WaitForExit();
            }

        } catch (Exception ex) {
            MessageBox.Show("Recon failed! " + ex.Message);
        }
        
    }

    private static void SaveDMeshToPLYFile(DMesh3 mesh, string outputFileName) {
        if (mesh == null)
            return;

        MeshNormals.QuickCompute(mesh);

        if (File.Exists(outputFileName)) {
            File.SetAttributes(outputFileName, FileAttributes.Normal);
            File.Delete(outputFileName);
        }

        using (TextWriter writer = new StreamWriter(outputFileName)) {
            writer.WriteLine("ply");
            writer.WriteLine("format ascii 1.0");
            writer.WriteLine("element vertex " + mesh.VertexCount);

            writer.WriteLine("property float x");
            writer.WriteLine("property float y");
            writer.WriteLine("property float z");
            writer.WriteLine("property float nx");
            writer.WriteLine("property float ny");
            writer.WriteLine("property float nz");

            writer.WriteLine("element face " + mesh.TriangleCount);

            writer.WriteLine("property list uchar int vertex_indices");

            writer.WriteLine("end_header");

            for (int v = 0; v < mesh.VertexCount; v++) {
                Vector3d normal = mesh.GetVertexNormal(v);

                writer.Write(mesh.GetVertex(v).x.ToAscii() + " ");
                writer.Write(mesh.GetVertex(v).y.ToAscii() + " ");
                writer.Write(mesh.GetVertex(v).z.ToAscii() + " ");
                writer.Write(normal.x.ToAscii() + " ");
                writer.Write(normal.y.ToAscii() + " ");
                writer.Write(normal.z.ToAscii());

                writer.WriteLine();
            }

            int i = 0;
            while (i < mesh.TriangleCount) {
                var triangle = mesh.GetTriangle(i);

                writer.Write("3 ");
                writer.Write(triangle.a + " ");
                writer.Write(triangle.b + " ");
                writer.Write(triangle.c + " ");
                writer.WriteLine();
                i++;
            }
        }
    }
    private static DMesh3 ReadPLYFileToDMesh(string filepath) {
        //verify file exists
        if (File.Exists(filepath)) {
            List<string> headers = new List<string>();

            bool endheader = false;
            using (BinaryReader b = new BinaryReader(File.Open(filepath, FileMode.Open))) {
                //reads the header
                while (!endheader) {
                    string line = ReadReturnTerminatedString(b);
                    headers.Add(line);
                    if (line == "end_header") {
                        endheader = true;
                    }
                }

                //determining the vertexes and faces
                int vertexRef = headers.FindIndex(element => element.StartsWith("element vertex", StringComparison.Ordinal));
                string text = headers[vertexRef].Substring(headers[vertexRef].LastIndexOf(' ') + 1);
                int number_of_vertexes = Convert.ToInt32(text);

                int faceRef = headers.FindIndex(element => element.StartsWith("element face", StringComparison.Ordinal));
                text = headers[faceRef].Substring(headers[faceRef].LastIndexOf(' ') + 1);
                int number_of_faces = Convert.ToInt32(text);

                //read the vertexes
                DMesh3 mesh = new DMesh3(true); //want normals
                for (int i = 0; i < number_of_vertexes; i++) {
                    float x, y, z;
                    x = b.ReadSingle();
                    y = b.ReadSingle();
                    z = b.ReadSingle();

                    mesh.AppendVertex(new Vector3d(x, y, z));
                }

                //read the faces
                for (int i = 0; i < number_of_faces; i++) {
                    b.ReadByte();//skips the first bye, always '3'
                    int v0 = b.ReadInt32();
                    int v1 = b.ReadInt32();
                    int v2 = b.ReadInt32();

                    mesh.AppendTriangle(v0, v1, v2);
                }

                return mesh;
            }

        } else {
            MessageBox.Show("The file " + filepath + " does not exist!");
        }
        return null;
    }
    private static string ReadReturnTerminatedString(BinaryReader stream) {
        string str = "";
        char ch;
        while ((ch = stream.ReadChar()) != 10)
            str = str + ch;
        return str;
    }

}

internal static class Extensions {
    public static string ToAscii(this double value) =>
        value.ToString("E", CultureInfo.InvariantCulture);
}
