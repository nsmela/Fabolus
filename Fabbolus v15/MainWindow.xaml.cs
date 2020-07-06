using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Shapes;
using HelixToolkit.Wpf;

namespace Fabbolus_v15
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Bolus bolus;
        private bool disableUpdates = false;
        private List<MeshGeometry3D> airholes;
        bool airholeToolActive = false;
        Vector3D zaxis = new Vector3D(0, 0, -1);
        bool showMold = false;
        bool tempTransforms = false;

        public MainWindow()
        {
            InitializeComponent();
            airholes = new List<MeshGeometry3D>();
        }

        //User Controls
        public void ImportModelFromFile(object sender, RoutedEventArgs e)
        {
            string filepath = "";

            //find the file to import
            Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog();
            ofd.Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*";
            ofd.Multiselect = false;

            if (ofd.ShowDialog() == true)
            {
                filepath = ofd.FileName;
                bolus = new Bolus(filepath);
                airholes.Clear();

                //import mesh to Viewport
                DisplayModels();
            }
        }

        public void ImportModelFromEclipse(object sender, RoutedEventArgs e)
        {
            //var meshExport = new MeshExport.MainWindow(true);
            //var mesh = meshExport.Mesh();

        }

        public void SmoothModel(object sender, RoutedEventArgs e)
        {
            if (bolus == null)
                return;

            bolus.Smooth(EdgeSlider.Value, SmoothSlider.Value, IterationsSlider.Value, CellsSlider.Value);

            airholes.Clear();

            //import mesh to Viewport
            DisplayModels();
        }

        private void AirholeTool(object sender, RoutedEventArgs e)
        {
            airholeToolActive = !airholeToolActive;

            if (airholeToolActive)
                AirholeToolButton.Background = Brushes.DarkSlateGray;
            else
                AirholeToolButton.Background = SystemColors.ControlBrush;

            if (showMold && airholeToolActive)
                ToggleMoldPreview(sender, e);
                

        }

        /// <summary>
        /// Creates a tube to allow fluids to enter the mold
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void AddHole(object sender, MouseButtonEventArgs e)
        {
            if (!airholeToolActive)
                return;

            try
            {
                Point3D p1 = (Point3D)view1.FindNearestPoint(e.GetPosition(view1));
                var mesh = Airhole(p1);
                airholes.Add(mesh);
                DisplayModels();
            }
            catch { }

        }

        private void ExportMesh_Click(object sender, RoutedEventArgs e)
        {
            //which mesh to export?
            //if a mold mesh exists, export that
            //otherwise, export the displayed mesh
            if (bolus != null)
            {
                Microsoft.Win32.SaveFileDialog sfd = new Microsoft.Win32.SaveFileDialog();
                sfd.Filter = "stl Files *.stl|*.stl|All Files *.*|*.*";

                if (sfd.ShowDialog() == true)
                    bolus.ExportMesh(sfd.FileName);
            }

        }
        //Hidden panels displaying
        public void DisplaySmoothingPanel(object sender, RoutedEventArgs e)
        {
            CloseAllPanels(sender, e);
            SmoothingPanel.Visibility = Visibility.Visible;
        }

        public void DisplayTransformsPanel(object sender, RoutedEventArgs e)
        {
            CloseAllPanels(sender, e);
            TransformsPanel.Visibility = Visibility.Visible;
        }

        public void DisplayMoldPanel(object sender, RoutedEventArgs e)
        {
            CloseAllPanels(sender, e);
            MoldPanel.Visibility = Visibility.Visible;
        }

        public void CloseAllPanels(object sender, RoutedEventArgs e)
        {
            SmoothingPanel.Visibility = Visibility.Collapsed;
            TransformsPanel.Visibility = Visibility.Collapsed;
            MoldPanel.Visibility = Visibility.Collapsed;
        }

        //model transforms
        /// <summary>
        /// during adjustments, the sliders change the displayed mesh's rotation. Completing the dragging and letting go of the mouse saves the rotation directly to the mesh and reset's the slider's value
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void Slider_ValueChanged(object sender, RoutedEventArgs e)
        {
            if (bolus == null)
                return;

            //used to ensure that when we change slider values, it doesn't affect anything else
            if (disableUpdates)
                return;

            //if the tuhmb on the slider isn't being used
            if (!tempTransforms)
            {
                Slider_SetTransforms(sender);
                return;
            }

            //load the slider that issued the event and determine which axis it is
            Slider slider = sender as Slider;

            double angle = slider.Value;
            Vector3D axis = new Vector3D(0, 0, 0);

            switch (slider.Name)
            {
                case "XAxisSlider":
                    axis = new Vector3D(1, 0, 0);
                    break;

                case "YAxisSlider":
                    axis = new Vector3D(0, 1, 0);
                    break;

                case "ZAxisSlider":
                    axis = new Vector3D(0, 0, 1);
                    break;
            }

            

            //apply the rotation based on which slider was used to the displayed model
            AxisAngleRotation3D rotation = new AxisAngleRotation3D(axis, angle);
            RotateTransform3D rotationTransform = new RotateTransform3D(rotation);
            MeshView.Transform = rotationTransform;

            airholes.Clear(); //remove the added tubes based on position fo the mold

            //Show models
            DisplayModels(axis, angle);
        }

        /// <summary>
        /// Works together with Slider_ValueChanged. ValueChanges is the temporary adjustment, DragCompleted saves the change
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Slider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            Slider_SetTransforms(sender);   
        }

        private void Slider_SetTransforms(object sender)
        {
            if (bolus == null)
                return;

            Slider slider = sender as Slider;

            double angle = slider.Value;
            Vector3D axis = new Vector3D(0, 0, 0);

            switch (slider.Name)
            {
                case "XAxisSlider":
                    axis = new Vector3D(1, 0, 0);
                    break;

                case "YAxisSlider":
                    axis = new Vector3D(0, 1, 0);
                    break;

                case "ZAxisSlider":
                    axis = new Vector3D(0, 0, 1);
                    break;
            }


            //rotate bolus mesh
            bolus.Transform(axis, angle);

            disableUpdates = true; //to prevent slider's valuechanged event from affecting anything

            //reset slider value
            slider.Value = 0;

            disableUpdates = false;

            //reset the rotation on the displayed model
            AxisAngleRotation3D rotation = new AxisAngleRotation3D(axis, 0);
            RotateTransform3D rotationTransform = new RotateTransform3D(rotation);
            MeshView.Transform = rotationTransform;
            airholes.Clear(); //remove the added tubes based on position fo the mold

            tempTransforms = false;
            DisplayModels();
        }

        private void SolidOverhangsCheckbox_Click(object sender, RoutedEventArgs e)
        {
            if ((bool)SolidOverhangsCheckbox.IsChecked)
                SoftOverhangsCheckbox.IsChecked = false;

            DisplayModels();
        }

        private void SoftOverhangsCheckbox_Click(object sender, RoutedEventArgs e)
        {
            if ((bool)SoftOverhangsCheckbox.IsChecked)
                SolidOverhangsCheckbox.IsChecked = false;

            DisplayModels();
        }

        private void ResetRotationsButton_Click(object sender, RoutedEventArgs e)
        {
            if (bolus == null)
                return;

            bolus.ResetRotations();

            airholes.Clear();

            DisplayModels();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            airholes.Clear();

            DisplayModels();
        }

        private void ToggleMoldPreview(object sender, RoutedEventArgs e)
        {
            showMold = !showMold;

            if (showMold)
                PreviewMoldButton.Content = "Disable Preview";
            else
                PreviewMoldButton.Content = "Preview Mold";

            if (airholeToolActive && showMold)
                AirholeTool(sender, e);

            DisplayModels();
        }

        private void GenerateMold(object sender, RoutedEventArgs e)
        {
            DisplayMold();
        }

        //Display Methods
        /// <summary>
        /// Clears the displayed models and displays the new ones
        /// </summary>
        private void DisplayModels()
        {
            if (bolus != null)
            {
                //ModelGroup is used to house the model meshes and skins
                Model3DGroup model = new Model3DGroup(); //houses the mesh and the skin

                //import mesh
                MeshGeometry3D rawMesh = bolus.Mesh(); //the base mesh
                DiffuseMaterial rawSkin = new DiffuseMaterial(new SolidColorBrush(Colors.LightGray));
                //rawSkin.Brush.Opacity = 0.8f;
                model.Children.Add(new GeometryModel3D(rawMesh, rawSkin));

                //the mesh's overhangs
                var overhangs = Overhangs();
                if (overhangs != null)
                    model.Children.Add(overhangs);

                //the airholes
                DiffuseMaterial tubes = new DiffuseMaterial(new SolidColorBrush(Colors.Green));
                foreach (MeshGeometry3D mesh in airholes)
                {
                    model.Children.Add(new GeometryModel3D(mesh, tubes));
                }

                //the mold
                if (showMold)
                {
                    MeshGeometry3D mold = bolus.PreviewMold((double)MoldResolutionSlider.Value); //the mold mesh
                    if (mold != null)
                    {
                        DiffuseMaterial lastSkin = new DiffuseMaterial(new SolidColorBrush(Colors.Red));
                        lastSkin.Brush.Opacity = 0.5f;
                        model.Children.Add(new GeometryModel3D(mold, lastSkin));
                    }
                }

                //save the models to the viewport
                MeshView.Content = model;
                MoldView.Content = null;

                //update text info
                DisplayText();

                bolus.ClearMold();
            }

        }

        private void DisplayModels(Vector3D axis, Double angle)
        {
            if (bolus != null)
            {
                //ModelGroup is used to house the model meshes and skins
                Model3DGroup model = new Model3DGroup(); //houses the mesh and the skin

                //import mesh
                MeshGeometry3D rawMesh = bolus.Mesh(); //the base mesh
                DiffuseMaterial rawSkin = new DiffuseMaterial(new SolidColorBrush(Colors.LightGray));
                model.Children.Add(new GeometryModel3D(rawMesh, rawSkin));

                //the mesh's overhangs
                var overhangs = Overhangs(axis, angle);
                if (overhangs != null)
                    model.Children.Add(overhangs);

                //the airholes
                DiffuseMaterial tubes = new DiffuseMaterial(new SolidColorBrush(Colors.Green));
                foreach (MeshGeometry3D mesh in airholes)
                {
                    model.Children.Add(new GeometryModel3D(mesh, tubes));
                }

                //save the models to the viewport
                MeshView.Content = model;

                //display the mold
                if (showMold)
                {
                    Model3DGroup moldGroup = new Model3DGroup();
                    MeshGeometry3D mold = bolus.PreviewMoldWhileRotating((double)MoldResolutionSlider.Value, axis, angle); //the mold mesh
                    if (mold != null)
                    {
                        DiffuseMaterial lastSkin = new DiffuseMaterial(new SolidColorBrush(Colors.Red));
                        lastSkin.Brush.Opacity = 0.5f;
                        moldGroup.Children.Add(new GeometryModel3D(mold, lastSkin));
                        MoldView.Content = moldGroup;
                    }
                }

                DisplayText();

            }
        }

        private void DisplayMold()
        {
            if (bolus != null)
            {
                //ModelGroup is used to house the model meshes and skins
                Model3DGroup model = new Model3DGroup(); //houses the mesh and the skin

                MeshGeometry3D mold = bolus.GenerateMold((double)MoldResolutionSlider.Value, airholes); //the mold mesh
                if (mold != null)
                {
                    DiffuseMaterial outerSkin = new DiffuseMaterial(new SolidColorBrush(Colors.Red));
                    outerSkin.Brush.Opacity = 0.5f;
                    var mesh = new GeometryModel3D(mold, outerSkin);
                    mesh.BackMaterial = new DiffuseMaterial(new SolidColorBrush(Colors.Red));
                    model.Children.Add(mesh);
                }

                //save the models to the viewport
                MeshView.Content = model;
                DisplayText();

            }
        }

        /// <summary>
        /// Returns a coloured mesh showing areas likely to experience 3D printed overhangs
        /// </summary>
        /// <param name="axis"></param>
        /// <param name="angle"></param>
        /// <returns></returns>
        private GeometryModel3D Overhangs(Vector3D axis, double angle)
        {
            //which checkbox is active
            //axis originally shows soft bolus overhangs
            //reverse it for solid overhangs, or exit if nothing is selected
            var reference = new Vector3D(0, 0, 1);

            if ((bool)SolidOverhangsCheckbox.IsChecked)
                reference = new Vector3D(0, 0, -1);
            else if (!(bool)SoftOverhangsCheckbox.IsChecked)
                return null;

            //calculate the reference vector
            //create a reference vector if overhangs are to be displayed
            axis.Negate();
            Matrix3D m = Matrix3D.Identity;
            Quaternion q = new Quaternion(axis, angle);
            m.Rotate(q);
            reference = m.Transform(reference);
            reference.Normalize();


            MeshGeometry3D overhangMesh = ShowSteepAngles(60, reference, false); //the base mesh
            DiffuseMaterial overhangs = new DiffuseMaterial(new SolidColorBrush(Colors.Yellow));
            overhangs.Brush.Opacity = 0.8f;
            return new GeometryModel3D(overhangMesh, overhangs);
        }

        private GeometryModel3D Overhangs()
        {
            //which checkbox is active
            //axis originally shows soft bolus overhangs
            //reverse it for solid overhangs, or exit if nothing is selected
            var reference = new Vector3D(0, 0, 1);

            if ((bool)SolidOverhangsCheckbox.IsChecked)
                reference = new Vector3D(0, 0, -1);
            else if (!(bool)SoftOverhangsCheckbox.IsChecked)
                return null;

            MeshGeometry3D overhangMesh = ShowSteepAngles(60, reference, false); //the base mesh
            DiffuseMaterial overhangs = new DiffuseMaterial(new SolidColorBrush(Colors.Yellow));
            overhangs.Brush.Opacity = 0.8f;
            return new GeometryModel3D(overhangMesh, overhangs);
        }


        /// <summary>
        /// Colored triangles to visualize overhangs
        /// </summary>
        /// <param name="angle"></param>
        /// <param name="inverted"></param>
        /// <returns></returns>
        private MeshGeometry3D ShowSteepAngles(double angle, bool inverted)
        {

            //to store the outgoing mesh
            var mesh = new MeshBuilder(true);
            MeshGeometry3D scannedMesh = bolus.Mesh();

            Vector3D angleDown = new Vector3D(0, 0, -1);
            //reference angle for normals
            if (inverted)
                angleDown = new Vector3D(0, 0, 1);

            //calculate for each triangle
            for (int triangle = 0; triangle < scannedMesh.TriangleIndices.Count; triangle += 3)
            {
                //get the triangle's normal
                int i0 = scannedMesh.TriangleIndices[triangle];
                int i1 = scannedMesh.TriangleIndices[triangle + 1];
                int i2 = scannedMesh.TriangleIndices[triangle + 2];

                Point3D p0 = scannedMesh.Positions[i0];
                Point3D p1 = scannedMesh.Positions[i1];
                Point3D p2 = scannedMesh.Positions[i2];

                var normal = CalculateSurfaceNormal(p0, p1, p2);

                //calculate normal's angle from the ground
                //using the z-axis as to determine how the angle if from the ground
                var degrees = Vector3D.AngleBetween(normal, angleDown);

                //if angle less than steepangle, add the triangle to the overhang mesh 
                if (degrees < angle)
                    mesh.AddTriangle(p0, p1, p2);
            }

            return mesh.ToMesh();
        }

        private MeshGeometry3D ShowSteepAngles(double angle, Vector3D reference, bool inverted)
        {

            //to store the outgoing mesh
            var mesh = new MeshBuilder(true);
            MeshGeometry3D scannedMesh = bolus.Mesh();

            //calculate for each triangle
            for (int triangle = 0; triangle < scannedMesh.TriangleIndices.Count; triangle += 3)
            {
                //get the triangle's normal
                int i0 = scannedMesh.TriangleIndices[triangle];
                int i1 = scannedMesh.TriangleIndices[triangle + 1];
                int i2 = scannedMesh.TriangleIndices[triangle + 2];

                Point3D p0 = scannedMesh.Positions[i0];
                Point3D p1 = scannedMesh.Positions[i1];
                Point3D p2 = scannedMesh.Positions[i2];

                var normal = CalculateSurfaceNormal(p0, p1, p2);

                //calculate normal's angle from the ground
                //using the z-axis as to determine how the angle if from the ground
                var degrees = Vector3D.AngleBetween(normal, reference);

                //if angle less than steepangle, add the triangle to the overhang mesh 
                if (degrees < angle)
                    mesh.AddTriangle(p0, p1, p2);
            }

            return mesh.ToMesh();
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

        private void DisplayText()
        {
            string text = "Mesh Statistics: \r\n";

            if (bolus != null)
            {
                text += "   Volume: \r\n" + bolus.DisplayVolumes(); 
                text += "\r\n";
                text += "Triangle Count = " + (bolus.Mesh().TriangleIndices.Count / 3).ToString();

            }

            RightTextblock.Text = text;
        }

        private MeshGeometry3D Airhole (Point3D p1)
        {
            var mesh = new MeshBuilder(true);
            try
            {
                Point3D p2 = new Point3D(p1.X, p1.Y, p1.Z);

                double ceiling = bolus.Ceiling();
                double diameter = (double)HoleDiameterSlider.Value;
                double ballRadius = (double)BallRadiusSlider.Value;

                mesh.AddSphere(p1, ballRadius);


                if ((double)ConeLengthSlider.Value > 0)
                {
                    p2 = new Point3D(p1.X, p1.Y, p1.Z + (double)ConeLengthSlider.Value);
                    mesh.AddCone(p2, new Point3D(p1.X, p1.Y, p1.Z - ballRadius), diameter, true, 32);

                    mesh.AddCylinder(p2, new Point3D(p2.X, p2.Y, ceiling + 20), diameter, 32, false, true);
                }
                else
                {
                    mesh.AddCylinder(p2, new Point3D(p2.X, p2.Y, ceiling + 20), ballRadius, 32, false, true);
                }
                
            }
            catch { }

            return mesh.ToMesh();
        }

        private void Slider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            tempTransforms = true;
        }
    }
}
