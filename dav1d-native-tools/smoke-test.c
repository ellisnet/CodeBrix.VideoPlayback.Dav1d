/* =============================================================================================
 * smoke-test.c - proves a freshly built dav1d library actually loads and runs
 * =============================================================================================
 *
 * WHAT IT CHECKS, AND WHY IT IS WORTH HAVING
 *   Checking that a shared library exports the right symbol names only proves it compiled.
 *   This program loads it THE WAY .NET DOES - dlopen/LoadLibrary plus per-symbol lookup, with
 *   no link-time dependency - and then drives the real entry points:
 *
 *     1. load the library by path                     (the P/Invoke resolver's job, done by hand)
 *     2. resolve all 17 entry points the managed binding declares. Every one is REQUIRED:
 *        a missing symbol is a run-time crash in the field, not a build error here.
 *     3. dav1d_version()                              must return a non-empty string
 *     4. dav1d_version_api()                          major must be 7 (DAV1D_API_VERSION_MAJOR).
 *                                                     The managed side refuses to load anything
 *                                                     else, so a mismatch here is fatal.
 *     5. dav1d_default_settings(s)                    fill a settings block
 *     6. dav1d_get_frame_delay(s)                     must be >= 1 for valid settings
 *     7. dav1d_open(&c, s) / dav1d_close(&c)          a real decoder context: this starts the
 *                                                     worker threads and runs the CPU-feature
 *                                                     detection, which is where a badly built
 *                                                     library (wrong arch asm, bad relocation)
 *                                                     actually falls over.
 *
 *   Note on step 5: the settings block is a fixed 4 KB zeroed buffer rather than a declared
 *   Dav1dSettings struct, on purpose. This program deliberately does not #include dav1d.h, so
 *   it tests the shipped BINARY rather than the headers it was built from - the same position
 *   the managed binding is in. sizeof(Dav1dSettings) is under 200 bytes in every 7.x release,
 *   so 4 KB is a wide margin, and the buffer is over-aligned for good measure.
 *
 * USAGE
 *   Linux:    cc -O2 -o smoke-test smoke-test.c -ldl
 *   macOS:    cc -O2 -o smoke-test smoke-test.c
 *   Windows:  cl /nologo /O2 smoke-test.c
 *
 *   smoke-test <path-to-library>
 *
 * Exit code 0 = every check passed. Any failure prints what broke and exits non-zero.
 * No dav1d headers, no libraries to link against, no build system: one file, three platforms.
 * ============================================================================================= */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
    #include <windows.h>
    #define LIB_HANDLE          HMODULE
    #define LIB_OPEN(path)      LoadLibraryA(path)
    #define LIB_SYM(lib, name)  ((void*)GetProcAddress((lib), (name)))
    #define LIB_CLOSE(lib)      FreeLibrary(lib)
    #define LIB_ERROR()         "LoadLibrary failed (see GetLastError)"
#else
    #include <dlfcn.h>
    #define LIB_HANDLE          void*
    #define LIB_OPEN(path)      dlopen((path), RTLD_NOW | RTLD_LOCAL)
    #define LIB_SYM(lib, name)  dlsym((lib), (name))
    #define LIB_CLOSE(lib)      dlclose(lib)
    #define LIB_ERROR()         dlerror()
#endif

/* dav1d's public ABI is plain cdecl on every platform, which is the default here. */
typedef const char* (*fn_version)(void);
typedef unsigned    (*fn_version_api)(void);
typedef void        (*fn_default_settings)(void *settings);
typedef int         (*fn_open)(void **context_out, const void *settings);
typedef void        (*fn_close)(void **context_out);
typedef int         (*fn_get_frame_delay)(const void *settings);

/* The complete list the managed binding declares. Order matters only for readability. */
static const char *const REQUIRED_SYMBOLS[] = {
    /* the 13 the decoder binding calls */
    "dav1d_version",
    "dav1d_version_api",
    "dav1d_default_settings",
    "dav1d_open",
    "dav1d_parse_sequence_header",
    "dav1d_send_data",
    "dav1d_get_picture",
    "dav1d_apply_grain",
    "dav1d_flush",
    "dav1d_close",
    "dav1d_get_event_flags",
    "dav1d_get_decode_error_data_props",
    "dav1d_get_frame_delay",
    /* the four data/picture lifetime helpers the binding also needs */
    "dav1d_data_wrap",
    "dav1d_data_create",
    "dav1d_data_unref",
    "dav1d_picture_unref",
};
#define REQUIRED_SYMBOL_COUNT ((int)(sizeof(REQUIRED_SYMBOLS) / sizeof(REQUIRED_SYMBOLS[0])))

