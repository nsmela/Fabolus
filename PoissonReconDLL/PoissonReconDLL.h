#ifndef POISSON_RECON_DLL_H
#define POISSON_RECON_DLL_H

// Define POISSON_RECON_DLL_EXPORTS when building the DLL
#ifdef POISSON_RECON_DLL_EXPORTS
#define POISSON_RECON_DLL_API __declspec(dllexport)
#else
#define POISSON_RECON_DLL_API __declspec(dllimport)
#endif

// Use extern "C" to ensure a C-style linkage, preventing C++ name mangling.
// This makes the function easier to call from other languages and environments.
#ifdef __cplusplus
extern "C" {
#endif

	/**
	 * A wrapper function for the templated ReconstructPoisson<3>.
	 * It takes command-line style arguments to pass to the reconstruction engine.
	 * @param argc The number of arguments in argv.
	 * @param argv An array of C-style strings representing the arguments.
	 * @return An exit code, typically 0 for success.
	 */
	POISSON_RECON_DLL_API int Test(int argc, char* argv[]);

#ifdef __cplusplus
}
#endif

#endif // POISSON_RECON_DLL_H