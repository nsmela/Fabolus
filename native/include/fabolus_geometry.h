/*
 * fabolus_geometry - native distance-field and offset support for Geometry.Manifold.
 *
 * Exists for one reason: Manifold's level-set mesher asks for a signed distance one point at a
 * time, and answering those from managed code costs a P/Invoke transition per sample. A bolus
 * offset is a few hundred thousand samples, so the transitions dominate - the field itself is
 * cheap by comparison. Everything here is shaped so the per-sample work never leaves native
 * code: fabolus_offset runs the whole offset, callback included, behind a single call.
 *
 * Signing comes from libigl, which offers both the angle-weighted pseudonormal test and the fast
 * winding number. The latter is the reason this exists at all beyond speed: it gives a sensible
 * sign on meshes with holes and self-intersections, which a pseudonormal cannot, and imported
 * patient scans are routinely neither closed nor clean.
 */
#ifndef FABOLUS_GEOMETRY_H
#define FABOLUS_GEOMETRY_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32)
#  if defined(FABOLUS_GEOMETRY_BUILD)
#    define FABOLUS_API __declspec(dllexport)
#  else
#    define FABOLUS_API __declspec(dllimport)
#  endif
#else
#  define FABOLUS_API __attribute__((visibility("default")))
#endif

/* How the sign of a distance is decided. */
typedef enum FabolusSignMode {
    /* Angle-weighted pseudonormal (Baerentzen and Aanaes). Fast, and exact on a closed mesh. */
    FABOLUS_SIGN_PSEUDONORMAL = 0,
    /* Fast winding number (Barill et al.). Slower, but meaningful on open or self-intersecting
       input, where a pseudonormal is not. */
    FABOLUS_SIGN_FAST_WINDING_NUMBER = 1
} FabolusSignMode;

typedef enum FabolusStatus {
    FABOLUS_OK = 0,
    FABOLUS_ERROR_INVALID_ARGUMENT = -1,
    FABOLUS_ERROR_EMPTY_MESH = -2,
    /* manifoldc could not be loaded, or lacks an entry point this needs. */
    FABOLUS_ERROR_MANIFOLD_UNAVAILABLE = -3,
    /* The level set ran but produced nothing, or Manifold rejected the result. */
    FABOLUS_ERROR_LEVEL_SET_FAILED = -4,
    FABOLUS_ERROR_EXCEPTION = -5
} FabolusStatus;

/* Bumped whenever the shape of anything below changes. The managed binding checks it on load,
   so a stale binary beside a new assembly is reported rather than crashed through. */
#define FABOLUS_GEOMETRY_ABI_VERSION 1

FABOLUS_API int32_t fabolus_geometry_abi_version(void);

/* An owned triangle mesh. Free with fabolus_mesh_free. */
typedef struct FabolusMesh {
    double*   vertices;        /* xyz interleaved, 3 * vertex_count doubles */
    size_t    vertex_count;
    uint32_t* triangles;       /* 3 * triangle_count indices */
    size_t    triangle_count;
} FabolusMesh;

FABOLUS_API void fabolus_mesh_free(FabolusMesh* mesh);

/* A mesh's distance structures, built once and queried many times. */
typedef struct FabolusSdf FabolusSdf;

/*
 * Builds the acceleration structures for a mesh. Returns NULL on bad input.
 * The arrays are copied, so the caller may free them immediately.
 */
FABOLUS_API FabolusSdf* fabolus_sdf_create(
    const double*  vertices,
    size_t         vertex_count,
    const int32_t* triangles,
    size_t         triangle_count,
    int32_t        sign_mode);

FABOLUS_API void fabolus_sdf_destroy(FabolusSdf* sdf);

/*
 * Signed distance for each of `count` points, negative inside the solid. Queries run in
 * parallel; `points` is xyz interleaved and `out` holds `count` results.
 */
FABOLUS_API int32_t fabolus_sdf_query(
    const FabolusSdf* sdf,
    const double*     points,
    size_t            count,
    double*           out);

/*
 * Re-meshes the surface at `offset_distance` from the input - positive outwards - by sampling a
 * distance field and handing it to Manifold's level-set mesher, whose output is a closed solid
 * by construction.
 *
 * The whole operation happens here so the mesher's per-sample callback stays native. `bounds` is
 * the volume to sample, as min x/y/z then max x/y/z; it must already contain the offset surface,
 * since nothing outside it is meshed. `edge_length` is Manifold's target output edge length,
 * which drives both fidelity and cost.
 */
FABOLUS_API int32_t fabolus_offset(
    const double*  vertices,
    size_t         vertex_count,
    const int32_t* triangles,
    size_t         triangle_count,
    double         offset_distance,
    const double*  bounds,
    double         edge_length,
    int32_t        sign_mode,
    FabolusMesh*   out);

#ifdef __cplusplus
}
#endif

#endif /* FABOLUS_GEOMETRY_H */
