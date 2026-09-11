/*
 * Runtime access to manifoldc, resolved by name rather than linked.
 *
 * Linking would need manifoldc's import library, and the binaries this ships beside are just the
 * two DLLs - no .lib. Resolving at runtime also means the shim loads on a machine without
 * Manifold at all and reports that as a status, which is what the managed side expects.
 */
#ifndef FABOLUS_MANIFOLD_DYNAMIC_HPP
#define FABOLUS_MANIFOLD_DYNAMIC_HPP

#include <cstdint>
#include <cstddef>
#include <mutex>

#if defined(_WIN32)
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <dlfcn.h>
#endif

namespace fabolus {

/* Mirrors ManifoldSdf from manifoldc.h. */
using ManifoldSdfFn = double (*)(double, double, double, void*);

struct ManifoldApi {
    void*   (*alloc_manifold)() = nullptr;
    void    (*delete_manifold)(void*) = nullptr;
    void*   (*alloc_box)() = nullptr;
    void    (*delete_box)(void*) = nullptr;
    void*   (*box)(void*, double, double, double, double, double, double) = nullptr;
    void*   (*level_set)(void*, ManifoldSdfFn, void*, double, double, double, void*) = nullptr;
    int32_t (*status)(void*) = nullptr;
    size_t  (*num_tri)(void*) = nullptr;
    void*   (*alloc_meshgl64)() = nullptr;
    void    (*delete_meshgl64)(void*) = nullptr;
    void*   (*get_meshgl64)(void*, void*) = nullptr;
    size_t  (*meshgl64_num_vert)(void*) = nullptr;
    size_t  (*meshgl64_num_tri)(void*) = nullptr;
    size_t  (*meshgl64_num_prop)(void*) = nullptr;
    size_t  (*meshgl64_vert_properties_length)(void*) = nullptr;
    size_t  (*meshgl64_tri_length)(void*) = nullptr;
    double* (*meshgl64_vert_properties)(void*, void*) = nullptr;
    uint64_t* (*meshgl64_tri_verts)(void*, void*) = nullptr;

    bool loaded = false;
};

namespace detail {

inline void* OpenLibrary()
{
#if defined(_WIN32)
    /* Already in the process if the managed side has used Manifold; otherwise found by the
       ordinary search, which includes the directory this shim was loaded from. */
    HMODULE handle = ::GetModuleHandleA("manifoldc.dll");
    if (handle == nullptr) handle = ::LoadLibraryA("manifoldc.dll");
    return reinterpret_cast<void*>(handle);
#else
    void* handle = ::dlopen("libmanifoldc.so", RTLD_NOW | RTLD_GLOBAL);
    if (handle == nullptr) handle = ::dlopen("manifoldc.so", RTLD_NOW | RTLD_GLOBAL);
    if (handle == nullptr) handle = ::dlopen("libmanifoldc.dylib", RTLD_NOW | RTLD_GLOBAL);
    return handle;
#endif
}

inline void* Symbol(void* handle, const char* name)
{
#if defined(_WIN32)
    return reinterpret_cast<void*>(::GetProcAddress(reinterpret_cast<HMODULE>(handle), name));
#else
    return ::dlsym(handle, name);
#endif
}

} // namespace detail

/*
 * Resolves manifoldc once. A single missing entry point fails the whole thing: a partial API is
 * a version mismatch, and calling through the half of it that resolved would crash somewhere
 * less obvious than here.
 */
inline const ManifoldApi& Manifold()
{
    static ManifoldApi api;
    static std::once_flag once;

    std::call_once(once, [] {
        void* handle = detail::OpenLibrary();
        if (handle == nullptr) return;

        bool complete = true;
        auto bind = [&](auto& target, const char* name) {
            void* symbol = detail::Symbol(handle, name);
            if (symbol == nullptr) { complete = false; return; }
            target = reinterpret_cast<std::decay_t<decltype(target)>>(symbol);
        };

        bind(api.alloc_manifold, "manifold_alloc_manifold");
        bind(api.delete_manifold, "manifold_delete_manifold");
        bind(api.alloc_box, "manifold_alloc_box");
        bind(api.delete_box, "manifold_delete_box");
        bind(api.box, "manifold_box");
        bind(api.level_set, "manifold_level_set");
        bind(api.status, "manifold_status");
        bind(api.num_tri, "manifold_num_tri");
        bind(api.alloc_meshgl64, "manifold_alloc_meshgl64");
        bind(api.delete_meshgl64, "manifold_delete_meshgl64");
        bind(api.get_meshgl64, "manifold_get_meshgl64");
        bind(api.meshgl64_num_vert, "manifold_meshgl64_num_vert");
        bind(api.meshgl64_num_tri, "manifold_meshgl64_num_tri");
        bind(api.meshgl64_num_prop, "manifold_meshgl64_num_prop");
        bind(api.meshgl64_vert_properties_length, "manifold_meshgl64_vert_properties_length");
        bind(api.meshgl64_tri_length, "manifold_meshgl64_tri_length");
        bind(api.meshgl64_vert_properties, "manifold_meshgl64_vert_properties");
        bind(api.meshgl64_tri_verts, "manifold_meshgl64_tri_verts");

        api.loaded = complete;
    });

    return api;
}

} // namespace fabolus

#endif /* FABOLUS_MANIFOLD_DYNAMIC_HPP */
