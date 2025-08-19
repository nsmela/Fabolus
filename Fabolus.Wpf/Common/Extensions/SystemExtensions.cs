using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Common.Extensions;
internal static class SystemExtensions {

    public static Vector3 ToVector3(this System.Numerics.Vector3 vector) => new Vector3(vector.X, vector.Y, vector.Z);
    public static Point ToSharpPoint(this System.Windows.Point point) => new Point((int)point.X, (int)point.Y);

}
