using HelixToolkit.Wpf.SharpDX;

namespace Fabolus.Wpf.Features.Smoothing;

public static class CuttingPlane
{
    public static Element3D Create(Action<double> onHeightChanged, Func<double> getMinZ = null, Func<double> getMaxZ = null) {
        var element = new UITranslateManipulator3D {
            Direction = SharpDX.Vector3.UnitZ,
            Material = DiffuseMaterials.Pearl,
            Offset = SharpDX.Vector3.Zero,
            Length = 10.0,
            Diameter = 5.0
        };

        // HelixToolkit updates TargetTransform by replacing it with a new MatrixTransform3D during drag.
        // We bind Transform to TargetTransform so the manipulator moves visually along with the drag.
        var binding = new System.Windows.Data.Binding(nameof(UITranslateManipulator3D.TargetTransform)) {
            Source = element,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        System.Windows.Data.BindingOperations.SetBinding(element, Element3D.TransformProperty, binding);

        bool isUpdating = false;

        // Listen to when TargetTransform gets replaced
        var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
            UITranslateManipulator3D.TargetTransformProperty, typeof(UITranslateManipulator3D));
            
        descriptor.AddValueChanged(element, (s, e) => {
            if (isUpdating) return;

            double currentZ = 0;
            if (element.TargetTransform is System.Windows.Media.Media3D.MatrixTransform3D matrixTransform) {
                currentZ = matrixTransform.Matrix.OffsetZ;
            } else if (element.TargetTransform is System.Windows.Media.Media3D.TranslateTransform3D translateTransform) {
                currentZ = translateTransform.OffsetZ;
            }

            double minZ = getMinZ?.Invoke() ?? -double.MaxValue;
            double maxZ = getMaxZ?.Invoke() ?? double.MaxValue;
            
            // Safeguard against inverted bounds if geometry is completely empty
            if (minZ > maxZ) {
                var temp = minZ;
                minZ = maxZ;
                maxZ = temp;
            }
            
            double clampedZ = Math.Clamp(currentZ, minZ, maxZ);

            if (Math.Abs(currentZ - clampedZ) > 0.0001) {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                    isUpdating = true;
                    element.TargetTransform = new System.Windows.Media.Media3D.TranslateTransform3D(0, 0, clampedZ);
                    isUpdating = false;
                }));
                
                // Still invoke with the clamped value so the scene planes don't go out of bounds
                onHeightChanged?.Invoke(clampedZ);
                return; 
            }

            onHeightChanged?.Invoke(clampedZ);
        });

        // Initialize with zero transform
        element.TargetTransform = new System.Windows.Media.Media3D.TranslateTransform3D(0, 0, 0);

        return element;
    }
}
