#include "fabolus_geometry.h"
#include "manifold_dynamic.hpp"

#include <igl/AABB.h>
#include <igl/fast_winding_number.h>

#include <Eigen/Core>

#include <atomic>
#include <cstring>
#include <cmath>
#include <exception>
#include <memory>
#include <new>
#include <thread>
#include <vector>

namespace {

using MatrixXd = Eigen::MatrixXd;
using MatrixXi = Eigen::MatrixXi;

} // namespace

/*
 * A mesh with everything needed to sign a distance to it.
 *
 * The precomputation lives here rather than being rebuilt per query, and every member is
 * read-only once built - which is what makes Query safe to call from the several threads
 * Manifold meshes on.
 */
struct FabolusSdf {
    MatrixXd V;
    MatrixXi F;
    igl::AABB<MatrixXd, 3> tree;

    igl::FastWindingNumberBVH windingBvh;

    /// Barnes-Hut accuracy for the winding number. libigl recommends 2; the sign only has to be
    /// right, and it is not close to the 0.5 threshold anywhere the magnitude matters.
    static constexpr float WindingAccuracy = 2.0f;

    /*
     * Signed distance to the surface, negative inside.
     *
     * The magnitude always comes from the AABB tree, which gives the exact closest-point
     * distance; only the sign is asked of libigl. That split is deliberate, and neither of
     * libigl's own signed_distance helpers can stand in for it:
     *
     *   signed_distance_fast_winding_number returns sqrt(d^2) * (1 - 2|w|) - the distance
     *   *scaled* by the winding number rather than merely signed by it. Away from 0 and 1 the
     *   magnitude is pulled towards zero, so an isosurface asked for at 1mm lands somewhere
     *   short of it. Measured on a bolus, up to 0.1mm short, which is a tenth of the offset.
     *
     *   signed_distance_pseudonormal returns a true distance, but its sign disagreed with the
     *   winding number on roughly one sample in three hundred of the same bolus - enough
     *   scattered flips to grow an erode-dilate by 2mm.
     *
     * The winding number is also the robust choice on exactly the input this sees: a scan with
     * holes or self-intersections has no consistent inside for a normal test to consult, and the
     * winding number degrades gracefully there rather than answering arbitrarily.
     */
    double Query(const Eigen::RowVector3d& point) const
    {
        double squared;
        Eigen::RowVector3d closest;
        int face = -1;
        squared = tree.squared_distance(V, F, point, face, closest);

        const double distance = std::sqrt(squared);

        // 0.5 is the surface: below it the point is outside, above it enclosed.
        const float winding = igl::fast_winding_number(
            windingBvh, WindingAccuracy, point.cast<float>().eval());

        return winding > 0.5f ? -distance : distance;
    }
};

namespace {

/// Runs `body` over [0, count) across the hardware's threads. libigl has parallel_for, but it is
/// behind a header this otherwise does not need, and the shape here is only ever a flat range.
template <typename Body>
void ParallelFor(size_t count, const Body& body)
{
    unsigned int threads = std::thread::hardware_concurrency();
    if (threads == 0) threads = 1;

    // Below this the thread handoff costs more than the work.
    if (count < 1024 || threads == 1) {
        for (size_t i = 0; i < count; ++i) body(i);
        return;
    }

    std::atomic<size_t> next{0};
    const size_t chunk = 256;

    std::vector<std::thread> workers;
    workers.reserve(threads);

    for (unsigned int t = 0; t < threads; ++t) {
        workers.emplace_back([&] {
            for (;;) {
                size_t start = next.fetch_add(chunk);
                if (start >= count) return;

                size_t end = start + chunk;
                if (end > count) end = count;
                for (size_t i = start; i < end; ++i) body(i);
            }
        });
    }

    for (auto& worker : workers) worker.join();
}

} // namespace

