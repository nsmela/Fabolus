﻿using Fabolus.Core.Common;
using g3;
using HelixToolkit.Wpf.SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Transform3DGroup = System.Windows.Media.Media3D.Transform3DGroup;
using MeshHelper = Fabolus.Wpf.Common.Mesh.MeshHelper;
using Fabolus.Core;
using SharpDX;

namespace Fabolus.Wpf.Common.Bolus;
public class BolusModel : Fabolus.Core.Bolus.Bolus {
    public MeshGeometry3D Geometry { get; set; }
    public BolusRotation Transforms { get; set; } = new();
    public Vector3 TranslateOffset { get; set; } = Vector3.Zero;

    #region Constructors
    public BolusModel() {
        Mesh = new();
        Geometry = new();
        Transforms = new();
    }

    public BolusModel(DMesh3 mesh) {
        SetMesh(mesh);
    }

    public BolusModel(MeshGeometry3D geometry) {
        SetGeometry(geometry);
    }

    #endregion

    #region Public Methods

    public bool IsLoaded =>
        Mesh is not null &&
        Mesh.VertexCount > 0 &&
        Geometry is not null &&
        Geometry.Positions.Count > 0;

    public void SetMesh(DMesh3 mesh) {
        Mesh = mesh;
        Geometry = mesh.ToGeometry();
        Transforms = new();
    }

    public void SetGeometry(MeshGeometry3D geometry) {
        Geometry = geometry;
        Mesh = geometry.ToDMesh();
        Transforms = new();
    }

    #endregion
}