static int failures = 0;

static void check(int ok, const char *what)
{
    printf("  [%s] %s\n", ok ? "ok" : "FAIL", what);
    if (!ok) {
        failures++;
    }
}

int main(int argc, char **argv)
{
    LIB_HANDLE lib;
    void *sym;
    int i;
    char detail[512];

    /* Over-aligned, over-sized, zeroed settings block - see the note at the top. */
    #if defined(__GNUC__) || defined(__clang__)
        __attribute__((aligned(64))) static unsigned char settings[4096];
    #else
        static unsigned char settings[4096];
    #endif

    fn_version           p_version;
    fn_version_api       p_version_api;
    fn_default_settings  p_default_settings;
    fn_open              p_open;
    fn_close             p_close;
    fn_get_frame_delay   p_get_frame_delay;

    const char *version_string;
    unsigned api;
    unsigned api_major;
    int frame_delay;
    int rc;
    void *context = NULL;

    if (argc != 2) {
        fprintf(stderr, "usage: %s <path-to-dav1d-shared-library>\n", argv[0]);
        return 2;
    }

    printf("dav1d smoke test\n");
    printf("  library: %s\n", argv[1]);

    lib = LIB_OPEN(argv[1]);
    if (!lib) {
        fprintf(stderr, "  [FAIL] could not load the library: %s\n", LIB_ERROR());
        return 1;
    }
    check(1, "loaded the library");

    /* --- every required entry point must resolve ------------------------------------------- */
    for (i = 0; i < REQUIRED_SYMBOL_COUNT; i++) {
        sym = LIB_SYM(lib, REQUIRED_SYMBOLS[i]);
        if (!sym) {
            snprintf(detail, sizeof(detail), "missing export: %s", REQUIRED_SYMBOLS[i]);
            check(0, detail);
        }
    }
    if (failures == 0) {
        snprintf(detail, sizeof(detail), "all %d required exports resolve", REQUIRED_SYMBOL_COUNT);
        check(1, detail);
    }

    p_version          = (fn_version)          LIB_SYM(lib, "dav1d_version");
    p_version_api      = (fn_version_api)      LIB_SYM(lib, "dav1d_version_api");
    p_default_settings = (fn_default_settings) LIB_SYM(lib, "dav1d_default_settings");
    p_open             = (fn_open)             LIB_SYM(lib, "dav1d_open");
    p_close            = (fn_close)            LIB_SYM(lib, "dav1d_close");
    p_get_frame_delay  = (fn_get_frame_delay)  LIB_SYM(lib, "dav1d_get_frame_delay");

    if (!p_version || !p_version_api || !p_default_settings || !p_open || !p_close || !p_get_frame_delay) {
        fprintf(stderr, "  [FAIL] the entry points needed to run the rest of the test are missing.\n");
        LIB_CLOSE(lib);
        return 1;
    }

    /* --- version --------------------------------------------------------------------------- */
    version_string = p_version();
    check(version_string != NULL && version_string[0] != '\0', "dav1d_version() returns a version string");
    printf("      dav1d_version()     = %s\n", version_string ? version_string : "(null)");

    api = p_version_api();
    api_major = (api >> 16) & 0xFF;
    printf("      dav1d_version_api() = %u.%u.%u (0x%06x)\n",
           api_major, (api >> 8) & 0xFF, api & 0xFF, api);
    snprintf(detail, sizeof(detail), "API major is 7 (found %u)", api_major);
    check(api_major == 7, detail);

    /* --- a real decoder context ------------------------------------------------------------ */
    memset(settings, 0, sizeof(settings));
    p_default_settings(settings);
    check(1, "dav1d_default_settings() returned");

    frame_delay = p_get_frame_delay(settings);
    snprintf(detail, sizeof(detail), "dav1d_get_frame_delay() = %d (must be >= 1)", frame_delay);
    check(frame_delay >= 1, detail);

    rc = p_open(&context, settings);
    snprintf(detail, sizeof(detail), "dav1d_open() = %d, context %s", rc, context ? "allocated" : "NULL");
    check(rc == 0 && context != NULL, detail);

    if (rc == 0 && context != NULL) {
        p_close(&context);
        check(context == NULL, "dav1d_close() released the context");
    }

    LIB_CLOSE(lib);

    printf("\n");
    if (failures) {
        printf("  %d check(s) FAILED\n", failures);
        return 1;
    }
    printf("  all checks passed\n");
    return 0;
}