extern "C" {

int32_t fabolus_geometry_abi_version(void)
{
    return FABOLUS_GEOMETRY_ABI_VERSION;
}

void fabolus_mesh_free(FabolusMesh* mesh)
{
    if (mesh == nullptr) return;

    delete[] mesh->vertices;
    delete[] mesh->triangles;
    mesh->vertices = nullptr;
    mesh->triangles = nullptr;
    mesh->vertex_count = 0;
    mesh->triangle_count = 0;
}

FabolusSdf* fabolus_sdf_create(
    const double*  vertices,
    size_t         vertex_count,
    const int32_t* triangles,
    size_t         triangle_count,
    int32_t        sign_mode)
{
    if (vertices == nullptr || triangles == nullptr) return nullptr;
    if (vertex_count == 0 || triangle_count == 0) return nullptr;

    try {
        (void)sign_mode; // Retained in the ABI; the winding number now signs every query.
        auto sdf = std::make_unique<FabolusSdf>();

        sdf->V.resize(static_cast<Eigen::Index>(vertex_count), 3);
        for (size_t i = 0; i < vertex_count; ++i) {
            sdf->V(static_cast<Eigen::Index>(i), 0) = vertices[i * 3];
            sdf->V(static_cast<Eigen::Index>(i), 1) = vertices[i * 3 + 1];
            sdf->V(static_cast<Eigen::Index>(i), 2) = vertices[i * 3 + 2];
        }

        sdf->F.resize(static_cast<Eigen::Index>(triangle_count), 3);
        for (size_t i = 0; i < triangle_count; ++i) {
            for (int c = 0; c < 3; ++c) {
                int32_t index = triangles[i * 3 + c];
                if (index < 0 || static_cast<size_t>(index) >= vertex_count) return nullptr;
                sdf->F(static_cast<Eigen::Index>(i), c) = index;
            }
        }

        // Both structures are built up front and read-only afterwards: the level set asks for
        // several hundred thousand queries, so anything rebuilt per query would dominate.
        sdf->tree.init(sdf->V, sdf->F);
        igl::fast_winding_number(
            sdf->V, sdf->F, static_cast<int>(FabolusSdf::WindingAccuracy), sdf->windingBvh);

        return sdf.release();
    } catch (const std::exception&) {
        return nullptr;
    } catch (...) {
        return nullptr;
    }
}

void fabolus_sdf_destroy(FabolusSdf* sdf)
{
    delete sdf;
}

int32_t fabolus_sdf_query(
    const FabolusSdf* sdf,
    const double*     points,
    size_t            count,
    double*           out)
{
    if (sdf == nullptr || points == nullptr || out == nullptr) return FABOLUS_ERROR_INVALID_ARGUMENT;
    if (count == 0) return FABOLUS_OK;

    try {
        ParallelFor(count, [&](size_t i) {
            Eigen::RowVector3d point(points[i * 3], points[i * 3 + 1], points[i * 3 + 2]);
            out[i] = sdf->Query(point);
        });
        return FABOLUS_OK;
    } catch (const std::exception&) {
        return FABOLUS_ERROR_EXCEPTION;
    } catch (...) {
        return FABOLUS_ERROR_EXCEPTION;
    }
}

int32_t fabolus_offset(
    const double*  vertices,
    size_t         vertex_count,
    const int32_t* triangles,
    size_t         triangle_count,
    double         offset_distance,
    const double*  bounds,
    double         edge_length,
    int32_t        sign_mode,
    FabolusMesh*   out)
{
    if (out == nullptr || bounds == nullptr) return FABOLUS_ERROR_INVALID_ARGUMENT;
    if (edge_length <= 0) return FABOLUS_ERROR_INVALID_ARGUMENT;

    std::memset(out, 0, sizeof(*out));

    const auto& manifold = fabolus::Manifold();
    if (!manifold.loaded) return FABOLUS_ERROR_MANIFOLD_UNAVAILABLE;

    std::unique_ptr<FabolusSdf, void (*)(FabolusSdf*)> sdf(
        fabolus_sdf_create(vertices, vertex_count, triangles, triangle_count, sign_mode),
        fabolus_sdf_destroy);

    if (!sdf) return FABOLUS_ERROR_EMPTY_MESH;

    void* box = nullptr;
    void* solid = nullptr;
    void* meshGl = nullptr;
    int32_t status = FABOLUS_OK;

    try {
        box = manifold.alloc_box();
        manifold.box(box, bounds[0], bounds[1], bounds[2], bounds[3], bounds[4], bounds[5]);

        // Manifold keeps the region where the field is above the level, so the field is handed
        // over negated - positive inside the solid - and the surface `offset_distance` outside
        // sits at the level of the same magnitude, negated to match.
        auto sample = [](double x, double y, double z, void* context) -> double {
            const auto* field = static_cast<const FabolusSdf*>(context);
            return -field->Query(Eigen::RowVector3d(x, y, z));
        };

        solid = manifold.alloc_manifold();
        manifold.level_set(
            solid, sample, box, edge_length, -offset_distance, 0, sdf.get());

        if (manifold.status(solid) != 0 || manifold.num_tri(solid) == 0) {
            status = FABOLUS_ERROR_LEVEL_SET_FAILED;
        } else {
            meshGl = manifold.alloc_meshgl64();
            manifold.get_meshgl64(meshGl, solid);

            const size_t vertexCount = manifold.meshgl64_num_vert(meshGl);
            const size_t triCount = manifold.meshgl64_num_tri(meshGl);
            const size_t stride = manifold.meshgl64_num_prop(meshGl);
            const size_t propsLength = manifold.meshgl64_vert_properties_length(meshGl);
            const size_t triLength = manifold.meshgl64_tri_length(meshGl);

            // The position is only the first three of however many properties a vertex carries,
            // and the two lengths are cross-checked so a disagreement is reported rather than
            // read past the end of the buffer.
            if (vertexCount == 0 || triCount == 0 || stride < 3 ||
                propsLength != vertexCount * stride || triLength != triCount * 3) {
                status = FABOLUS_ERROR_LEVEL_SET_FAILED;
            } else {
                std::vector<double> props(propsLength);
                std::vector<uint64_t> tris(triLength);
                manifold.meshgl64_vert_properties(props.data(), meshGl);
                manifold.meshgl64_tri_verts(tris.data(), meshGl);

                out->vertices = new double[vertexCount * 3];
                out->triangles = new uint32_t[triLength];
                out->vertex_count = vertexCount;
                out->triangle_count = triCount;

                for (size_t i = 0; i < vertexCount; ++i) {
                    out->vertices[i * 3] = props[i * stride];
                    out->vertices[i * 3 + 1] = props[i * stride + 1];
                    out->vertices[i * 3 + 2] = props[i * stride + 2];
                }
                for (size_t i = 0; i < triLength; ++i) {
                    out->triangles[i] = static_cast<uint32_t>(tris[i]);
                }
            }
        }
    } catch (const std::bad_alloc&) {
        fabolus_mesh_free(out);
        status = FABOLUS_ERROR_EXCEPTION;
    } catch (const std::exception&) {
        fabolus_mesh_free(out);
        status = FABOLUS_ERROR_EXCEPTION;
    } catch (...) {
        fabolus_mesh_free(out);
        status = FABOLUS_ERROR_EXCEPTION;
    }

    if (meshGl != nullptr) manifold.delete_meshgl64(meshGl);
    if (solid != nullptr) manifold.delete_manifold(solid);
    if (box != nullptr) manifold.delete_box(box);

    return status;
}

} // extern "C"
