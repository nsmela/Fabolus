// Global usings are per-assembly, so the test project needs its own copy of the aliases
// Fabolus.Core declares - a test reads `IMesh` and `Vector3` meaning the same types the code
// under test means. Keep this in step with src/Fabolus.Core/GeometryGlobalUsings.cs.
global using Vector3 = GeometryEngine.Core.Geometry.Primitives.Vec3;
global using Vector2 = GeometryEngine.Core.Geometry.Primitives.Vec2;
global using IGeometryEngine = GeometryEngine.Core.Geometry.IGeometryEngine;
global using IMesh = GeometryEngine.Core.Geometry.IMesh;
global using Polygon2D = GeometryEngine.Core.Geometry.PlanarPolygon;
global using RaycastHit = GeometryEngine.Core.Geometry.RayHit;
global using MeshStatistics = GeometryEngine.Core.Geometry.MeshStatistics;
global using TopologyValidation = GeometryEngine.Core.Geometry.TopologyValidation;
global using MeshMetadata = GeometryEngine.Core.Geometry.MeshMetadata;
global using MeshOperation = GeometryEngine.Core.Geometry.MeshOperation;
